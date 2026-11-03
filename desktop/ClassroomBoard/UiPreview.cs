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
        app.Run(window); return 0;
    }
}
