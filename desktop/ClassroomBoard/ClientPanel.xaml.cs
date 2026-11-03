using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ClassroomBoard;

/// <summary>首次配置逐页引导，完成后只呈现运行状态；预览模式不联网或写入系统。</summary>
public partial class ClientPanel : System.Windows.Controls.UserControl
{
    private readonly Engine engine;
    private readonly bool preview;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool pairing, setup, preferencesLoaded;
    private string page = "", displayedQr = "";
    private sealed record MonitorOption(int Index, string Label);
    private bool Bound => engine.State.BindingId.Length > 0;

    public ClientPanel(Engine engine, bool preview = false)
    {
        this.engine = engine; this.preview = preview; setup = !Bound || engine.State.SetupCompleted == false;
        InitializeComponent();
        ServerInput.Text = engine.State.Server;
        ShowPage(setup ? Bound ? "display" : "connect" : "home");
        timer.Tick += (_, _) => RefreshState();
        Loaded += (_, _) => { RefreshState(); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
    }

    private void ShowPage(string target)
    {
        page = target; FeedbackBox.Visibility = Visibility.Collapsed;
        ConnectPage.Visibility = target == "connect" ? Visibility.Visible : Visibility.Collapsed;
        DisplayPage.Visibility = target == "display" ? Visibility.Visible : Visibility.Collapsed;
        HomePage.Visibility = target == "home" ? Visibility.Visible : Visibility.Collapsed;
        WizardActions.Visibility = target == "home" ? Visibility.Collapsed : Visibility.Visible;
        PageProgress.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
        AppWindow.SetProgress(PageProgress, target == "connect" ? 2 : 3);
        PageArt.Source = (ImageSource)FindResource(target == "connect" ? "GlowConnectArt" : "GlowDisplayArt");
        PageTitle.Text = target switch { "connect" => "连接班级", "display" => "展示设置", _ => "班级看板" };
        BackButton.Content = setup ? target == "display" ? "上一步" : "稍后绑定" : "返回状态";
        NextButton.Content = target == "display" ? setup ? "完成" : "保存" : setup ? "继续" : "返回";
        if (target == "display" && (!setup || !preferencesLoaded)) { LoadPreferences(); preferencesLoaded = true; }
        RefreshState();
        PageScroll.ScrollToTop();
        // 页面切换只做短淡入，遵从系统关闭动画的设置，不动画化教学内容。
        if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            PageBody.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void LoadPreferences()
    {
        // 只在进入本页时读取；后台状态刷新不能覆盖老师尚未保存的选择。
        WallpaperEnabled.IsChecked = engine.State.EnableWallpaper;
        MonitorPanel.Visibility = WallpaperEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        try {
            var monitors = preview ? new[] { new MonitorOption(0, "显示器 1 · 1920 × 1080（示例）") }
                : new Wallpaper().Monitors().Select(m => new MonitorOption(m.Index, $"显示器 {m.Index + 1} · {m.Width} × {m.Height}")).ToArray();
            MonitorChoice.ItemsSource = monitors; MonitorChoice.DisplayMemberPath = nameof(MonitorOption.Label);
            MonitorChoice.SelectedItem = monitors.FirstOrDefault(m => m.Index == engine.State.MonitorIndex);
            if (!preview) {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AppPaths.StartupKey);
                StartupEnabled.IsChecked = key?.GetValue(AppPaths.ProductId) is not null;
            } else StartupEnabled.IsChecked = false;
        } catch (Exception error) when (error is COMException or UnauthorizedAccessException or System.Security.SecurityException) {
            Tell("暂时无法读取本机设置，请检查系统显示设置或联系电教管理员。");
        }
    }
    private void Tell(string text) { Feedback.Text = text; FeedbackBox.Visibility = Visibility.Visible; }
    private bool PreviewOnly()
    {
        if (!preview) return false;
        Tell("预览模式"); return true;
    }

    private void RefreshState()
    {
        var state = engine.State;
        bool hasCode = !Bound && state.PairCode.Length > 0;
        string classroom = state.Snapshot?["classroom"]?["name"]?.GetValue<string>() ?? "已绑定班级";
        PairButton.Visibility = Bound ? Visibility.Collapsed : Visibility.Visible;
        PairButton.IsEnabled = !pairing; ServerInput.IsEnabled = !Bound && !pairing;
        CodeLabel.Text = Bound ? "已连接班级" : "一次性绑定码";
        CodeLabel.Visibility = Bound || hasCode ? Visibility.Visible : Visibility.Collapsed;
        PairCode.Text = Bound ? classroom : state.PairCode;
        PairCode.FontSize = hasCode ? 34 : 22;
        PairCode.Visibility = Bound || hasCode ? Visibility.Visible : Visibility.Collapsed;
        PairHint.Visibility = hasCode ? Visibility.Visible : Visibility.Collapsed;
        PairButton.Content = pairing ? "正在生成…" : hasCode ? "重新生成绑定码" : "生成绑定码";
        ConnectionStatus.Visibility = Bound ? Visibility.Collapsed : Visibility.Visible;
        if (hasCode && state.PairQr.Length > 0 && displayedQr != state.PairQr) {
            using var stream = new MemoryStream(Convert.FromBase64String(state.PairQr));
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); PairQr.Source = bitmap; displayedQr = state.PairQr;
        }
        PairQr.Visibility = hasCode && PairQr.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        PageArt.Visibility = page == "connect" && PairQr.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        NextButton.IsEnabled = !pairing && (page == "display" || Bound || preview || !setup);
        BackButton.IsEnabled = !pairing;
        StatusText.Text = Bound ? (state.Error.Length > 0 || state.EnableWallpaper ? engine.Status : "桌面展示已关闭")
            : hasCode ? "等待确认绑定" : "未绑定";
        TechnicalStatus.Text = $"ClassGlow {AppPaths.Version}\n已接收 v{state.CachedVersion} / 已展示 v{state.AppliedVersion}\n内容第 {state.Page} / {state.PageCount} 页\n{state.MaterialReason}";
        HomePrimary.Content = Bound ? "立即同步" : "继续绑定班级";
        PauseButton.Visibility = Bound ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.Content = state.LocalPaused ? "恢复展示" : "暂停展示";
        if (page == "home") PageTitle.Text = Bound ? classroom : "尚未连接班级";
    }

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewOnly()) return;
        pairing = true; FeedbackBox.Visibility = Visibility.Collapsed; RefreshState();
        try { await engine.Pair(ServerInput.Text); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException or DeviceAuthorizationException) { Tell("绑定未完成：" + error.Message); }
        finally { pairing = false; RefreshState(); }
    }
    private void Wallpaper_Click(object sender, RoutedEventArgs e) =>
        MonitorPanel.Visibility = WallpaperEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (page == "connect") { ShowPage(setup ? "display" : "home"); return; }
        if (preview) { setup = false; ShowPage("home"); return; }
        if (!SavePreferences(out string message)) return;
        setup = false; ShowPage("home"); Tell(message);
    }
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (setup && page == "display") { ShowPage("connect"); return; }
        setup = false; ShowPage("home");
    }

    private bool SavePreferences(out string message)
    {
        message = "已保存";
        bool enabled = WallpaperEnabled.IsChecked == true;
        if (enabled && MonitorChoice.SelectedItem is not MonitorOption) { Tell("请选择一块可用的显示器，再完成设置。"); return false; }
        var state = engine.State;
        int monitor = (MonitorChoice.SelectedItem as MonitorOption)?.Index ?? state.MonitorIndex;
        var previous = (state.EnableWallpaper, state.MonitorIndex, state.OriginalCaptured, state.OriginalWallpaper, state.OriginalMonitor, state.SetupCompleted);
        try {
            if (enabled) _ = new Wallpaper().Read(monitor);
            // 先恢复旧屏幕，再切换目标；返回上一步和取消都不会执行此处。
            if (state.EnableWallpaper && (!enabled || monitor != state.MonitorIndex)) engine.RestoreWallpaper();
            if (monitor != state.MonitorIndex) {
                state.OriginalCaptured = false; state.OriginalWallpaper = state.OriginalMonitor = "";
            }
            state.MonitorIndex = monitor; state.EnableWallpaper = enabled; state.SetupCompleted = true;
            engine.Save(); engine.RequestSync();
        } catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or InvalidOperationException) {
            (state.EnableWallpaper, state.MonitorIndex, state.OriginalCaptured, state.OriginalWallpaper, state.OriginalMonitor, state.SetupCompleted) = previous;
            Tell("展示设置未保存：" + error.Message); return false;
        }
        try {
            bool startup = StartupEnabled.IsChecked == true;
            using var read = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AppPaths.StartupKey);
            bool current = read?.GetValue(AppPaths.ProductId) is not null;
            // 默认关闭时不创建注册表项；仅在老师明确改变选项后写入当前用户设置。
            if (startup != current) {
                if (startup) {
                    using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AppPaths.StartupKey);
                    key.SetValue(AppPaths.ProductId, $"\"{Environment.ProcessPath}\" --startup");
                } else {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AppPaths.StartupKey, writable: true);
                    key?.DeleteValue(AppPaths.ProductId, false);
                }
            }
        } catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) {
            message = "展示设置已保存，但学校的系统策略阻止了自动启动设置。请联系电教管理员。";
        }
        return true;
    }
    private void HomePrimary_Click(object sender, RoutedEventArgs e)
    {
        if (!Bound) { setup = true; preferencesLoaded = false; ShowPage("connect"); return; }
        if (PreviewOnly()) return;
        engine.RequestSync(); Tell("已请求同步");
    }
    private void Settings_Click(object sender, RoutedEventArgs e) { setup = false; ShowPage("display"); }
    private void Binding_Click(object sender, RoutedEventArgs e) { setup = !Bound; ShowPage("connect"); }
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewOnly()) return;
        engine.LocalPause(!engine.State.LocalPaused); RefreshState();
    }
    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewOnly()) return;
        try { Tell(await engine.CheckVersion()); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { Tell("暂时无法检查更新，当前版本可继续使用。"); }
    }
    private void Privacy_Click(object sender, RoutedEventArgs e)
    {
        if (preview) PrivacyNotice.Create(requireConsent: false, preview: true).ShowDialog();
        else PrivacyNotice.Show(requireConsent: false);
    }
    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewOnly()) return;
        ClientLifecycle.StartUninstaller();
    }
}
