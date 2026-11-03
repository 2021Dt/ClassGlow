using System.IO;
using System.Runtime.InteropServices;

namespace ClassroomBoard;

[StructLayout(LayoutKind.Sequential)]
public struct NativeRect { public int Left, Top, Right, Bottom; }

// COM 方法顺序必须与 shobjidl_core.h 一致，仅声明本程序使用到的前五个槽位。
[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
    void GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitor);
    void GetMonitorDevicePathCount(out uint count);
    void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitor, out NativeRect rect);
}

public interface IWallpaper
{
    (string Id, NativeRect Rect, string Original) Read(int index);
    bool SafeToApply();
    void Set(string monitor, string path);
    string Current(string monitor);
}

public sealed class Wallpaper : IWallpaper
{
    private static readonly Guid ClassId = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int length);

    private static IDesktopWallpaper Open() => (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(ClassId, true)!)!;
    public (string Id, NativeRect Rect, string Original) Read(int index)
    {
        var api = Open();
        try {
            api.GetMonitorDevicePathCount(out uint count);
            if (index < 0 || index >= count) throw new InvalidOperationException("目标显示器不存在，请重新选择。");
            api.GetMonitorDevicePathAt((uint)index, out var id); api.GetMonitorRECT(id, out var rect); api.GetWallpaper(id, out var original);
            return (id, rect, original);
        } finally { Marshal.FinalReleaseComObject(api); }
    }
    public bool SafeToApply()
    {
        // 保守策略：只有前台本来就是桌面才应用。其他软件、全屏和输入场景全部延后。
        // 不激活窗口、不切桌面、不注入按键，不以后台更新为理由影响正在使用电脑的人。
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        var name = new System.Text.StringBuilder(256);
        if (GetClassName(hwnd, name, name.Capacity) == 0) return false;
        return name.ToString() is "Progman" or "WorkerW";
    }
    public void Set(string monitor, string path)
    {
        if (path.Length > 0 && !File.Exists(path)) throw new FileNotFoundException("待应用画面不存在。", path);
        var api = Open();
        try { api.SetWallpaper(monitor, path.Length == 0 ? "" : Path.GetFullPath(path)); }
        finally { Marshal.FinalReleaseComObject(api); }
    }
    public string Current(string monitor)
    {
        var api = Open();
        try { api.GetWallpaper(monitor, out string path); return path; }
        finally { Marshal.FinalReleaseComObject(api); }
    }
    public void ClearOwnedWallpapers(string directory)
    {
        var api = Open();
        try {
            api.GetMonitorDevicePathCount(out uint count);
            for (uint i = 0; i < count; i++) {
                api.GetMonitorDevicePathAt(i, out string id); api.GetWallpaper(id, out string path);
                if (ClientLifecycle.OwnedWallpaper(path, directory)) api.SetWallpaper(id, "");
            }
        } finally { Marshal.FinalReleaseComObject(api); }
    }
}
