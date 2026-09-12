import ctypes
import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'app'))
from process_runner import ProcessGroup, RenderCancelled


@unittest.skipUnless(os.name=='nt', 'Windows process ownership regression')
class ProcessOwnershipTests(unittest.TestCase):
    def test_cancellation_terminates_descendants_and_rejects_new_children(self):
        from ctypes import wintypes
        kernel=ctypes.WinDLL('kernel32',use_last_error=True)
        kernel.OpenProcess.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD]
        kernel.OpenProcess.restype=wintypes.HANDLE
        kernel.WaitForSingleObject.argtypes=[wintypes.HANDLE,wintypes.DWORD]
        kernel.CloseHandle.argtypes=[wintypes.HANDLE]
        handles=[]
        with tempfile.TemporaryDirectory(prefix='dlss-process-') as directory:
            root=Path(directory)
            grand="import os,sys,time;from pathlib import Path;Path(sys.argv[1]).write_text(str(os.getpid()));time.sleep(30)"
            child="import os,sys,time,subprocess;from pathlib import Path;Path(sys.argv[1]).write_text(str(os.getpid()));subprocess.Popen([sys.executable,'-c',sys.argv[2],sys.argv[3]]);time.sleep(30)"
            parent="import subprocess,sys,time;subprocess.Popen([sys.executable,'-c',*sys.argv[1:]]);time.sleep(30)"
            group=ProcessGroup()
            try:
                proc=group.start([sys.executable,'-c',parent,child,str(root/'child'),grand,str(root/'grand')],
                                 stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                end=time.monotonic()+5
                while not all((root/name).exists() for name in ('child','grand')) and time.monotonic()<end:
                    time.sleep(.05)
                for name in ('child','grand'):
                    pid=int((root/name).read_text());handle=kernel.OpenProcess(0x100000,False,pid)
                    self.assertTrue(handle);handles.append(handle)
                group.cancel();proc.wait(timeout=5)
                for handle in handles:self.assertEqual(kernel.WaitForSingleObject(handle,5000),0)
                with self.assertRaises(RenderCancelled):group.start([sys.executable,'-c','pass'])
            finally:
                group.close()
                for handle in handles:kernel.CloseHandle(handle)


if __name__=='__main__':unittest.main()
