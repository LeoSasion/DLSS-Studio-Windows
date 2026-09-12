"""Own a render pipeline and all of its child processes, including preview encoders."""
import os
import signal
import subprocess
import threading


class RenderCancelled(Exception):
    pass


class ProcessGroup:
    def __init__(self, cancelled=None):
        self.cancelled = cancelled or threading.Event()
        self.lock = threading.Lock()
        self.process = None
        self.handle = None
        if os.name == "nt":
            self._create_windows_job()

    def _create_windows_job(self):
        import ctypes as c
        from ctypes import wintypes as w
        class Basic(c.Structure):
            _fields_ = [("user", c.c_int64), ("job_user", c.c_int64), ("flags", w.DWORD),
                        ("min_ws", c.c_size_t), ("max_ws", c.c_size_t), ("active", w.DWORD),
                        ("affinity", c.c_size_t), ("priority", w.DWORD), ("scheduling", w.DWORD)]
        class IO(c.Structure):
            _fields_ = [(name, c.c_uint64) for name in ("read", "write", "other", "rb", "wb", "ob")]
        class Limits(c.Structure):
            _fields_ = [("basic", Basic), ("io", IO), ("pm", c.c_size_t), ("jm", c.c_size_t),
                        ("peak_pm", c.c_size_t), ("peak_jm", c.c_size_t)]
        k = c.WinDLL("kernel32", use_last_error=True)
        k.CreateJobObjectW.argtypes = [c.c_void_p, w.LPCWSTR]
        k.CreateJobObjectW.restype = w.HANDLE
        k.SetInformationJobObject.argtypes = [w.HANDLE, c.c_int, c.c_void_p, w.DWORD]
        k.AssignProcessToJobObject.argtypes = [w.HANDLE, w.HANDLE]
        k.TerminateJobObject.argtypes = [w.HANDLE, w.UINT]
        k.CloseHandle.argtypes = [w.HANDLE]
        self.kernel = k
        self.handle = k.CreateJobObjectW(None, None)
        limits = Limits()
        limits.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not self.handle or not k.SetInformationJobObject(self.handle, 9, c.byref(limits), c.sizeof(limits)):
            if self.handle:
                k.CloseHandle(self.handle)
                self.handle = None
            raise OSError("无法创建可取消的渲染进程。")

    def check(self):
        if self.cancelled.is_set():
            raise RenderCancelled()

    def start(self, args, **kwargs):
        with self.lock:
            self.check()
            kwargs.setdefault("creationflags", getattr(subprocess, "CREATE_NO_WINDOW", 0))
            if os.name != "nt":
                kwargs["start_new_session"] = True
            process = subprocess.Popen(args, **kwargs)
            self.process = process
            if self.handle and not self.kernel.AssignProcessToJobObject(self.handle, int(process._handle)):
                process.kill()
                process.wait()
                raise OSError("无法管理渲染进程，请重新启动服务。")
            return process

    def run(self, args, **kwargs):
        process = self.start(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, **kwargs)
        stdout, stderr = process.communicate()
        self.check()
        return subprocess.CompletedProcess(args, process.returncode, stdout, stderr)

    def cancel(self):
        self.cancelled.set()
        with self.lock:
            if self.handle:
                self.kernel.TerminateJobObject(self.handle, 1)
            elif self.process is not None and self.process.poll() is None:
                os.killpg(self.process.pid, signal.SIGKILL)

    def close(self):
        with self.lock:
            if self.handle:
                self.kernel.CloseHandle(self.handle)
                self.handle = None
            if self.process is not None:
                if self.process.poll() is None:
                    if os.name == "nt":
                        self.process.kill()
                    else:
                        os.killpg(self.process.pid, signal.SIGKILL)
                self.process.wait()
                for stream in (self.process.stdout, self.process.stderr):
                    if stream:
                        stream.close()
