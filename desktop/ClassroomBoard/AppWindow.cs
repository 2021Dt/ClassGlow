using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using SystemColors = System.Windows.SystemColors;
using TextBlock = System.Windows.Controls.TextBlock;

namespace ClassroomBoard;

/// <summary>所有客户端窗口共用的 Fluent 外壳，标题栏与内容连成一体。</summary>
public class AppWindow : FluentWindow
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref uint value, int size);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    private readonly DockPanel root = new();

    public static void InitializeTheme()
    {
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new Uri("/ClassroomBoard;component/ClientTheme.xaml", UriKind.Relative)
        });
        ApplicationAccentColorManager.Apply(Color.FromRgb(44, 103, 86), ApplicationTheme.Light, false, false);
        // 高对比模式优先可读性；不强行保留半透明材质。
        if (SystemParameters.HighContrast) {
            ApplicationThemeManager.Apply(ApplicationTheme.HighContrast, WindowBackdropType.None);
            var resources = System.Windows.Application.Current.Resources;
            resources["GlowText"] = SystemColors.WindowTextBrush;
            resources["GlowMuted"] = SystemColors.WindowTextBrush;
            resources["GlowSurface"] = SystemColors.WindowBrush;
            resources["GlowInset"] = SystemColors.WindowBrush;
            resources["GlowLine"] = SystemColors.WindowTextBrush;
            resources["GlowAccent"] = SystemColors.HighlightBrush;
            foreach (string key in new[] { "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBorderBrush" }) resources[key] = SystemColors.HighlightBrush;
            resources["CheckBoxCheckGlyphForeground"] = SystemColors.HighlightTextBrush;
        }
    }

    public AppWindow(string title, double width = 820, double height = 800, bool resizable = true)
    {
        Title = title; Width = Math.Min(width, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(height, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(660, Width); MinHeight = Math.Min(480, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.CanMinimize;
        ExtendsContentIntoTitleBar = true;
        WindowCornerPreference = WindowCornerPreference.Round;
        WindowBackdropType = SystemParameters.HighContrast ? WindowBackdropType.None : WindowBackdropType.Mica;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        SetResourceReference(ForegroundProperty, "GlowText");
        // WPF UI 在不支持云母的系统上保留底色，不使用易出现黑底的透明分层窗口。
        Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(242, 245, 243));
        Icon = BrandIcon();
        var titleBar = new TitleBar { Title = "ClassGlow  ·  " + title, Height = 48, ShowMaximize = resizable, ShowMinimize = true,
            Icon = new ImageIcon { Source = Icon } };
        void CaptionNames()
        {
            foreach (var (name, label) in new[] { ("PART_MinimizeButton", "最小化"), ("PART_MaximizeButton", WindowState == WindowState.Maximized ? "还原窗口" : "最大化"), ("PART_CloseButton", "关闭") })
                if (titleBar.Template.FindName(name, titleBar) is FrameworkElement element) { AutomationProperties.SetName(element, label); element.ToolTip = label; }
        }
        titleBar.Loaded += (_, _) => CaptionNames(); StateChanged += (_, _) => CaptionNames();
        DockPanel.SetDock(titleBar, Dock.Top); root.Children.Add(titleBar);
        Content = root;
    }

    public void SetBody(UIElement body) => root.Children.Add(body);

    private void RemoveNativeBorder()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || SystemParameters.HighContrast) return;
        // DWMWA_BORDER_COLOR / DWMWA_COLOR_NONE：取消系统深色描边，保留系统圆角和阴影。
        uint none = 0xFFFFFFFE;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 34, ref none, sizeof(uint));
    }
    protected override void OnSourceInitialized(EventArgs e) { base.OnSourceInitialized(e); RemoveNativeBorder(); }
    protected override void OnActivated(EventArgs e) { base.OnActivated(e); RemoveNativeBorder(); }
    protected override void OnDeactivated(EventArgs e) { base.OnDeactivated(e); RemoveNativeBorder(); }

    public static DrawingImage BrandIcon()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(44, 103, 86)), null,
            new RectangleGeometry(new Rect(0, 0, 32, 32), 8, 8)));
        var pen = new System.Windows.Media.Pen(Brushes.White, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawing.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M7,13 L16,7 L25,13 M9,12 L9,25 L23,25 L23,12 M13,25 L13,19 L19,19 L19,25 M14,12 L18,12 L18,16 L14,16 Z")));
        var icon = new DrawingImage(drawing); icon.Freeze(); return icon;
    }

    public static System.Drawing.Icon TrayIcon()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawImage(BrandIcon(), new Rect(0, 0, 32, 32));
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new System.IO.MemoryStream(); encoder.Save(stream); stream.Position = 0;
        using var image = new System.Drawing.Bitmap(stream);
        IntPtr handle = image.GetHicon();
        // 托盘持有独立副本，及时释放临时 HICON，避免反复打开产生句柄泄漏。
        try { using var icon = System.Drawing.Icon.FromHandle(handle); return (System.Drawing.Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }

    public static TextBlock Hint(string text) => new() { Text = text, Style = (Style)System.Windows.Application.Current.FindResource("GlowHint") };
}
