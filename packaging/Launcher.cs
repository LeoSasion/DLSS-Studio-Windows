using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;

internal sealed class StudioService : IDisposable {
    public readonly string Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    public string AppDir { get { return Path.Combine(Root, "app"); } }
    public string LogDir { get { return Path.Combine(Root, "logs"); } }
    public string Identity { get { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(AppDir.ToLowerInvariant()))).Replace("-", "").ToLowerInvariant(); } }
    public int Port = 7860;
    public string LogName = "server.log";
    public string InstanceFile = "instance.json";
    public string Url { get { return "http://127.0.0.1:"+Port+"/"; } }
    Process process;
    IntPtr job;
    StreamWriter log;
    readonly object gate = new object();
    public bool Running { get { return process != null && !process.HasExited; } }
    public Dictionary<string, object> Health(int port) {
        try {
            var req=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+port+"/api/health");
            req.Timeout=700; req.ReadWriteTimeout=700; req.Proxy=null;
            using(var res=req.GetResponse()) using(var reader=new StreamReader(res.GetResponseStream()))
                return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
        } catch { return null; }
    }
    public bool Ready() { var h=Health(Port); return h!=null && h.ContainsKey("instance") && (string)h["instance"]==Identity; }
    public bool Busy() { var h=Health(Port); return h!=null && h.ContainsKey("busy") && (bool)h["busy"]; }
    public void Validate() {
        foreach(var file in new[]{"runtime/python.exe","runtime/python310.dll","app/serve_local.py","app/studio_server.py","app/web/index.html","app/out/video2dlssnr.exe","app/out/nvngx_dlssnr.dll","app/out/nvngx_dlss.dll","app/out/ffmpeg.exe","app/out/ffprobe.exe"})
            if(!File.Exists(Path.Combine(Root,file))) throw new IOException("缺少文件："+file+"。请完整解压 ZIP，勿单独移动 EXE。");
        Directory.CreateDirectory(LogDir);
        var probe=Path.Combine(LogDir,".write-check"); File.WriteAllText(probe,"ok"); File.Delete(probe);
    }
    public async Task Start(CancellationToken cancellation = default(CancellationToken)) {
        if(Running) return;
        Validate();
        bool found=false;
        for(int p=7860;p<7960;p++) {
            try { var listener=new TcpListener(IPAddress.Loopback,p); listener.Start(); listener.Stop(); Port=p; found=true; break; }
            catch(SocketException) { }
        }
        if(!found) throw new IOException("7860–7959 端口均不可用，请关闭占用端口的程序后重试。");
        log=new StreamWriter(Path.Combine(LogDir,LogName),true,new UTF8Encoding(false)); log.AutoFlush=true;
        WriteLog("\n--- "+DateTime.Now.ToString("s")+" 服务启动，端口 "+Port+" ---");
        var info=new ProcessStartInfo(Path.Combine(Root,"runtime/python.exe"),"-X utf8 -u -B \""+Path.Combine(AppDir,"serve_local.py")+"\"");
        info.WorkingDirectory=AppDir; info.UseShellExecute=false; info.CreateNoWindow=true;
        info.RedirectStandardOutput=true; info.RedirectStandardError=true;
        info.StandardOutputEncoding=Encoding.UTF8; info.StandardErrorEncoding=Encoding.UTF8;
        info.EnvironmentVariables["DLSS_STUDIO_PORT"]=Port.ToString();
        info.EnvironmentVariables["GRADIO_ANALYTICS_ENABLED"]="False";
        info.EnvironmentVariables["HF_HUB_OFFLINE"]="1";
        info.EnvironmentVariables["PYTHONUTF8"]="1";
        process=new Process(); process.StartInfo=info;
        process.OutputDataReceived+=(s,e)=>{if(e.Data!=null) WriteLog(e.Data);};
        process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null) WriteLog(e.Data);};
        try {
            job=CreateJobObject(IntPtr.Zero,null);
            if(job==IntPtr.Zero) throw new IOException("无法创建服务进程组。");
            var limits=new JOBOBJECT_EXTENDED_LIMIT_INFORMATION(); limits.BasicLimitInformation.LimitFlags=0x2000;
            int size=Marshal.SizeOf(limits); IntPtr ptr=Marshal.AllocHGlobal(size);
            try { Marshal.StructureToPtr(limits,ptr,false); if(!SetInformationJobObject(job,9,ptr,(uint)size)) throw new IOException("无法配置服务进程组。"); }
            finally { Marshal.FreeHGlobal(ptr); }
            process.Start();
            if(!AssignProcessToJobObject(job,process.Handle)) { process.Kill(); throw new IOException("无法管理后台服务进程，请重新启动窗口。"); }
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            for(int i=0;i<120;i++) {
                cancellation.ThrowIfCancellationRequested();
                if(process.HasExited) throw new IOException("服务启动失败（退出码 "+process.ExitCode+"）。请点击“查看日志”。");
                if(await Task.Run(()=>Ready())) {
                    File.WriteAllText(Path.Combine(LogDir,InstanceFile),new JavaScriptSerializer().Serialize(new {port=Port,instance=Identity}),Encoding.UTF8);
                    return;
                }
                await Task.Delay(1000,cancellation);
            }
            throw new IOException("启动等待超过 120 秒，请查看日志后重试。");
        } catch { Stop(); throw; }
    }
    void WriteLog(string line) { lock(gate) { if(log!=null) log.WriteLine(line); } }
    public void Stop() {
        if(job!=IntPtr.Zero) { CloseHandle(job); job=IntPtr.Zero; }
        if(process!=null) {
            try { if(!process.HasExited) { process.Kill(); process.WaitForExit(5000); } } catch { }
            process.Dispose(); process=null;
        }
        lock(gate) { if(log!=null) { log.Dispose(); log=null; } }
    }
    public void Dispose() { Stop(); }
    [StructLayout(LayoutKind.Sequential)] struct JOBOBJECT_BASIC_LIMIT_INFORMATION { public long PerProcessUserTimeLimit,PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass,SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] struct IO_COUNTERS { public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION { public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo; public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a,string name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job,int type,IntPtr data,uint size);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}

internal sealed class Launcher : Form {
    readonly StudioService service=new StudioService();
    Label status,address; Button start,stop,open; bool starting;
    public Launcher() {
        Text="WebUI启动器 · DLSS Studio"; ClientSize=new Size(620,432);
        FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false; StartPosition=FormStartPosition.CenterScreen;
        AutoScaleMode=AutoScaleMode.Dpi; Font=new Font("Microsoft YaHei UI",10);
        BackColor=Color.FromArgb(239,241,244); ForeColor=Color.FromArgb(32,44,59);
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var title=new Label{Text="DLSS Studio",Font=new Font(Font.FontFamily,25,FontStyle.Bold),Location=new Point(30,25),Size=new Size(480,50)};
        Controls.Add(title);
        Controls.Add(new Label{Text="WebUI启动器   /   在浏览器中打开工作台",Location=new Point(33,80),Size=new Size(500,26),ForeColor=Color.FromArgb(104,114,128)});
        var card=new Panel{Location=new Point(30,128),Size=new Size(560,96),BackColor=Color.White};
        card.Paint+=(s,e)=>{using(var pen=new Pen(Color.FromArgb(220,225,231))) e.Graphics.DrawRectangle(pen,0,0,card.Width-1,card.Height-1);};
        status=new Label{Text="准备就绪",Font=new Font(Font.FontFamily,14,FontStyle.Bold),Location=new Point(20,17),Size=new Size(520,30)};
        address=new Label{Text="点击下方按钮，启动后自动打开浏览器",Location=new Point(20,54),Size=new Size(520,26),ForeColor=Color.FromArgb(106,116,131)};
        card.Controls.Add(status);card.Controls.Add(address);Controls.Add(card);
        start=MakeButton("启动并打开",30,244,270,54,true); stop=MakeButton("停止服务",316,244,274,54,false); stop.Enabled=false;
        open=MakeButton("打开网页",30,316,174,42,false);open.Enabled=false;
        var output=MakeButton("输出文件夹",222,316,176,42,false);
        var logs=MakeButton("查看日志",416,316,174,42,false);
        Controls.Add(new Label{Text="运行环境已内置 · 保持此窗口开启即可持续使用",Location=new Point(30,383),Size=new Size(560,24),ForeColor=Color.FromArgb(113,121,135),Font=new Font(Font.FontFamily,9)});
        start.Click+=async(s,e)=>await StartService();
        stop.Click+=(s,e)=>{if(service.Busy() && MessageBox.Show(this,"正在处理素材，停止服务会中断当前任务。确定停止？","停止服务",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;service.Stop();SetStopped("服务已停止");};
        open.Click+=(s,e)=>Open(service.Url);
        output.Click+=(s,e)=>{var path=Path.Combine(service.AppDir,"ui_out");Directory.CreateDirectory(path);Open(path);};
        logs.Click+=(s,e)=>{Directory.CreateDirectory(service.LogDir);Open(service.LogDir);};
        FormClosing+=(s,e)=>{if(starting){e.Cancel=true;return;} if(service.Running && MessageBox.Show(this,"关闭启动窗口将停止服务"+(service.Busy()?"并中断当前处理任务":"")+"。确定关闭？","关闭 DLSS Studio",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes){e.Cancel=true;return;} service.Dispose();};
        var timer=new System.Windows.Forms.Timer{Interval=2000};
        timer.Tick+=(s,e)=>{if(!starting && stop.Enabled && !service.Running)SetStopped("服务已退出，请查看日志");}; timer.Start();
    }
    Button MakeButton(string text,int x,int y,int w,int h,bool primary){
        var b=new Button{Text=text,Location=new Point(x,y),Size=new Size(w,h),FlatStyle=FlatStyle.Flat,BackColor=primary?Color.FromArgb(30,117,103):Color.White,ForeColor=primary?Color.White:ForeColor,Cursor=Cursors.Hand};
        b.FlatAppearance.BorderColor=primary?Color.FromArgb(30,117,103):Color.FromArgb(210,216,224); b.FlatAppearance.BorderSize=1; Controls.Add(b);return b;
    }
    async Task StartService(){
        starting=true;start.Enabled=false;status.Text="正在启动…";address.Text="首次启动可能需要几十秒，请稍候";
        try{await service.Start();status.Text="服务运行中";address.Text=service.Url;stop.Enabled=true;open.Enabled=true;Open(service.Url);}
        catch(Exception ex){SetStopped("启动失败");MessageBox.Show(this,ex.Message,"DLSS Studio",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally{starting=false;start.Enabled=!service.Running;}
    }
    void SetStopped(string text){status.Text=text;address.Text="点击启动即可重新打开工作台";start.Enabled=true;stop.Enabled=false;open.Enabled=false;}
    static void Open(string target){try{Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(ex.Message,"无法打开");}}
    public void CapturePreview(string path){Opacity=0;ShowInTaskbar=false;Show();Application.DoEvents();using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(Point.Empty,Size));image.Save(path);}Hide();}
}

internal static class Program {
    [STAThread] static int Main(string[] args){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>0 && args[0]=="--smoke-test"){
            var service=new StudioService();
            try{
                using(var form=new Launcher()) form.CapturePreview(Path.Combine(service.Root,"launcher-preview.png"));
                Task.Run(()=>service.Start()).GetAwaiter().GetResult();
                var port=service.Port;
                File.WriteAllText(Path.Combine(service.Root,"smoke-ready.txt"),service.Url);
                // Optional hold allows the package test to exercise the real API before shutdown.
                if(args.Length>1 && args[1]=="--hold"){
                    for(int i=0;i<180 && !File.Exists(Path.Combine(service.Root,"smoke-release.txt"));i++)Thread.Sleep(1000);
                }
                service.Stop();Thread.Sleep(1000);
                if(service.Health(port)!=null)throw new Exception("服务停止后端口仍在响应");
                File.WriteAllText(Path.Combine(service.Root,"smoke-result.txt"),"PASS: launcher rendered; portable service ready; service stopped. Port="+port);
                return 0;
            }catch(Exception ex){File.WriteAllText(Path.Combine(service.Root,"smoke-result.txt"),ex.ToString());return 1;}
            finally{service.Dispose();}
        }
        using(var service=new StudioService()){
            bool created;using(var mutex=new Mutex(true,"Local\\DLSSStudio_"+service.Identity,out created)){
                if(!created){
                    try{var state=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(service.LogDir,"instance.json")));service.Port=Convert.ToInt32(state["port"]);if(service.Ready()){Process.Start(service.Url);return 0;}}catch{}
                    MessageBox.Show("此部署包的启动窗口已打开，请从任务栏切换到 DLSS Studio。","DLSS Studio");return 0;
                }
                try{Application.Run(new Launcher());}finally{mutex.ReleaseMutex();}
            }
        }
        return 0;
    }
}
