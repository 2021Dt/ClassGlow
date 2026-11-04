using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using App = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace ClassroomBoard;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--prepare-uninstall")) return ClientLifecycle.PrepareUninstall(args.Contains("--silent"));
        if (args.Contains("--lifecycle-self-test")) return LifecycleChecks.Run(args);
        if (args.Contains("--render-preview")) return PreviewExport.Run(args);
        if (args.Contains("--self-test")) return SelfTest.Run(args);
        if (args.Contains("--network-self-test")) return SelfTest.Network(args).GetAwaiter().GetResult();
        if (args.Contains("--pairing-check")) return SelfTest.Pairing(args).GetAwaiter().GetResult();
        if (args.Contains("--integration-test")) return SelfTest.Integration(args).GetAwaiter().GetResult();
        if (args.Contains("--control-preview")) return ControlPreview.Run(args);
        if (args.Contains("--ui-preview")) return UiPreview.Run(args);
        try { return Run(args); }
        catch (Exception error) {
            // 主题、资源和窗口构造失败也须给出诊断，不能只剩一个空壳窗口。
            StartupDiagnostics.Report(error, "startup", args.Contains("--startup"));
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, AppPaths.ShowEventName);
        using var singleton = new Mutex(true, AppPaths.MutexName, out bool first);
        if (!first) {
            // 再点开始菜单快捷方式时唤回已有窗口，开机启动则继续保持安静。
            if (!args.Contains("--startup")) showRequest.Set();
            return 0;
        }
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Action? stopAfterUiFailure = null;
        bool uiFailed = false;
        app.DispatcherUnhandledException += (_, e) => {
            uiFailed = true;
            e.Handled = true;
            StartupDiagnostics.Report(e.Exception, "ui", args.Contains("--startup") && !app.Windows.Cast<Window>().Any(window => window.IsVisible));
            if (stopAfterUiFailure is not null) stopAfterUiFailure();
            else app.Shutdown(1);
        };
        AppWindow.InitializeTheme();
        try {
            if (!PrivacyNotice.Accepted()) {
                // 升级后首次告知留到用户手动打开；开机不弹窗，也不继续联网或展示。
                if (args.Contains("--startup") || !PrivacyNotice.Show(requireConsent: true)) return uiFailed ? 1 : 0;
            }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            if (!args.Contains("--startup")) MessageBox.Show("无法读取或保存本机同意记录，请检查当前用户目录权限。", "班级看板");
            return 1;
        }
        Engine engine;
        try { engine = new Engine(new StateStore()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException) {
            // 状态损坏不自动覆盖；只写诊断文件，不在上课时弹出窗口或通知。
            AppPaths.QuietLog("startup.log",e.GetType().Name);
            if (!args.Contains("--startup")) MessageBox.Show("本机状态无法读取。可从 Windows“已安装的应用”卸载并清理，或先由管理员备份检查。", "班级看板");
            return 1;
        }
        using var trayIcon = AppWindow.TrayIcon();
        using var tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "班级看板", Visible = true };
        using var menu = new Forms.ContextMenuStrip();
        using var statusWindow = new TrayWindowHost(() => StatusWindow(engine));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        bool exiting = false, exitingWithError = false;
        async void Quit(bool forUninstall)
        {
            if (exiting) return;
            exiting = true; timer.Stop();
            await engine.StopAsync();
            // 卸载助手在进程退出后恢复壁纸，随后才删除数据；普通退出在这里恢复。
            if (!forUninstall) {
                try { engine.RestoreWallpaper(); }
                catch (Exception e) when (e is IOException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { AppPaths.QuietLog("runtime.log", e.GetType().Name); }
            }
            tray.Visible = false; statusWindow.Dispose(); app.Shutdown(exitingWithError ? 1 : 0);
        }
        stopAfterUiFailure = () => { exitingWithError = true; Quit(false); };
        using var stopRequest = new EventWaitHandle(false, EventResetMode.AutoReset, AppPaths.StopEventName);
        var stopListener = ThreadPool.RegisterWaitForSingleObject(stopRequest, (_, _) => app.Dispatcher.BeginInvoke(() => Quit(true)), null, Timeout.Infinite, false);
        var showListener = ThreadPool.RegisterWaitForSingleObject(showRequest, (_, _) => app.Dispatcher.BeginInvoke(statusWindow.Open), null, Timeout.Infinite, false);
        menu.Items.Add("打开班级看板", null, (_, _) => statusWindow.Open());
        menu.Items.Add("立即同步", null, (_, _) => engine.RequestSync());
        menu.Items.Add("本机暂停展示", null, (_, _) => engine.LocalPause(true));
        menu.Items.Add("本机恢复展示", null, (_, _) => engine.LocalPause(false));
        menu.Items.Add("减少材质效果", null, (_, _) => {
            engine.State.ReduceEffects = !engine.State.ReduceEffects; engine.Save(); engine.RequestSync();
            foreach (var window in app.Windows.OfType<AppWindow>()) { window.ReduceMaterial = engine.State.ReduceEffects; window.RefreshMaterial(); }
        });
        menu.Items.Add("隐私与本机数据", null, (_, _) => PrivacyNotice.Show(requireConsent: false));
        menu.Items.Add("卸载班级看板…", null, (_, _) => ClientLifecycle.StartUninstaller());
        menu.Items.Add("退出并恢复原壁纸", null, (_, _) => Quit(false));
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) statusWindow.Open(); };
        timer.Tick += async (_, _) => {
            try { await engine.Tick(); tray.Text = ("班级看板 · " + engine.Status)[..Math.Min(60, ("班级看板 · " + engine.Status).Length)]; }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException) { engine.State.Error = "本机运行异常，请在托盘状态页检查"; }
        };
        timer.Start();
        // 网络、唤醒、显示和系统材质变化只触发后台重算，不弹窗或改变前台。
        System.Net.NetworkInformation.NetworkAvailabilityChangedEventHandler networkChanged = (_, e) => { if (e.IsAvailable) app.Dispatcher.BeginInvoke(engine.RequestSync); };
        Microsoft.Win32.PowerModeChangedEventHandler powerChanged = (_, e) => { if (e.Mode == Microsoft.Win32.PowerModes.Resume) app.Dispatcher.BeginInvoke(engine.RequestSync); };
        EventHandler displayChanged = (_, _) => app.Dispatcher.BeginInvoke(engine.RequestSync);
        Microsoft.Win32.UserPreferenceChangedEventHandler preferenceChanged = (_, e) => {
            // 设置壁纸本身也产生 Desktop 通知，忽略该类别，防止触发自我重绘循环。
            if(e.Category is Microsoft.Win32.UserPreferenceCategory.Accessibility or Microsoft.Win32.UserPreferenceCategory.Color or Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.VisualStyle) app.Dispatcher.BeginInvoke(engine.RequestSync);
        };
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += networkChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += powerChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displayChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += preferenceChanged;
        // 手动打开可查看状态；开机启动不弹窗打断教学。
        if (!args.Contains("--startup")) statusWindow.Open();
        int exitCode = app.Run(); timer.Stop();
        stopListener.Unregister(null);
        showListener.Unregister(null);
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= networkChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= powerChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= preferenceChanged;
        engine.Dispose(); return exitCode;
    }
    private static Window StatusWindow(Engine engine)
    {
        var window = new AppWindow("教室电脑") { ReduceMaterial = engine.State.ReduceEffects };
        window.SetBody(new ClientPanel(engine)); return window;
    }

}

public static class SelfTest
{
    public static async Task<int> Pairing(string[] args)
    {
        string? output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
        string? server = args.SkipWhile(a => a != "--server").Skip(1).FirstOrDefault();
        if (output is null || server is null) return 2;
        Directory.CreateDirectory(output);
        // 只在这个验证进程内模拟失效代理；不修改 Windows 代理、壁纸或真实客户端数据。
        HttpClient.DefaultProxy = new System.Net.WebProxy("http://127.0.0.1:7890") { BypassProxyOnLocal = false };
        using var engine = new Engine(new StateStore(output));
        engine.State.EnableWallpaper = false;
        try {
            await engine.Pair(server);
            bool paired = engine.State.PairCode.Length == 6;
            await engine.RevokeForUninstall();
            await engine.StopAsync();
            File.Delete(Path.Combine(output, "state.bin"));
            File.WriteAllText(Path.Combine(output, "pairing-result.json"), new JsonObject {
                ["passed"] = paired, ["proxyBypassed"] = true, ["pairingRevoked"] = true,
                ["server"] = server, ["wallpaperChanged"] = false }.ToJsonString());
            return paired ? 0 : 1;
        } catch (Exception e) {
            File.WriteAllText(Path.Combine(output, "pairing-error.txt"), e.ToString());
            return 1;
        }
    }
    public static async Task<int> Network(string[] args)
    {
        // 本地假服务只发响应头和部分正文，验证整体截止时间；不创建托盘或修改壁纸。
        string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault()
            ?? Path.Combine(Path.GetTempPath(), "ClassroomBoard-network-test");
        Directory.CreateDirectory(output);
        foreach (string bad in new[] { "ftp://127.0.0.1/", "file:///test", "http://example.com/", "https://user:pass@example.com/" })
        {
            try { Engine.ValidateServer(bad); return 1; }
            catch (InvalidOperationException) { /* 非 HTTP(S)、远端明文及 URL 内嵌口令都须拒绝。 */ }
        }
        if (new Uri(Engine.ValidateServer("https://example.com/api/"), "device/snapshot").AbsolutePath != "/api/device/snapshot") return 1;
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var response = Task.Run(async () => {
            using var connection = await listener.AcceptTcpClientAsync(stop.Token);
            await using var stream = connection.GetStream();
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1000\r\n\r\n{"), stop.Token);
            await Task.Delay(12000, stop.Token);
        });
        using var engine = new Engine(new StateStore(output));
        engine.State.EnableWallpaper = false;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await engine.Pair($"http://127.0.0.1:{((System.Net.IPEndPoint)listener.LocalEndpoint).Port}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            bool passed = timer.Elapsed.TotalSeconds is >= 7 and < 11;
            File.WriteAllText(Path.Combine(output, "network-result.json"), new JsonObject {
                ["passed"] = passed, ["addressChecks"] = 5, ["partialBodyTimeoutSeconds"] = timer.Elapsed.TotalSeconds,
                ["wallpaperChanged"] = false }.ToJsonString());
            return passed ? 0 : 1;
        }
        finally
        {
            stop.Cancel(); listener.Stop();
            try { await response; } catch (OperationCanceledException) { }
        }
    }
    public static async Task<int> Integration(string[] args)
    {
        string? output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
        string? server = args.SkipWhile(a => a != "--server").Skip(1).FirstOrDefault();
        if (output is null || server is null) return 2;
        Directory.CreateDirectory(output);
        using var engine = new Engine(new StateStore(output));
        // 测试入口强制关闭物理壁纸写入，即使测试目录曾保存过启用状态也不继承。
        engine.State.EnableWallpaper = false; engine.Save();
        try {
            await engine.Pair(server);
            File.WriteAllText(Path.Combine(output, "pairing.json"), new JsonObject { ["code"] = engine.State.PairCode }.ToJsonString());
            var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
            while (DateTimeOffset.UtcNow < deadline && !File.Exists(Path.Combine(output, "stop"))) {
                await engine.Tick();
                File.WriteAllText(Path.Combine(output, "status.json"), new JsonObject { ["deviceId"] = engine.State.DeviceId, ["cached"] = engine.State.CachedVersion, ["applied"] = engine.State.AppliedVersion,
                    ["wallpaperEnabled"] = engine.State.EnableWallpaper, ["state"] = engine.State.ApplyState }.ToJsonString());
                await Task.Delay(500);
            }
            return engine.State.CachedVersion > 0 && engine.State.AppliedVersion == 0 ? 0 : 1;
        } catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); return 1; }
    }
    // 该模式只生成临时 PNG，不创建托盘、设置壁纸或修改系统启动项。
    public static int Run(string[] args)
    {
        try {
            string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "ClassroomBoard-self-test");
            Directory.CreateDirectory(output);
            var state = new ClientState { LastContact = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), LastContentSync = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            string date = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd");
            state.Snapshot = JsonNode.Parse("""
            {"classVersion":1,"classroom":{"name":"测试班级（非真实数据）","layout":{"theme":"mica_light","material":"subtle","font":"medium","iconMargin":"left","interval":20,"modules":{"homework":true,"todo":true,"timetable":true,"countdown":true,"message":true},"reverse":false},"templates":[],"overrides":{},"message":"测试模式，不应用壁纸。"},"contents":[]}
            """)!.AsObject();
            var contents = state.Snapshot["contents"]!.AsArray();
            contents.Add(new JsonObject { ["id"] = "test-content", ["revision"] = 1, ["subject"] = "语文", ["body"] = new string('测', 500), ["kind"] = "homework", ["state"] = "published", ["from"] = date + "T00:00:00+08:00", ["until"] = DateTimeOffset.UtcNow.AddDays(1).ToString("O") });
            var renderer = new Renderer(output); var frame = renderer.Render(state, DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)), 1920, 1080);
            if (!File.Exists(frame.Path) || frame.Count < 2) throw new InvalidDataException("500 字正文未产生完整分页。");
            var wrapped = Renderer.Wrap(new string('测', 500), 36, 700);
            if (string.Concat(wrapped).Length != 500) throw new InvalidDataException("排版丢失正文。");
            int cases = 0;
            foreach (string theme in new[] { "mica_light", "mica_dark" }) foreach (string material in new[] { "subtle", "solid" })
            foreach (string font in new[] { "small", "medium", "large" }) foreach (var size in new[] { (1920, 1080), (1366, 768) }) {
                var layout = state.Snapshot["classroom"]!["layout"]!; layout["theme"] = theme; layout["material"] = material; layout["font"] = font;
                var testFrame = renderer.Render(state, DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)), size.Item1, size.Item2);
                if (testFrame.Count < 2 || !File.Exists(testFrame.Path)) throw new InvalidDataException("长文分页或目标尺寸验证失败。");
                state.PageSetId = testFrame.PageSetId;
                renderer.Render(state, DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)), size.Item1, size.Item2, testFrame.Count);
                cases++;
            }
            var baseClock=DateTimeOffset.Parse(date+"T12:00:00+08:00");
            state.Snapshot["classroom"]!["message"]=new string('寄',80);
            var startFrame=renderer.Render(state,baseClock,1920,1080); state.PageSetId=startFrame.PageSetId;state.AutoPageStartedAt=baseClock.ToUnixTimeSeconds();state.ManualUntil=0;
            if(renderer.Render(state,baseClock.AddSeconds(19),1920,1080).Page!=1 || renderer.Render(state,baseClock.AddSeconds(20),1920,1080).Page!=2) throw new InvalidDataException("新页面没有保持完整的 20 秒轮换周期");
            if(!Engine.IsTimeUncertain(baseClock.ToUnixTimeSeconds()-121,baseClock.ToUnixTimeSeconds())) throw new InvalidDataException("时间倒退保护未生效");
            File.WriteAllText(Path.Combine(output, "result.json"), new JsonObject { ["passed"] = true, ["cases"] = cases, ["timingChecks"]=2, ["pages"] = frame.Count, ["path"] = frame.Path, ["longMessagePath"]=startFrame.Path }.ToJsonString());
            return 0;
        } catch (Exception e) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "ClassroomBoard-self-test-error.txt"), e.ToString()); return 1; }
    }
}
