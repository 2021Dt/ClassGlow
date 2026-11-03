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
    private readonly DockPanel root = new();

    public static void InitializeTheme()
    {
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new Uri("/ClassroomBoard;component/ClientTheme.xaml", UriKind.Relative)
        });
        ApplicationAccentColorManager.Apply(Color.FromRgb(36, 120, 237), ApplicationTheme.Light, false, false);
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
            resources["GlowCardEdge"] = SystemColors.WindowTextBrush;
            resources["GlowOnAccent"] = SystemColors.HighlightTextBrush;
            resources["GlowPrimaryFill"] = resources["GlowPrimaryHover"] = resources["GlowPrimaryPressed"] = SystemColors.HighlightBrush;
            resources["GlowSwitchOff"] = SystemColors.GrayTextBrush;
            resources["GlowKnob"] = SystemColors.HighlightTextBrush;
            resources["GlowProgressIdle"] = SystemColors.GrayTextBrush;
            foreach (string key in new[] { "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBorderBrush" }) resources[key] = SystemColors.HighlightBrush;
            resources["CheckBoxCheckGlyphForeground"] = SystemColors.HighlightTextBrush;
        }
    }

    public AppWindow(string title, double width = 720, double height = 640, bool resizable = true)
    {
        Title = title; Width = Math.Min(width, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(height, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(600, Width); MinHeight = Math.Min(500, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.CanMinimize;
        ExtendsContentIntoTitleBar = true;
        WindowCornerPreference = WindowCornerPreference.Round;
        WindowBackdropType = SystemParameters.HighContrast ? WindowBackdropType.None : WindowBackdropType.Mica;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        SetResourceReference(ForegroundProperty, "GlowText");
        // WPF UI 在不支持云母的系统上保留底色，不使用易出现黑底的透明分层窗口。
        Background = SystemParameters.HighContrast ? SystemColors.WindowBrush
            : new SolidColorBrush(Color.FromArgb(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? (byte)208 : (byte)255, 247, 248, 251));
        Icon = BrandIcon();
        var titleBar = new TitleBar { Title = title.Contains("外观预览") ? "ClassGlow · 外观预览" : "ClassGlow", Height = 40, FontFamily = FontFamily, FontSize = 12, ShowMaximize = resizable, ShowMinimize = true,
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

    public static System.Windows.Controls.Image Artwork(string key, double height = 104) => new() {
        Source = (ImageSource)System.Windows.Application.Current.FindResource(key),
        Width = height * 128 / 112, Height = height, HorizontalAlignment = System.Windows.HorizontalAlignment.Center
    };

    public static StackPanel Progress(int step)
    {
        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        SetProgress(panel, step); return panel;
    }

    public static void SetProgress(StackPanel panel, int step)
    {
        // 进度点只是位置提示；下一步由页面按钮控制，不能跳过同意或绑定。
        panel.Children.Clear();
        AutomationProperties.SetName(panel, $"第 {step} 步，共 3 步");
        for (int index = 1; index <= 3; index++) {
            var dot = new Border { Width = index == step ? 20 : 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(4, 0, 4, 0) };
            dot.SetResourceReference(Border.BackgroundProperty, index == step ? "GlowAccent" : "GlowProgressIdle");
            panel.Children.Add(dot);
        }
    }

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

    public static ImageSource BrandIcon()
    {
        var icon = new BitmapImage(new Uri("pack://application:,,,/ClassroomBoard;component/Assets/AppIcon.png"));
        icon.Freeze(); return icon;
    }

    public static System.Drawing.Icon TrayIcon()
    {
        // 托盘与 EXE 使用同一份多尺寸图标，交由 Windows 选择适合当前缩放的尺寸。
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/ClassroomBoard;component/Assets/AppIcon.ico")).Stream;
        return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    public static TextBlock Hint(string text) => new() { Text = text, Style = (Style)System.Windows.Application.Current.FindResource("GlowHint") };
}
