using System.IO;

namespace ClassroomBoard;

public static class AppPaths
{
    // 对外上报与安装包共用项目版本，避免升级后仍被服务器识别成旧客户端。
    public static string Version => typeof(AppPaths).Assembly.GetName().Version!.ToString(3);
#if LIFECYCLE_QA
    // 验证包使用独立目录、启动项和进程锁，绝不触碰正在使用的正式客户端。
    public const string ProductId = "ClassroomBoard.QA";
    public const string InstallFolder = "ClassGlow.QA";
    public const string DisplayName = "班级看板 · 卸载验证";
#else
    public const string ProductId = "ClassroomBoard";
    public const string InstallFolder = "ClassGlow";
    public const string DisplayName = "班级看板";
#endif
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductId);
    public static string MutexName => "Local\\" + ProductId + ".CurrentUser";
    public static string StopEventName => "Local\\" + ProductId + ".StopRequested";
    public static string ShowEventName => "Local\\" + ProductId + ".ShowRequested";
    public const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void QuietLog(string name, string diagnostic, string? directory = null)
    {
        try {
            directory ??= DataDirectory;
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, name);
            // 调用方只传入不含用户数据的诊断字段；日志轮转，不记录正文、网址或凭证。
            if (File.Exists(file) && new FileInfo(file).Length >= 128 * 1024) File.Move(file, file + ".old", true);
            File.AppendAllText(file, $"{DateTimeOffset.Now:O} {diagnostic}\n");
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
