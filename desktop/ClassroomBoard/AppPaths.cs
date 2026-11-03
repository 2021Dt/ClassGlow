using System.IO;

namespace ClassroomBoard;

public static class AppPaths
{
    public const string Version = "0.4.3";
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
    public const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void QuietLog(string name, string exceptionType)
    {
        try {
            Directory.CreateDirectory(DataDirectory);
            string file = Path.Combine(DataDirectory, name);
            // 只保留异常类型与时间；两份日志各自不超过约 128 KiB，不记录正文、网址或凭证。
            if (File.Exists(file) && new FileInfo(file).Length >= 128 * 1024) File.Move(file, file + ".old", true);
            File.AppendAllText(file, $"{DateTimeOffset.Now:O} {exceptionType}\n");
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
