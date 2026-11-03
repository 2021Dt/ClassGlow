using System.IO;
using System.Text.Json.Nodes;

namespace ClassroomBoard;

/// <summary>只验证窗口外观；不启动同步计时器，不写入真实用户数据或注册表。</summary>
public static class UiPreview
{
    public static int Run(string[] args)
    {
        var app = new System.Windows.Application(); AppWindow.InitializeTheme();
        if (args.Contains("--privacy")) {
            app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            var privacy = PrivacyNotice.Create(true, preview: true); privacy.ShowDialog();
            if (privacy.Tag is not true) return 0;
            app.ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;
        }
        var state = new ClientState();
        if (args.Contains("--bound")) {
            state.BindingId = "appearance-preview"; state.Snapshot = new JsonObject { ["classroom"] = new JsonObject { ["name"] = "六年级（1）班 · 示例" } };
            state.CachedVersion = state.AppliedVersion = 12; state.ApplyState = "applied"; state.PageCount = 3;
            state.EnableWallpaper = true;
            if (args.Contains("--unfinished")) state.SetupCompleted = false;
        }
        using var engine = new Engine(new StateStore(Path.Combine(AppContext.BaseDirectory, "ui-preview-unused")), initial: state);
        var window = new AppWindow("教室电脑 · 外观预览"); window.SetBody(new ClientPanel(engine, preview: true));
        if (args.Contains("--tray")) {
            // 使用正式窗口收起逻辑验证托盘，不连接服务器、不改变壁纸或启动项。
            app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            using var host = new TrayWindowHost(() => window);
            using var icon = AppWindow.TrayIcon();
            using var menu = new System.Windows.Forms.ContextMenuStrip();
            using var tray = new System.Windows.Forms.NotifyIcon { Icon = icon, Text = "班级看板 · 托盘预览", Visible = true, ContextMenuStrip = menu };
            menu.Items.Add("打开班级看板", null, (_, _) => host.Open());
            menu.Items.Add("退出预览", null, (_, _) => { tray.Visible = false; host.Dispose(); app.Shutdown(); });
            tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) host.Open(); };
            host.Open();
            if (args.Contains("--check-tray")) {
                // 小范围验证真实 WPF 事件，覆盖收起、唤回和退出，不调用桌面自动化 API。
                app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => {
                    string output = args[Array.IndexOf(args, "--output") + 1];
                    var checks = new JsonObject();
                    try {
                        window.Close();
                        checks["closeHidesWindow"] = !window.IsVisible && !window.ShowInTaskbar;
                        host.Open();
                        checks["reopenRestoresWindow"] = window.IsVisible && window.ShowInTaskbar;
                        window.WindowState = System.Windows.WindowState.Minimized;
                        checks["minimizeHidesWindow"] = !window.IsVisible && !window.ShowInTaskbar;
                        host.Open();
                        checks["reopenRestoresNormalState"] = window.IsVisible && window.WindowState == System.Windows.WindowState.Normal;
                        bool closed = false;
                        window.Closed += (_, _) => closed = true;
                        host.Dispose();
                        tray.Visible = false;
                        checks["exitClosesWindowAndTray"] = closed && !tray.Visible;
                        bool passed = checks.All(item => item.Value!.GetValue<bool>());
                        checks["passed"] = passed;
                        File.WriteAllText(output, checks.ToJsonString());
                        app.Shutdown(passed ? 0 : 1);
                    } catch (Exception e) {
                        File.WriteAllText(output, new JsonObject { ["passed"] = false, ["error"] = e.ToString() }.ToJsonString());
                        app.Shutdown(1);
                    }
                }));
            }
            return app.Run();
        }
        app.Run(window); return 0;
    }
}
