using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class DesktopShell : Form {
    readonly StudioService service = new StudioService();
    readonly WebView2 web = new WebView2();
    readonly CancellationTokenSource startup = new CancellationTokenSource();
    readonly Panel loading = new Panel();
    readonly Label message = new Label();
    readonly bool smoke;
    readonly uint activateMessage;
    bool starting = true, allowClose, smokeStarted, smokeDownloadComplete, documentLoaded;
    public int TestResult = 1;
    public DesktopShell(bool test, uint messageId) {
        smoke=test;activateMessage=messageId;
        service.LogName="desktop-server.log";service.InstanceFile="desktop-instance.json";
        Text="DLSS Studio";FormBorderStyle=FormBorderStyle.None;
        StartPosition=FormStartPosition.CenterScreen;Size=new Size(1320,820);MinimumSize=new Size(980,700);
        BackColor=Color.FromArgb(1,2,3);Padding=new Padding(4);
        Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        web.Dock=DockStyle.Fill;web.DefaultBackgroundColor=BackColor;Controls.Add(web);
        loading.Dock=DockStyle.Fill;loading.BackColor=BackColor;Controls.Add(loading);loading.BringToFront();
        message.Text="DLSS Studio\n\n正在准备工作台…";message.ForeColor=Color.White;
        message.Dock=DockStyle.Fill;message.TextAlign=ContentAlignment.MiddleCenter;
        message.Font=new Font(Font.FontFamily,19);loading.Controls.Add(message);
        var caption=new FlowLayoutPanel{Dock=DockStyle.Top,Height=40,FlowDirection=FlowDirection.RightToLeft,BackColor=BackColor};
        var close=new Button{Text="关闭",Width=64,FlatStyle=FlatStyle.Flat,ForeColor=Color.White};
        var minimize=new Button{Text="最小化",Width=64,FlatStyle=FlatStyle.Flat,ForeColor=Color.White};
        close.Click+=(s,e)=>Close();minimize.Click+=(s,e)=>WindowState=FormWindowState.Minimized;
        caption.Controls.Add(close);caption.Controls.Add(minimize);loading.Controls.Add(caption);caption.BringToFront();
        caption.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left)DragWindow();};
        Shown+=async(s,e)=>await Initialize();
        FormClosing+=OnClosing;
        FormClosed+=(s,e)=>{web.Dispose();service.Dispose();startup.Dispose();};
        if(smoke)ShowInTaskbar=false;
    }
    async Task Initialize() {
        try {
            bool missingRuntime=false;
            try { CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch(WebView2RuntimeNotFoundException) { missingRuntime=true; }
            if(missingRuntime) {
                if(smoke)throw new InvalidOperationException("WebView2 Runtime not installed");
                var choice=MessageBox.Show(this,"独立窗口需要 Microsoft WebView2。现在安装此组件？首次安装需要联网。","安装浏览器组件",MessageBoxButtons.YesNo,MessageBoxIcon.Information);
                if(choice!=DialogResult.Yes){allowClose=true;Close();return;}
                message.Text="正在安装 WebView2…\n请保持网络连接";
                var installer=Path.Combine(service.Root,"components","MicrosoftEdgeWebview2Setup.exe");
                using(var proc=Process.Start(new ProcessStartInfo(installer,"/silent /install"){UseShellExecute=false,CreateNoWindow=true})) {
                    await Task.Run(()=>proc.WaitForExit());
                    if(proc.ExitCode!=0)throw new IOException("WebView2 安装未完成，请检查网络后重试。");
                }
            }
            startup.Token.ThrowIfCancellationRequested();
            await service.Start(startup.Token);
            startup.Token.ThrowIfCancellationRequested();
            var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(service.Root,"desktop-profile"));
            await web.EnsureCoreWebView2Async(environment);
            startup.Token.ThrowIfCancellationRequested();
            var core=web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled=false;
            core.Settings.AreDevToolsEnabled=false;
            core.Settings.IsStatusBarEnabled=false;
            core.Settings.AreBrowserAcceleratorKeysEnabled=false;
            core.Settings.IsZoomControlEnabled=false;
            core.Settings.AreHostObjectsAllowed=false;
            core.Settings.IsPasswordAutosaveEnabled=false;
            core.Settings.IsGeneralAutofillEnabled=false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__DLSS_DESKTOP__=true;");
            core.NavigationStarting+=(s,e)=>{if(!OwnOrigin(e.Uri))e.Cancel=true;};
            core.NewWindowRequested+=(s,e)=>{e.Handled=true;};
            core.PermissionRequested+=(s,e)=>{e.State=CoreWebView2PermissionState.Deny;};
            if(smoke)core.DownloadStarting+=(s,e)=>{
                e.ResultFilePath=Path.Combine(service.Root,"desktop-downloaded-result.png");e.Handled=true;
                e.DownloadOperation.StateChanged+=(sender,args)=>{if(e.DownloadOperation.State==CoreWebView2DownloadState.Completed)smokeDownloadComplete=true;};
            };
            core.WebMessageReceived+=(s,e)=>{
                if(!OwnOrigin(e.Source))return;
                string action;try{action=e.TryGetWebMessageAsString();}catch{return;}
                if(action=="minimize")WindowState=FormWindowState.Minimized;
                else if(action=="open-browser") {
                    try { Process.Start(new ProcessStartInfo(service.Url){UseShellExecute=true}); }
                    catch(Exception ex){MessageBox.Show(this,ex.Message,"无法打开浏览器");}
                }
                else if(action=="close")Close();
                else if(action=="drag")DragWindow();
                else if(action=="maximize")ToggleMaximize();
                else if(action=="theme:light")BackColor=Color.FromArgb(233,235,238);
                else if(action=="theme:dark")BackColor=Color.FromArgb(1,2,3);
            };
            core.NavigationCompleted+=async(s,e)=>{
                // File downloads can finish a navigation with a cancellation status.
                // Keep an already loaded workbench visible in that case.
                if(!e.IsSuccess){if(!documentLoaded){message.Text="工作台加载失败\n请关闭窗口后重试";loading.Show();}return;}
                documentLoaded=true;
                loading.Hide();
                if(smoke && !smokeStarted){smokeStarted=true;await SmokeTest();}
            };
            core.ProcessFailed+=(s,e)=>{if(!IsDisposed){message.Text="浏览器组件已退出\n请重新打开 DLSS Studio";loading.Show();}};
            core.Navigate(service.Url);
        } catch(OperationCanceledException) { allowClose=true;Close(); }
        catch(Exception ex) {
            service.Stop();
            try{Directory.CreateDirectory(service.LogDir);File.AppendAllText(Path.Combine(service.LogDir,"desktop.log"),DateTime.Now+" "+ex+Environment.NewLine);}catch{}
            if(smoke){File.WriteAllText(Path.Combine(service.Root,"desktop-smoke-result.txt"),ex.ToString());allowClose=true;Close();}
            else message.Text="无法打开工作台\n\n"+ex.Message+"\n\n详情见 logs/desktop.log";
        } finally { starting=false; }
    }
    bool OwnOrigin(string value){Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri)&&uri.Scheme=="http"&&uri.Host=="127.0.0.1"&&uri.Port==service.Port;}
    void DragWindow(){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}
    void ToggleMaximize(){MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    void OnClosing(object sender,FormClosingEventArgs e){
        if(allowClose)return;
        if(starting){e.Cancel=true;startup.Cancel();return;}
        if(service.Busy() && MessageBox.Show(this,"关闭窗口会中断正在处理的任务，确定关闭？","关闭 DLSS Studio",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)e.Cancel=true;
    }
    protected override void WndProc(ref Message m){
        if((uint)m.Msg==activateMessage){WindowState=FormWindowState.Normal;Show();Activate();SetForegroundWindow(Handle);return;}
        base.WndProc(ref m);
        if(m.Msg==0x84 && WindowState==FormWindowState.Normal){
            var p=PointToClient(new Point(unchecked((short)m.LParam.ToInt64()),unchecked((short)(m.LParam.ToInt64()>>16))));
            int grip=4;bool left=p.X<grip,right=p.X>=ClientSize.Width-grip,top=p.Y<grip,bottom=p.Y>=ClientSize.Height-grip;
            int hit=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;
            if(hit!=0)m.Result=new IntPtr(hit);
        }
    }
    async Task<bool> Check(string expression,int seconds){
        for(int i=0;i<seconds*4;i++){if(await web.CoreWebView2.ExecuteScriptAsync("Boolean("+expression+")")=="true")return true;await Task.Delay(250);}return false;
    }
    async Task SmokeTest(){
        try {
            var core=web.CoreWebView2;
            if(!await Check("document.querySelector('.desktop-window-controls') && !document.querySelector('#empty-state').hidden",20))throw new Exception("Native controls or upload area missing");
            var normalSize=Size;Size=MinimumSize;await Task.Delay(250);
            if(!await Check("document.documentElement.scrollWidth<=innerWidth && document.documentElement.scrollHeight<=innerHeight && document.querySelector('.window-close').getBoundingClientRect().right<=innerWidth && document.querySelector('.inspector').scrollHeight<=document.querySelector('.inspector').clientHeight && document.querySelector('.open-browser')",3))throw new Exception("Compact window overflow");
            await core.ExecuteScriptAsync("document.querySelector('[data-mode=video]').click();document.querySelector('#video-settings details:last-child summary').click()");
            if(!await Check("document.querySelector('#settings-dialog').open && document.querySelector('#settings-dialog').scrollHeight<=document.querySelector('#settings-dialog').clientHeight && document.querySelector('#settings-form').elements.codec",3))throw new Exception("Video settings dialog overflow or form association lost");
            await core.ExecuteScriptAsync("document.querySelector('#settings-dialog .done').click()");
            await Task.Delay(100);
            if(!await Check("document.querySelector('.inspector').scrollHeight<=document.querySelector('.inspector').clientHeight",3))throw new Exception("Video inspector overflow");
            await core.ExecuteScriptAsync("document.querySelector('[data-mode=image]').click();document.querySelector('.advanced summary').click()");
            if(!await Check("document.querySelector('#settings-dialog').open && document.querySelector('#settings-form').elements.local_structure",3))throw new Exception("Advanced settings dialog failed");
            await core.ExecuteScriptAsync("document.querySelector('#settings-dialog .done').click()");
            await Task.Delay(100);
            Size=normalSize;await Task.Delay(250);
            await core.ExecuteScriptAsync("document.querySelector('[data-theme=light][type=button]').click()");
            if(!await Check("document.documentElement.dataset.theme==='light'",5))throw new Exception("Light theme failed");
            using(var file=File.Create(Path.Combine(service.Root,"desktop-light.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
            await core.ExecuteScriptAsync("document.querySelector('[data-theme=dark][type=button]').click()");
            if(!await Check("document.documentElement.dataset.theme==='dark'",5))throw new Exception("Dark theme failed");
            using(var file=File.Create(Path.Combine(service.Root,"desktop-dark.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
            if(!await Check("document.documentElement.scrollWidth<=innerWidth",2))throw new Exception("Horizontal overflow");
            // Exercise the actual upload input and processing button inside the embedded browser.
            await core.ExecuteScriptAsync("(async()=>{const b=await(await fetch('/assets/sample-lake.png')).blob();const d=new DataTransfer();d.items.add(new File([b],'desktop-test.png',{type:'image/png'}));const i=document.querySelector('#file-input');i.files=d.files;i.dispatchEvent(new Event('change',{bubbles:true}));})()");
            if(!await Check("!document.querySelector('#start-button').disabled",20))throw new Exception("Upload failed");
            await core.ExecuteScriptAsync("document.querySelector('#start-button').click()");
            if(!await Check("document.querySelector('#status-text').textContent==='处理完成'",100))throw new Exception("Embedded rendering failed");
            await core.ExecuteScriptAsync("document.querySelector('[data-view=compare]').click();document.querySelector('#close-notice').click()");
            if(!await Check("!document.querySelector('#compare-slider').hidden",5))throw new Exception("Compare mode failed");
            using(var file=File.Create(Path.Combine(service.Root,"desktop-result.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
            await core.ExecuteScriptAsync("document.querySelector('#download').click()");
            for(int i=0;i<80&&!smokeDownloadComplete;i++)await Task.Delay(250);
            if(!smokeDownloadComplete || new FileInfo(Path.Combine(service.Root,"desktop-downloaded-result.png")).Length<1024)throw new Exception("Download failed");
            if(loading.Visible)throw new Exception("Download hid the workbench");
            await core.ExecuteScriptAsync("document.querySelector('.window-minimize').click()");await Task.Delay(300);
            if(WindowState!=FormWindowState.Minimized)throw new Exception("Minimize command failed");
            WindowState=FormWindowState.Normal;
            if(OwnOrigin("https://example.com/")||OwnOrigin("file:///C:/"))throw new Exception("Origin validation failed");
            File.WriteAllText(Path.Combine(service.Root,"desktop-smoke-result.txt"),"PASS: borderless WebView2; compact image/video layout without overflow; settings dialogs and form fields; dark/light themes; real upload and render; compare; download; minimize; close requested; service_port="+service.Port);
            TestResult=0;
            await core.ExecuteScriptAsync("setTimeout(()=>document.querySelector('.window-close').click(),100)");
        } catch(Exception ex){File.WriteAllText(Path.Combine(service.Root,"desktop-smoke-result.txt"),ex.ToString());allowClose=true;Close();}
    }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
}

internal static class DesktopProgram {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] static int Main(string[] args){
        SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>0&&args[0]=="--smoke-test")return ServiceDiagnostics.Run(args);
        bool test=args.Length>0&&args[0]=="--desktop-smoke-test";
        using(var service=new StudioService()){
            string identity=service.Identity;uint activate=RegisterWindowMessage("DLSSStudioDesktop_"+identity);
            bool created;using(var mutex=new Mutex(true,"Local\\DLSSStudioDesktop_"+identity,out created)){
                if(!created){PostMessage(new IntPtr(0xffff),activate,IntPtr.Zero,IntPtr.Zero);return 0;}
                try{using(var form=new DesktopShell(test,activate)){Application.Run(form);return test?form.TestResult:0;}}
                finally{mutex.ReleaseMutex();}
            }
        }
    }
}
