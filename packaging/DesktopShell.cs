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

internal sealed class CaptionButton : Button {
    readonly string action;
    public bool Maximized;
    public CaptionButton(string kind) {
        action=kind;bool close=kind=="close";Text="";AccessibleName=close?"关闭窗口":kind=="maximize"?"最大化窗口":"最小化窗口";
        Width=40;Height=32;Margin=new Padding(2,2,2,0);
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
        FlatAppearance.MouseOverBackColor=close?Color.FromArgb(196,43,55):Color.FromArgb(35,43,47);
        FlatAppearance.MouseDownBackColor=close?Color.FromArgb(156,30,40):Color.FromArgb(49,60,65);
        ForeColor=Color.FromArgb(222,233,238);BackColor=Color.FromArgb(1,2,3);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        float scale=DeviceDpi/96f,cx=ClientSize.Width/2f,cy=ClientSize.Height/2f,r=5*scale;
        e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var pen=new Pen(ForeColor,1.2f*scale)) {
            if(action=="close"){e.Graphics.DrawLine(pen,cx-r,cy-r,cx+r,cy+r);e.Graphics.DrawLine(pen,cx+r,cy-r,cx-r,cy+r);}
            else if(action=="maximize"){
                if(Maximized){
                    float d=2*scale;
                    e.Graphics.DrawLines(pen,new[]{new PointF(cx-r+d,cy-r+d),new PointF(cx-r+d,cy-r),new PointF(cx+r,cy-r),new PointF(cx+r,cy+r-d),new PointF(cx+r-d,cy+r-d)});
                    e.Graphics.DrawRectangle(pen,cx-r,cy-r+d,2*r-d,2*r-d);
                }else e.Graphics.DrawRectangle(pen,cx-r,cy-r,2*r,2*r);
            }
            else e.Graphics.DrawLine(pen,cx-r,cy,cx+r,cy);
        }
    }
}

internal sealed class DesktopShell : Form {
    readonly StudioService service = new StudioService();
    readonly WebView2 web = new WebView2();
    readonly CancellationTokenSource startup = new CancellationTokenSource();
    readonly TableLayoutPanel loading = new TableLayoutPanel();
    readonly Label message = new Label();
    readonly ToolTip captionTips = new ToolTip();
    readonly CaptionButton loadingMaximize = new CaptionButton("maximize");
    readonly bool smoke;
    readonly uint activateMessage;
    bool starting = true, allowClose, smokeStarted, smokeDownloadComplete, documentLoaded, transparentHost, glassMode, nativeBlurApplied, maximizedState, lightTheme;
    public int TestResult = 1;
    public DesktopShell(bool test, uint messageId) {
        smoke=test;activateMessage=messageId;
        service.LogName="desktop-server.log";service.InstanceFile="desktop-instance.json";
        Text="DLSS Studio";FormBorderStyle=FormBorderStyle.None;
        StartPosition=FormStartPosition.CenterScreen;Size=new Size(1320,820);MinimumSize=new Size(980,700);
        BackColor=Color.FromArgb(1,2,3);Padding=new Padding(4);
        Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        web.Dock=DockStyle.Fill;web.DefaultBackgroundColor=Color.Transparent;Controls.Add(web);
        loading.Dock=DockStyle.Fill;loading.BackColor=BackColor;loading.Margin=Padding.Empty;loading.Padding=Padding.Empty;
        loading.ColumnCount=1;loading.RowCount=2;loading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        loading.RowStyles.Add(new RowStyle(SizeType.Absolute,40));loading.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(loading);loading.BringToFront();
        message.Text="DLSS Studio\n\n正在准备工作台…";message.ForeColor=Color.White;
        message.Dock=DockStyle.Fill;message.TextAlign=ContentAlignment.MiddleCenter;
        message.Font=new Font(Font.FontFamily,19);message.Margin=Padding.Empty;loading.Controls.Add(message,0,1);
        var caption=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,Height=40,FlowDirection=FlowDirection.RightToLeft,BackColor=BackColor};
        var close=new CaptionButton("close");
        var minimize=new CaptionButton("minimize");
        captionTips.SetToolTip(close,"关闭");captionTips.SetToolTip(minimize,"最小化");captionTips.SetToolTip(loadingMaximize,"最大化");
        close.Click+=(s,e)=>Close();minimize.Click+=(s,e)=>WindowState=FormWindowState.Minimized;
        loadingMaximize.Click+=(s,e)=>ToggleMaximize();
        caption.Controls.Add(close);caption.Controls.Add(loadingMaximize);caption.Controls.Add(minimize);loading.Controls.Add(caption,0,0);
        caption.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left)DragWindow();};
        Resize+=(s,e)=>UpdateWindowState();
        Shown+=async(s,e)=>{
            if(smoke){
                using(var snapshot=new Bitmap(loading.Width,loading.Height)){
                    loading.DrawToBitmap(snapshot,new Rectangle(Point.Empty,loading.Size));
                    snapshot.Save(Path.Combine(service.Root,"desktop-preparing.png"),System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            await Initialize();
        };
        FormClosing+=OnClosing;
        FormClosed+=(s,e)=>{captionTips.Dispose();web.Dispose();service.Dispose();startup.Dispose();};
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
            var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(service.Root,smoke?"desktop-smoke-profile":"desktop-profile"));
            await web.EnsureCoreWebView2Async(environment);
            startup.Token.ThrowIfCancellationRequested();
            var core=web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled=false;
            core.Settings.AreDevToolsEnabled=false;
            core.Settings.IsStatusBarEnabled=false;
            core.Settings.AreBrowserAcceleratorKeysEnabled=true;
            core.Settings.IsZoomControlEnabled=true;
            core.Settings.AreHostObjectsAllowed=false;
            core.Settings.IsPasswordAutosaveEnabled=false;
            core.Settings.IsGeneralAutofillEnabled=false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__DLSS_DESKTOP__=true;");
            if(smoke)await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.LocalStorage);
            if(smoke)await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__appErrors=[];window.addEventListener('error',e=>{if(e.message)window.__appErrors.push(e.message)});window.addEventListener('unhandledrejection',e=>window.__appErrors.push(String(e.reason)));");
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
                else if(action=="window-state")NotifyWindowState();
                else if(action=="glass:on" || action=="glass:off"){
                    bool enabled=action=="glass:on";
                    if(glassMode!=enabled){glassMode=enabled;if(documentLoaded)ApplyGlassMode();}
                }
                else if(action=="theme:light" || action=="theme:dark"){
                    lightTheme=action=="theme:light";
                    if(!transparentHost)BackColor=WindowBackground;
                    if(maximizedState)web.DefaultBackgroundColor=WindowBackground;
                }
            };
            core.NavigationCompleted+=async(s,e)=>{
                // File downloads can finish a navigation with a cancellation status.
                // Keep an already loaded workbench visible in that case.
                if(!e.IsSuccess){if(!documentLoaded){message.Text="工作台加载失败\n请关闭窗口后重试";loading.Show();}return;}
                documentLoaded=true;
                ApplyGlassMode();
                NotifyWindowState();
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
    void EnableDesktopTransparency(){
        // Keep per-pixel WebView alpha, including shadows, instead of removing a key color.
        var region=CreateRectRgn(0,0,-1,-1);
        try{
            var blur=new DwmBlurBehind{Flags=3,Enable=true,Region=region};
            transparentHost=DwmEnableBlurBehindWindow(Handle,ref blur)==0;
        }finally{DeleteObject(region);}
        if(transparentHost){BackColor=Color.Black;Invalidate();}
    }
    Color WindowBackground{get{return lightTheme?Color.FromArgb(233,235,238):Color.FromArgb(1,2,3);}}
    void UpdateWindowState(){
        // Minimizing preserves whether the window will return maximized.
        if(WindowState==FormWindowState.Minimized)return;
        bool maximized=WindowState==FormWindowState.Maximized;
        loadingMaximize.Maximized=maximized;loadingMaximize.AccessibleName=maximized?"还原窗口":"最大化窗口";
        captionTips.SetToolTip(loadingMaximize,maximized?"还原":"最大化");loadingMaximize.Invalidate();
        if(maximizedState==maximized)return;
        maximizedState=maximized;
        if(documentLoaded){ApplyGlassMode();NotifyWindowState();}
    }
    void NotifyWindowState(){
        if(!IsDisposed && web.CoreWebView2!=null)web.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new {type="window-state",maximized=maximizedState}));
    }
    void ApplyGlassMode(){
        SetNativeBlur(false);nativeBlurApplied=false;
        if(maximizedState){
            var blur=new DwmBlurBehind{Flags=1,Enable=false};
            DwmEnableBlurBehindWindow(Handle,ref blur);transparentHost=false;
            web.DefaultBackgroundColor=WindowBackground;BackColor=WindowBackground;
        }else{
            web.DefaultBackgroundColor=Color.Transparent;
            EnableDesktopTransparency();
            nativeBlurApplied=glassMode && SetNativeBlur(true);
        }
    }
    bool SetNativeBlur(bool enabled){
        // CSS backdrop filters cannot sample the desktop behind a WebView window.
        var accent=new AccentPolicy{State=enabled?3:0};
        var memory=Marshal.AllocHGlobal(Marshal.SizeOf(accent));
        try{
            Marshal.StructureToPtr(accent,memory,false);
            var data=new CompositionAttributeData{Attribute=19,Data=memory,Size=new IntPtr(Marshal.SizeOf(accent))};
            return SetWindowCompositionAttribute(Handle,ref data);
        }catch(EntryPointNotFoundException){return false;}
        finally{Marshal.FreeHGlobal(memory);}
    }
    protected override CreateParams CreateParams{
        get{var style=base.CreateParams;style.ExStyle&=~0x20;return style;}
    }
    void DragWindow(){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}
    void ToggleMaximize(){MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    void OnClosing(object sender,FormClosingEventArgs e){
        if(allowClose)return;
        if(starting){e.Cancel=true;startup.Cancel();return;}
        if(service.Busy() && MessageBox.Show(this,"关闭窗口会中断正在处理的任务，确定关闭？","关闭 DLSS Studio",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)e.Cancel=true;
    }
    protected override void WndProc(ref Message m){
        if((uint)m.Msg==activateMessage){if(WindowState==FormWindowState.Minimized)WindowState=maximizedState?FormWindowState.Maximized:FormWindowState.Normal;Show();Activate();SetForegroundWindow(Handle);return;}
        base.WndProc(ref m);
        if(m.Msg==0x31E && documentLoaded)ApplyGlassMode();
        if(m.Msg==0x84){
            var p=PointToClient(new Point(unchecked((short)m.LParam.ToInt64()),unchecked((short)(m.LParam.ToInt64()>>16))));
            int grip=4;bool left=p.X<grip,right=p.X>=ClientSize.Width-grip,top=p.Y<grip,bottom=p.Y>=ClientSize.Height-grip;
            int hit=WindowState==FormWindowState.Normal?(top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0):0;
            // Transparent pixels still belong to this window; never return HTTRANSPARENT.
            if(ClientRectangle.Contains(p))m.Result=new IntPtr(hit!=0?hit:1);
        }
    }
    async Task<bool> Check(string expression,int seconds){
        for(int i=0;i<seconds*4;i++){if(await web.CoreWebView2.ExecuteScriptAsync("Boolean("+expression+")")=="true")return true;await Task.Delay(250);}return false;
    }
    async Task SmokeTest(){
        try {
            var core=web.CoreWebView2;
            if(!await Check("document.querySelector('.desktop-window-controls') && !document.querySelector('#empty-state').hidden",20))throw new Exception("Native controls or upload area missing");
            await core.ExecuteScriptAsync("if(document.documentElement.dataset.glass==='on')document.querySelector('#glass-toggle').click()");
            await Task.Delay(150);
            if(!transparentHost || !await Check("getComputedStyle(document.documentElement).backgroundColor==='rgba(0, 0, 0, 0)' && getComputedStyle(document.body).backgroundColor==='rgba(0, 0, 0, 0)'",3))throw new Exception("Desktop background is not transparent");
            var normalSize=Size;Size=MinimumSize;await Task.Delay(250);
            if(!await Check("document.documentElement.scrollWidth<=innerWidth && document.documentElement.scrollHeight<=innerHeight && document.querySelector('.window-close').getBoundingClientRect().right<=innerWidth && document.querySelector('.inspector').scrollHeight<=document.querySelector('.inspector').clientHeight && document.querySelector('.open-browser')",3))throw new Exception("Compact window overflow");
            if(!await Check("document.querySelector('#glass-toggle').getBoundingClientRect().right<document.querySelector('.theme-switch').getBoundingClientRect().left && document.querySelector('.mode-tabs').getBoundingClientRect().right+6<document.querySelector('.desktop-actions').getBoundingClientRect().left && document.querySelector('.engine-status').getBoundingClientRect().right+6<document.querySelector('.mode-tabs').getBoundingClientRect().left",3))throw new Exception("Compact appearance controls overlap navigation");
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
            await VerifyDesktopTransparency();
            await VerifyGlassMode();
            await VerifyMaximizedMode();
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
            await VerifyVideoWorkflow();
            await VerifyWorkbenchWorkflow();
            await core.ExecuteScriptAsync("document.querySelector('.window-minimize').click()");await Task.Delay(300);
            if(WindowState!=FormWindowState.Minimized)throw new Exception("Minimize command failed");
            WindowState=FormWindowState.Normal;
            if(OwnOrigin("https://example.com/")||OwnOrigin("file:///C:/"))throw new Exception("Origin validation failed");
            File.WriteAllText(Path.Combine(service.Root,"desktop-smoke-result.txt"),"PASS: maximize/restore with opaque background and preserved glass choice; native frosted glass and input hit testing; transparent desktop composition; borderless WebView2; compact image/video layout without overflow; settings dialogs and form fields; dark/light themes; real upload and render; compare; download; minimize; close requested; service_port="+service.Port);
            TestResult=0;
            await core.ExecuteScriptAsync("setTimeout(()=>document.querySelector('.window-close').click(),100)");
        } catch(Exception ex){File.WriteAllText(Path.Combine(service.Root,"desktop-smoke-result.txt"),ex.ToString());allowClose=true;Close();}
    }
    async Task VerifyVideoWorkflow(){
        var fixture=Environment.GetEnvironmentVariable("DLSS_STUDIO_TEST_VIDEO");
        if(String.IsNullOrEmpty(fixture))return;
        var core=web.CoreWebView2;
        var script=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fixture),"../video-workflow.js"));
        await core.ExecuteScriptAsync("window.__videoFixture="+new JavaScriptSerializer().Serialize(Convert.ToBase64String(File.ReadAllBytes(fixture)))+";");
        await core.ExecuteScriptAsync(File.ReadAllText(script));
        foreach(var phase in new[]{"frame","compare"}){
            if(!await Check("window.__videoTest && (window.__videoTest.phase==='"+phase+"' || window.__videoTest.error)",240))throw new Exception("Video workflow timed out: "+phase);
            var error=await core.ExecuteScriptAsync("window.__videoTest.error");
            if(error!="null")throw new Exception("Video workflow: "+error);
            using(var file=File.Create(Path.Combine(service.Root,"desktop-video-"+phase+".png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
            if(phase=="compare"){
                var previous=Size;Size=MinimumSize;await Task.Delay(250);
                if(!await Check("document.documentElement.scrollWidth<=innerWidth && document.querySelector('#video-stage').scrollWidth<=document.querySelector('#video-stage').clientWidth && document.querySelector('#frame-test-button').getBoundingClientRect().right<=document.querySelector('#canvas').getBoundingClientRect().right",3))throw new Exception("Compact video transport overflow");
                using(var file=File.Create(Path.Combine(service.Root,"desktop-video-compact.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
                Size=previous;
            }
            await core.ExecuteScriptAsync("window.__videoTest.resume=true");
        }
        if(!await Check("window.__videoTest.phase==='done'",5))throw new Exception("Video workflow did not finish");
        File.WriteAllText(Path.Combine(service.Root,"desktop-video-diagnostics.json"),await core.ExecuteScriptAsync("window.__videoTest.diagnostics"));
    }
    async Task VerifyWorkbenchWorkflow(){
        var script=Environment.GetEnvironmentVariable("DLSS_STUDIO_TEST_WORKBENCH");
        if(String.IsNullOrEmpty(script))return;
        var core=web.CoreWebView2;
        await core.ExecuteScriptAsync(File.ReadAllText(script));
        if(!await Check("window.__workbenchTest && (window.__workbenchTest.done || window.__workbenchTest.error)",150))throw new Exception("Workbench regression timed out");
        var error=await core.ExecuteScriptAsync("window.__workbenchTest.error");
        if(error!="null")throw new Exception("Workbench regression: "+error);
        File.WriteAllText(Path.Combine(service.Root,"desktop-workbench-diagnostics.json"),await core.ExecuteScriptAsync("window.__workbenchTest"));
        using(var file=File.Create(Path.Combine(service.Root,"desktop-history.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
        await core.ExecuteScriptAsync("document.querySelector('#history-dialog').close();saveSession();sessionStorage.setItem('smoke-result-id',state.output.id)");
        core.Reload();
        if(!await Check("typeof sessionReady!=='undefined' && sessionReady && typeof state!=='undefined' && state.output && state.output.id===sessionStorage.getItem('smoke-result-id') && !state.busy && !document.querySelector('#download').classList.contains('disabled')",30))throw new Exception("Reload did not restore downloadable result");
        var normal=Size;Size=MinimumSize;await Task.Delay(250);
        if(!await Check("document.documentElement.scrollWidth<=innerWidth && document.documentElement.scrollHeight<=innerHeight && document.querySelector('.preview-tools').getBoundingClientRect().right<=document.querySelector('.canvas-panel').getBoundingClientRect().right",3))throw new Exception("Workbench controls overflow compact window");
        using(var file=File.Create(Path.Combine(service.Root,"desktop-workbench-compact.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
        Size=normal;
        web.ZoomFactor=2;await Task.Delay(300);
        if(!await Check("document.documentElement.scrollWidth<=innerWidth && document.documentElement.scrollHeight<=innerHeight && document.querySelector('.brand-group').getBoundingClientRect().right<=document.querySelector('.desktop-actions').getBoundingClientRect().left",3))throw new Exception("200% text scale overflow");
        await core.ExecuteScriptAsync("document.querySelector('.mobile-settings').click()");
        if(!await Check("document.querySelector('.inspector').classList.contains('mobile-open') && document.querySelector('.inspector').scrollHeight<=document.querySelector('.inspector').clientHeight",3))throw new Exception("200% settings drawer overflow");
        using(var file=File.Create(Path.Combine(service.Root,"desktop-workbench-zoom200.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
        await core.ExecuteScriptAsync("document.querySelector('.inspector-close').click()");web.ZoomFactor=1;
        if(!await Check("window.__appErrors.length===0",3))throw new Exception("Browser script errors: "+await core.ExecuteScriptAsync("window.__appErrors"));
    }
    async Task VerifyDesktopTransparency(){
        // A solid test window behind the app proves actual desktop composition, not just CSS alpha.
        using(var backdrop=new Form{FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=Bounds,BackColor=Color.FromArgb(52,84,110)}){
            backdrop.Show();BringToFront();Activate();await Task.Delay(350);
            using(var snapshot=new Bitmap(Width,Height)){
                using(var graphics=Graphics.FromImage(snapshot))graphics.CopyFromScreen(Location,Point.Empty,Size);
                snapshot.Save(Path.Combine(service.Root,"desktop-transparent-native.png"),System.Drawing.Imaging.ImageFormat.Png);
                var pixel=snapshot.GetPixel(1,Height/2);
                if(Math.Abs(pixel.R-52)>2 || Math.Abs(pixel.G-84)>2 || Math.Abs(pixel.B-110)>2)throw new Exception("Desktop transparency did not reveal the test backdrop: "+pixel);
            }
        }
    }
    Bitmap CaptureNative(string filename){
        var snapshot=new Bitmap(Width,Height);
        using(var graphics=Graphics.FromImage(snapshot))graphics.CopyFromScreen(Location,Point.Empty,Size);
        snapshot.Save(Path.Combine(service.Root,filename),System.Drawing.Imaging.ImageFormat.Png);
        return snapshot;
    }
    static double StripeVariation(Bitmap bitmap){
        double total=0,squared=0;int count=0;
        for(int x=120;x<440;x++){var p=bitmap.GetPixel(x,210);double value=(p.R+p.G+p.B)/3.0;total+=value;squared+=value*value;count++;}
        return Math.Sqrt(squared/count-Math.Pow(total/count,2));
    }
    void VerifyInputOwnership(){
        foreach(var point in new[]{new Point(1,Height/2),new Point(8,8),new Point(100,210),new Point(Width-60,40)}){
            var screen=PointToScreen(point);var target=WindowFromPoint(screen);
            if(GetAncestor(target,2)!=Handle)throw new Exception("Transparent region passes input to another window: "+point);
            var packed=new IntPtr(unchecked((screen.Y<<16)|(screen.X&0xffff)));
            if(SendMessage(target,0x84,IntPtr.Zero,packed).ToInt64()==-1)throw new Exception("Transparent region returns HTTRANSPARENT: "+point);
        }
    }
    async Task VerifyGlassMode(){
        var core=web.CoreWebView2;
        using(var backdrop=new Form{FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=Bounds,BackColor=Color.FromArgb(57,91,119)}){
            backdrop.Paint+=(s,e)=>{
                using(var blue=new SolidBrush(Color.FromArgb(86,151,183)))e.Graphics.FillEllipse(blue,-120,280,980,820);
                using(var mauve=new SolidBrush(Color.FromArgb(171,119,142)))e.Graphics.FillEllipse(mauve,640,-200,1000,1050);
                for(int x=80;x<480;x+=16)e.Graphics.FillRectangle((x/16)%2==0?Brushes.White:Brushes.Black,x,170,16,100);
            };
            backdrop.Show();BringToFront();Activate();await Task.Delay(250);
            VerifyInputOwnership();
            await core.ExecuteScriptAsync("document.querySelector('#glass-toggle').click();document.activeElement.blur()");
            if(!await Check("document.querySelector('#glass-toggle').getAttribute('aria-pressed')==='true' && getComputedStyle(document.querySelector('.canvas-panel')).backdropFilter.includes('blur') && getComputedStyle(document.querySelector('.canvas-panel')).opacity==='1'",3) || !nativeBlurApplied)throw new Exception("Glass activation failed");
            core.Reload();
            if(!await Check("document.querySelector('#glass-toggle')?.getAttribute('aria-pressed')==='true' && document.querySelector('.desktop-window-controls') && localStorage.getItem('dlss-glass')==='on'",5))throw new Exception("Glass state did not persist");
            if(!await Check("[...document.querySelectorAll('.glass,button,button>.icon,.download')].every(e=>getComputedStyle(e).opacity==='1')",3))throw new Exception("Glass faded controls or text");
            await core.ExecuteScriptAsync("document.querySelector('.advanced summary').click()");
            if(!await Check("document.querySelector('#settings-dialog').open && getComputedStyle(document.querySelector('#settings-dialog')).backdropFilter.includes('blur')",3))throw new Exception("Settings dialog is not frosted");
            await core.ExecuteScriptAsync("document.querySelector('#settings-dialog .done').click();document.activeElement.blur()");
            SetNativeBlur(false);EnableDesktopTransparency();await Task.Delay(350);
            double clearVariation,blurredVariation;
            using(var snapshot=CaptureNative("desktop-glass-unblurred-test.png"))clearVariation=StripeVariation(snapshot);
            ApplyGlassMode();await Task.Delay(350);
            using(var snapshot=CaptureNative("desktop-glass-dark.png"))blurredVariation=StripeVariation(snapshot);
            VerifyInputOwnership();
            File.WriteAllText(Path.Combine(service.Root,"desktop-glass-diagnostics.json"),new JavaScriptSerializer().Serialize(new {clearVariation,blurredVariation,inputOwnership="PASS"}));
            if(clearVariation<10 || blurredVariation>clearVariation*.65)throw new Exception("Desktop blur did not soften backdrop stripes: "+clearVariation+" -> "+blurredVariation);
            await core.ExecuteScriptAsync("document.querySelector('[data-theme=light][type=button]').click()");await Task.Delay(250);
            using(var snapshot=CaptureNative("desktop-glass-light.png")){}
            VerifyInputOwnership();
            await core.ExecuteScriptAsync("document.querySelector('[data-theme=dark][type=button]').click();document.querySelector('#glass-toggle').click()");await Task.Delay(250);
            if(glassMode || nativeBlurApplied)throw new Exception("Glass did not turn off");
            VerifyInputOwnership();
        }
    }
    async Task VerifyMaximizedMode(){
        var core=web.CoreWebView2;
        var originalBounds=Bounds;
        await core.ExecuteScriptAsync("document.querySelector('#glass-toggle').click();document.querySelector('.window-maximize').click()");
        if(!await Check("document.documentElement.classList.contains('desktop-maximized') && document.querySelector('.window-maximize').getAttribute('aria-label')==='还原窗口' && document.querySelector('.window-maximize img').src.includes('restore.svg') && document.querySelector('#glass-toggle').disabled",5))throw new Exception("Maximize button or state synchronization failed");
        if(WindowState!=FormWindowState.Maximized || nativeBlurApplied || transparentHost || web.DefaultBackgroundColor.A!=255)throw new Exception("Maximized host is not opaque");
        if(Bounds!=Screen.FromHandle(Handle).WorkingArea)throw new Exception("Maximized bounds do not match the work area");
        string opaque="getComputedStyle(document.body).backgroundColor.startsWith('rgb(') && [...document.querySelectorAll('.glass,.segmented,select,input[type=number]')].every(e=>getComputedStyle(e).backgroundColor.startsWith('rgb(') && getComputedStyle(e).opacity==='1') && [...document.querySelectorAll('button,button>.icon,.download')].every(e=>getComputedStyle(e).opacity==='1') && getComputedStyle(document.querySelector('.canvas-panel')).backdropFilter==='none'";
        if(!await Check(opaque,3))throw new Exception("Maximized page still has translucent surfaces");
        BringToFront();Activate();await Task.Delay(250);VerifyInputOwnership();
        using(var file=File.Create(Path.Combine(service.Root,"desktop-maximized-dark.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
        await core.ExecuteScriptAsync("document.querySelector('[data-theme=light][type=button]').click()");
        if(!await Check("document.documentElement.dataset.theme==='light' && "+opaque,3) || BackColor!=Color.FromArgb(233,235,238))throw new Exception("Maximized light theme is not opaque");
        using(var file=File.Create(Path.Combine(service.Root,"desktop-maximized-light.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
        await core.ExecuteScriptAsync("document.querySelector('.window-minimize').click()");await Task.Delay(200);
        if(WindowState!=FormWindowState.Minimized || !maximizedState)throw new Exception("Minimize lost the maximized state");
        WindowState=FormWindowState.Maximized;await Task.Delay(200);
        await core.ExecuteScriptAsync("document.querySelector('[data-theme=dark][type=button]').click();document.querySelector('.window-maximize').click()");
        if(!await Check("!document.documentElement.classList.contains('desktop-maximized') && document.querySelector('.window-maximize').getAttribute('aria-label')==='最大化窗口' && !document.querySelector('#glass-toggle').disabled && document.querySelector('#glass-toggle').getAttribute('aria-pressed')==='true' && getComputedStyle(document.querySelector('.canvas-panel')).backgroundColor==='rgba(0, 0, 0, 0.85)' && getComputedStyle(document.querySelector('#upload-button')).backgroundColor==='rgba(0, 0, 0, 0.95)'",5))throw new Exception("Restore did not preserve glass opacity and button state");
        if(Bounds!=originalBounds || !transparentHost || !nativeBlurApplied || !glassMode)throw new Exception("Restore did not recover native glass or window bounds");
        await core.ExecuteScriptAsync("document.querySelector('#glass-toggle').click();document.querySelector('.topbar').dispatchEvent(new MouseEvent('mousedown',{bubbles:true,button:0,detail:2}))");
        if(!await Check("document.documentElement.classList.contains('desktop-maximized')",3) || glassMode || nativeBlurApplied || transparentHost)throw new Exception("Title bar maximize failed with glass off");
        await core.ExecuteScriptAsync("document.querySelector('.window-maximize').click()");
        if(!await Check("!document.documentElement.classList.contains('desktop-maximized') && document.querySelector('#glass-toggle').getAttribute('aria-pressed')==='false'",3) || glassMode || nativeBlurApplied || !transparentHost)throw new Exception("Restore enabled glass unexpectedly");
    }
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy{public int State,Flags,Color,Animation;}
    [StructLayout(LayoutKind.Sequential)] struct CompositionAttributeData{public int Attribute;public IntPtr Data,Size;}
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowCompositionAttribute(IntPtr window,ref CompositionAttributeData data);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [StructLayout(LayoutKind.Sequential)] struct DwmBlurBehind{public uint Flags;[MarshalAs(UnmanagedType.Bool)] public bool Enable;public IntPtr Region;[MarshalAs(UnmanagedType.Bool)] public bool TransitionOnMaximized;}
    [DllImport("dwmapi.dll")] static extern int DwmEnableBlurBehindWindow(IntPtr window,ref DwmBlurBehind blur);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
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
