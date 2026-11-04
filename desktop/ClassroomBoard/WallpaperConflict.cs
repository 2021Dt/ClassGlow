using System.ComponentModel;
using System.Diagnostics;

namespace ClassroomBoard;

/// <summary>只在本机检查已知壁纸进程是否存在，不读取路径、命令行或上传进程信息。</summary>
public static class WallpaperConflict
{
    private static long nextCheck;
    private static bool running;

    public static bool IsRunning(bool refresh = false)
    {
        if (!refresh && Environment.TickCount64 < nextCheck) return running;
        using var current = Process.GetCurrentProcess();
        running = false;
        // 官方文档列出的主进程：https://help.wallpaperengine.io/en/functionality/cli.html
        foreach (string name in new[] { "wallpaper32", "wallpaper64" }) {
            var processes = Process.GetProcessesByName(name);
            try {
                foreach (var process in processes) {
                    try { if (process.SessionId == current.SessionId && !process.HasExited) running = true; }
                    catch (Exception error) when (error is InvalidOperationException or Win32Exception) { /* 检查期间进程可能已退出。 */ }
                }
            } finally { foreach (var process in processes) process.Dispose(); }
        }
        nextCheck = Environment.TickCount64 + 5000;
        return running;
    }
}
