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
        if (args.Contains("--integration-test")) return SelfTest.Integration(args).GetAwaiter().GetResult();
        using var singleton = new Mutex(true, AppPaths.MutexName, out bool first);
        if (!first) return 0;
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try {
            if (!PrivacyNotice.Accepted()) {
                // 升级后首次告知留到用户手动打开；开机不弹窗，也不继续联网或展示。
                if (args.Contains("--startup") || !PrivacyNotice.Show(requireConsent: true)) return 0;
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
        using var tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "班级看板", Visible = true };
        app.DispatcherUnhandledException += (_, e) => {
            // 运行异常只留在托盘状态，不让默认异常对话框打断课件；不记录正文或凭证。
            engine.State.Error="本机运行异常，请在托盘检查状态";
            AppPaths.QuietLog("runtime.log",e.Exception.GetType().Name);
            e.Handled=true;
        };
        var menu = new Forms.ContextMenuStrip();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        bool exiting = false;
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
            tray.Visible = false; app.Shutdown();
        }
        using var stopRequest = new EventWaitHandle(false, EventResetMode.AutoReset, AppPaths.StopEventName);
        var stopListener = ThreadPool.RegisterWaitForSingleObject(stopRequest, (_, _) => app.Dispatcher.BeginInvoke(() => Quit(true)), null, Timeout.Infinite, false);
        Window? statusWindow = null;
        void ShowStatus()
        {
            if (statusWindow is not null) { statusWindow.Activate(); return; }
            statusWindow = StatusWindow(engine); statusWindow.Closed += (_, _) => statusWindow = null; statusWindow.Show();
        }
        menu.Items.Add("查看状态与绑定", null, (_, _) => ShowStatus());
        menu.Items.Add("立即同步", null, (_, _) => engine.RequestSync());
        menu.Items.Add("本机暂停展示", null, (_, _) => engine.LocalPause(true));
        menu.Items.Add("本机恢复展示", null, (_, _) => engine.LocalPause(false));
        menu.Items.Add("减少材质效果", null, (_, _) => { engine.State.ReduceEffects = !engine.State.ReduceEffects; engine.Save(); engine.RequestSync(); });
        menu.Items.Add("隐私与本机数据", null, (_, _) => PrivacyNotice.Show(requireConsent: false));
        menu.Items.Add("卸载班级看板…", null, (_, _) => ClientLifecycle.StartUninstaller());
        menu.Items.Add("退出并恢复原壁纸", null, (_, _) => Quit(false));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => ShowStatus();
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
        // 仅首次未绑定时展示配置。已绑定的开机启动全过程保持安静。
        if (engine.State.Credential.Length == 0 && !args.Contains("--startup")) ShowStatus();
        app.Run(); timer.Stop();
        stopListener.Unregister(null);
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= networkChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= powerChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= preferenceChanged;
        engine.Dispose(); return 0;
    }
    private static Window StatusWindow(Engine engine)
    {
        var window = new Window { Title = AppPaths.DisplayName + " · 本机状态", Width = 560, Height = 740, ResizeMode = ResizeMode.CanMinimize, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var panel = new StackPanel { Margin = new Thickness(28) };
        void Label(string value, double size = 14) => panel.Children.Add(new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 9) });
        Label("班级看板", 26); Label("远程更新不会弹窗或切换课件。实际壁纸应用采用保守策略，等待前台返回桌面。");
        Label("服务地址（生产使用 HTTPS）"); var server = new TextBox { Text = engine.State.Server, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(8) }; panel.Children.Add(server);
        var output = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 16) };
        var qr = new System.Windows.Controls.Image { Width = 180, Height = 180, Margin = new Thickness(0, 10, 0, 10) };
        var pair = new Button { Content = "生成一次性绑定码", Padding = new Thickness(10), IsEnabled = engine.State.BindingId.Length == 0 };
        pair.Click += async (_, _) => {
            pair.IsEnabled = false;
            try {
                await engine.Pair(server.Text); output.Text = $"绑定码：{engine.State.PairCode}（10 分钟有效）\n手机入口：{engine.State.PairUrl}\n请在手机登录后核对班级与本机名称。";
                if (engine.State.PairQr.Length > 0) {
                    using var stream = new MemoryStream(Convert.FromBase64String(engine.State.PairQr));
                    var image = new System.Windows.Media.Imaging.BitmapImage(); image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); qr.Source = image;
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException) { output.Text = "配对未完成：" + e.Message; }
            finally { pair.IsEnabled = engine.State.BindingId.Length == 0; }
        };
        panel.Children.Add(pair); panel.Children.Add(output); panel.Children.Add(qr);
        var enable = new CheckBox { Content = "在本机启用壁纸展示（实机验收前仅用于测试）", IsChecked = engine.State.EnableWallpaper, Margin = new Thickness(0, 10, 0, 8) };
        enable.Click += (_, _) => { engine.State.EnableWallpaper = enable.IsChecked == true; engine.Save(); engine.RequestSync(); if (!engine.State.EnableWallpaper) { try { engine.RestoreWallpaper(); } catch (System.Runtime.InteropServices.COMException) { output.Text = "恢复壁纸失败，请检查系统桌面策略。"; } } };
        panel.Children.Add(enable);
        Label("目标显示器序号（从 0 开始，仅影响选定显示器）", 12);
        var monitorInput = new TextBox { Text = engine.State.MonitorIndex.ToString(), Padding = new Thickness(8) }; panel.Children.Add(monitorInput);
        var chooseMonitor = new Button { Content = "核对并选择显示器", Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 8) };
        chooseMonitor.Click += (_, _) => {
            try {
                if (!int.TryParse(monitorInput.Text, out int index) || index < 0) throw new InvalidOperationException("请输入有效的显示器序号。");
                var monitor = new Wallpaper().Read(index);
                if (index != engine.State.MonitorIndex) {
                    if (engine.State.EnableWallpaper) engine.RestoreWallpaper();
                    engine.State.MonitorIndex = index; engine.State.OriginalCaptured = false; engine.State.OriginalWallpaper = engine.State.OriginalMonitor = "";
                    engine.Save(); engine.RequestSync();
                }
                output.Text = $"已选择显示器 {index}，分辨率 {monitor.Rect.Right - monitor.Rect.Left} × {monitor.Rect.Bottom - monitor.Rect.Top}";
            } catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException) { output.Text = e.Message; }
        };
        panel.Children.Add(chooseMonitor);
        var startup = new CheckBox { Content = "随当前用户登录启动", Margin = new Thickness(0, 10, 0, 10) };
        using (var registry = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AppPaths.StartupKey)) startup.IsChecked = registry?.GetValue(AppPaths.ProductId) is not null;
        startup.Click += (_, _) => {
            try {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AppPaths.StartupKey);
                if (startup.IsChecked == true) key.SetValue(AppPaths.ProductId, $"\"{Environment.ProcessPath}\" --startup"); else key.DeleteValue(AppPaths.ProductId, false);
            } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException) { output.Text = "当前学校策略不允许设置启动项，请联系电教管理员。"; startup.IsChecked = false; }
        };
        panel.Children.Add(startup);
        var refresh = new Button { Content = "刷新本机状态", Padding = new Thickness(10), Margin = new Thickness(0, 10, 0, 0) };
        refresh.Click += (_, _) => output.Text = $"{engine.Status}\n缓存 v{engine.State.CachedVersion} / 应用 v{engine.State.AppliedVersion}\n第 {engine.State.Page} / {engine.State.PageCount} 页\n{(engine.State.LocalPaused ? "现场已暂停，远程不能解除" : "现场未暂停")}\n实际材质：{engine.State.EffectiveMaterial} {engine.State.MaterialReason}";
        panel.Children.Add(refresh);
        var update = new Button { Content = "检查程序版本", Padding = new Thickness(8), Margin = new Thickness(0, 10, 0, 0) };
        update.Click += async (_, _) => { try { output.Text = await engine.CheckVersion(); } catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { output.Text = "版本检查未完成，程序继续使用当前版本。"; } };
        panel.Children.Add(update);
        var privacy = new Button { Content = "隐私说明与卸载", Padding = new Thickness(8), Margin = new Thickness(0, 12, 0, 0) };
        privacy.Click += (_, _) => PrivacyNotice.Show(requireConsent: false); panel.Children.Add(privacy);
        Label("安装后需由学校管理员验证持久化目录、开机启动和重启恢复；不假设任何盘符不受还原保护。", 12);
        window.Content = new ScrollViewer { Content = panel }; return window;
    }
}

public static class SelfTest
{
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
