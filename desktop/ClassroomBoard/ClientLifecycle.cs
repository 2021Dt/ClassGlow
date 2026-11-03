using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace ClassroomBoard;

public static partial class ClientLifecycle
{
    [GeneratedRegex(@"^board-[a-fA-F0-9]{12}-\d{12}-\d+-[a-fA-F0-9]{32}\.png(?:\.tmp)?$")]
    private static partial Regex FrameName();

    public static bool OwnedRaster(string file) => file == "neutral.png" || FrameName().IsMatch(file);
    public static bool OwnedWallpaper(string path, string directory) => path.Length > 0 &&
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase) && OwnedRaster(Path.GetFileName(path));

    public static bool StartUninstaller()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Uninstall.exe");
        if (!File.Exists(path)) {
            MessageBox.Show("当前是便携或开发运行目录。请先使用新版安装包安装，再从 Windows“已安装的应用”卸载；不会自动删除您自行放置的整个文件夹。", "班级看板", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return true;
    }

    public static void RemoveStartup()
    {
        // 仅删除本产品的一个值，不删除 Run 注册表键，也不操作其他软件的启动项。
        using var key = Registry.CurrentUser.OpenSubKey(AppPaths.StartupKey, true);
        key?.DeleteValue(AppPaths.ProductId, false);
    }

    public static bool CleanupData(string directory, bool legacyLogs)
    {
        string root = Path.GetFullPath(directory);
        if (Directory.Exists(root)) {
            // 不能穿过用户放入的目录联接；只清理本程序已知文件，不递归扫用户目录。
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("数据目录是目录联接，请先在系统中核对目标。");
            string[] known = ["state.bin", "state.bin.tmp", "privacy.json", "runtime.log", "runtime.log.old", "startup.log", "startup.log.old"];
            foreach (string path in Directory.EnumerateFiles(root)) {
                string name = Path.GetFileName(path);
                if (known.Contains(name) || OwnedRaster(name)) File.Delete(path);
            }
            if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
        if (legacyLogs) {
            foreach (string name in new[] { "ClassroomBoard-startup.log", "ClassroomBoard-runtime.log" }) File.Delete(Path.Combine(Path.GetTempPath(), name));
        }
        return Directory.Exists(root);
    }

    // NSIS 在用户确认卸载后调用。没有成功恢复壁纸/清理本机数据时，返回非零阻止删除程序。
    [STAThread]
    public static int PrepareUninstall(bool silent)
    {
        try {
            if (EventWaitHandle.TryOpenExisting(AppPaths.StopEventName, out var request)) { using (request) request.Set(); }
            using var singleton = new Mutex(false, AppPaths.MutexName);
            bool acquired;
            try { acquired = singleton.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("程序仍在运行。请先从托盘退出旧版本，再重试卸载。");
            try {
                bool remoteRemoved = true;
                var store = new StateStore();
                ClientState state;
                try { state = store.Load(); }
                catch (Exception e) when (e is IOException or CryptographicException or JsonException) {
                    if (silent || MessageBox.Show("本机状态无法读取，无法取得原壁纸记录或设备凭证。是否清理本机数据？仍使用本程序图片的显示器会恢复为系统纯色；服务器授权请由教师在网页撤销。", "卸载班级看板", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return 2;
                    new Wallpaper().ClearOwnedWallpapers(store.DirectoryPath);
                    state = new(); remoteRemoved = false;
                }
                using var engine = new Engine(store, state);
                engine.RestoreWallpaper();
                // 兼容旧版本遗留的本产品图片；检查所有显示器，仅清除仍引用本产品目录的画面。
                new Wallpaper().ClearOwnedWallpapers(store.DirectoryPath);
                if (state.Credential.Length > 0) {
                    try { engine.RevokeForUninstall().GetAwaiter().GetResult(); }
                    catch (DeviceAuthorizationException) { /* 凭证已经失效，同样不再拥有设备权限。 */ }
                    catch (Exception e) when (e is System.Net.Http.HttpRequestException or OperationCanceledException or InvalidDataException or JsonException or InvalidOperationException) {
                        remoteRemoved = false;
                        if (silent || MessageBox.Show("暂时无法联系服务器。是否继续卸载本机程序和数据？继续后，请由教师在网页“教室电脑”撤销原设备；服务器上的教学内容保留。", "离线卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return 3;
                    }
                }
                RemoveStartup();
                bool leftovers = CleanupData(store.DirectoryPath, AppPaths.ProductId == "ClassroomBoard");
                if (!silent && (leftovers || !remoteRemoved)) MessageBox.Show(
                    (leftovers ? "本程序数据已删除。目录中另外放置的文件已保留：\n" + store.DirectoryPath + "\n\n" : "") +
                    (!remoteRemoved ? "服务器设备授权尚未确认撤销，请由教师在网页“教室电脑”核对。" : ""), "卸载提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            } finally { singleton.ReleaseMutex(); }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or System.Security.SecurityException) {
            if (!silent) MessageBox.Show("卸载已暂停，程序和恢复记录仍保留。\n" + e.Message, "班级看板", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }
    }
}
