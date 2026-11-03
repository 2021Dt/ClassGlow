using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Image = System.Windows.Controls.Image;

namespace ClassroomBoard;

/// <summary>隔离联调窗口：真实同步、命令执行和渲染，显示到窗口，不调用桌面壁纸 COM。</summary>
public static class ControlPreview
{
    private sealed class PreviewSurface(Image image) : IWallpaper
    {
        public bool Available { get; set; } = true;
        public string Path { get; private set; } = "";
        public (string Id, NativeRect Rect, string Original) Read(int index) => ("isolated-preview", new NativeRect { Right = 1920, Bottom = 1080 }, Path);
        public bool SafeToApply() => Available;
        public string Current(string monitor) => Path;
        public void Set(string monitor, string path)
        {
            BitmapImage? bitmap = null;
            if (path.Length > 0) {
                bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
            }
            image.Source = bitmap; Path = path;
        }
    }

    public static int Run(string[] args)
    {
        string? directory = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
        string? server = args.SkipWhile(a => a != "--server").Skip(1).FirstOrDefault();
        // 预览回执只用于本机测试服务，禁止连接正式服务并冒充真实大屏应用成功。
        if (directory is null || server is null || !Engine.ValidateServer(server).IsLoopback) return 2;
        Directory.CreateDirectory(directory);
        var app = new System.Windows.Application();
        var image = new Image { Stretch = Stretch.Uniform };
        var surface = new PreviewSurface(image);
        using var engine = new Engine(new StateStore(directory), desktop: surface);
        engine.State.EnableWallpaper = true;
        var panel = new DockPanel { Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 239, 237)) };
        var toolbar = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(12) };
        var localPause = new Button { Content = "现场暂停", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 12, 0) };
        localPause.Click += (_, _) => { engine.LocalPause(!engine.State.LocalPaused); localPause.Content = engine.State.LocalPaused ? "现场恢复" : "现场暂停"; };
        var available = new CheckBox { Content = "允许应用画面（模拟返回桌面）", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        available.Click += (_, _) => { surface.Available = available.IsChecked == true; engine.RequestSync(); };
        toolbar.Children.Add(localPause); toolbar.Children.Add(available);
        DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
        var status = new TextBlock { Margin = new Thickness(14, 8, 14, 12), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status); panel.Children.Add(image);
        var window = new Window { Title = "ClassGlow · Windows 联调预览（隔离）", Width = 1280, Height = 810, MinWidth = 800, MinHeight = 500, Content = panel };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        bool ticking = false, closing = false;
        timer.Tick += async (_, _) => {
            if (ticking || closing) return;
            ticking = true;
            try {
                if (File.Exists(System.IO.Path.Combine(directory, "stop"))) { window.Close(); return; }
                await engine.Tick();
                var state = engine.State;
                status.Text = $"隔离验收 · 系统壁纸未改变  |  {engine.Status}  |  缓存 v{state.CachedVersion} / 窗口应用 v{state.AppliedVersion}  |  第 {state.Page}/{state.PageCount} 页";
                var report = new JsonObject { ["deviceId"] = state.DeviceId, ["cached"] = state.CachedVersion, ["applied"] = state.AppliedVersion, ["state"] = state.ApplyState, ["page"] = state.Page, ["pages"] = state.PageCount, ["pageSetId"] = state.PageSetId, ["localPaused"] = state.LocalPaused, ["remotePaused"] = state.RemotePaused, ["frame"] = surface.Path, ["error"] = state.Error, ["wallpaperChanged"] = false };
                string path = System.IO.Path.Combine(directory, "status.json");
                File.WriteAllText(path + ".tmp", report.ToJsonString()); File.Move(path + ".tmp", path, true);
            } catch (Exception e) { File.WriteAllText(System.IO.Path.Combine(directory, "error.txt"), e.ToString()); status.Text = "联调失败，请检查隔离目录中的 error.txt。"; }
            finally { ticking = false; }
        };
        window.Loaded += async (_, _) => {
            try {
                await engine.Pair(server);
                File.WriteAllText(System.IO.Path.Combine(directory, "pairing.json"), new JsonObject { ["code"] = engine.State.PairCode }.ToJsonString());
                status.Text = $"隔离验收 · 本机测试服务绑定码：{engine.State.PairCode}。不会改变系统壁纸。";
                timer.Start();
            } catch (Exception e) { File.WriteAllText(System.IO.Path.Combine(directory, "error.txt"), e.ToString()); status.Text = "测试服务配对失败。"; }
        };
        window.Closing += async (_, e) => {
            if (closing) return;
            e.Cancel = true; closing = true; timer.Stop(); await engine.StopAsync(); window.Close();
        };
        app.Run(window);
        return File.Exists(System.IO.Path.Combine(directory, "error.txt")) ? 1 : 0;
    }
}
