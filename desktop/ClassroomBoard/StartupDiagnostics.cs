using System.Diagnostics;
using System.Windows.Forms;

namespace ClassroomBoard;

/// <summary>启动失败时提供可读提示；诊断只留在本产品目录，不自动上报。</summary>
public static class StartupDiagnostics
{
    public static void Report(Exception error, string phase, bool silent)
    {
        // 不记录异常 Message/ToString，它们可能包含服务器地址、文件路径或教学正文。
        var details = new List<string> {
            $"phase={phase}", $"client={AppPaths.Version}",
            $"windows={Environment.OSVersion.Version}", $"runtime={Environment.Version}",
            "render=software;backdrop=none"
        };
        for (Exception? current = error; current is not null; current = current.InnerException) {
            details.Add($"{current.GetType().FullName}(0x{current.HResult:X8})");
            // 仅记录应用内部的方法名，不带源码路径、参数或局部变量。
            details.AddRange(new StackTrace(current, false).GetFrames()
                .Select(frame => frame.GetMethod())
                .Where(method => method?.DeclaringType?.Namespace == "ClassroomBoard")
                .Take(6)
                .Select(method => $"{method!.DeclaringType!.Name}.{method.Name}"));
        }
        AppPaths.QuietLog("startup.log", string.Join(" | ", details));
        if (silent) return;
        // 使用系统对话框显示故障，避免继续依赖可能已经失效的 WPF 界面。
        MessageBox.Show(
            $"班级看板未能正常打开，请退出后重新运行最新版。\n\n" +
            $"诊断记录：{AppPaths.DataDirectory}\\startup.log\n" +
            $"错误类型：{error.GetType().Name}（0x{error.HResult:X8}）\n\n" +
            "如果仍无法打开，请把这份日志交给管理员排查。日志不会自动上传。",
            "班级看板 · 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
