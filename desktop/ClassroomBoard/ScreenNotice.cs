using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Size = System.Windows.Size;
using FontFamily = System.Windows.Media.FontFamily;

namespace ClassroomBoard;

public interface IScreenNotice : IDisposable
{
    string ActiveId { get; }
    double EndsAt { get; }
    void Show(JsonObject command, NativeRect monitor, DateTimeOffset clock);
    void Dismiss();
}

/// <summary>临时提醒独立于静态壁纸，只在播放期间创建窗口和计时器，不抢焦点、不保存额外文件。</summary>
public sealed class ScreenNotice : IScreenNotice
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    private Window? window;
    private DispatcherTimer? timer;
    public string ActiveId { get; private set; } = "";
    public double EndsAt { get; private set; }

    private static FrameworkElement DesignedNotice(JsonObject notice, Grid root, out TextBlock? counter)
    {
        var style = notice["style"]!.AsObject();
        string mode = notice.Str("mode"), message = notice.Str("message");
        double size = Math.Clamp(style.Int("fontSize", 56), 24, 120);
        var color = (Color)ColorConverter.ConvertFromString(style.Str("backgroundColor", "#17458c"));
        var fill = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(style.Int("opacity", 95), 20, 100) * 2.55), color.R, color.G, color.B));
        var foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Str("textColor", "#ffffff")));
        var family = new FontFamily(style.Str("fontFamily") switch { "serif" => "SimSun, Microsoft YaHei", "kai" => "KaiTi, Microsoft YaHei", _ => "Microsoft YaHei UI, Microsoft YaHei" });
        var weight = style["bold"]?.GetValue<bool>() == false ? FontWeights.Normal : FontWeights.Bold;
        double radius = Math.Clamp(style.Int("radius", 32), 0, 64);
        var stage = new Grid { Width = 1920, Height = 1080 };
        counter = null;
        if (mode is "popup" or "countdown") {
            bool card = style.Str("surface") == "card";
            if (!card) root.Background = fill;
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = mode == "countdown" ? "课堂倒计时" : "课堂提醒", FontFamily = family, FontSize = 26, Opacity = .75, Foreground = foreground, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 28) });
            content.Children.Add(new TextBlock { Text = message, FontFamily = family, FontSize = size, FontWeight = weight, Foreground = foreground, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, LineHeight = size * 1.4, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
            if (mode == "countdown") {
                counter = new TextBlock { Text = "00:00", FontFamily = family, FontSize = 144, FontWeight = FontWeights.Bold, Foreground = foreground, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 28, 0, 0) };
                content.Children.Add(counter);
            }
            var panel = new Border { Width = 1920 * Math.Clamp(style.Int("width", 72), 40, 90) / 100d, Padding = new Thickness(56, 48, 56, 48), Background = card ? fill : Brushes.Transparent, CornerRadius = new CornerRadius(radius), Child = content };
            stage.Children.Add(new Viewbox { Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(0, 110, 0, 110), MaxWidth = 1920, MaxHeight = 860, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = !card ? VerticalAlignment.Center : style.Str("position") switch { "bottom" => VerticalAlignment.Bottom, "center" => VerticalAlignment.Center, _ => VerticalAlignment.Top } });
        } else {
            string[] lines = mode == "barrage" ? message.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3).ToArray() : [message.Replace('\n', ' ')];
            double lineHeight = size * 1.4 + 48, height = lineHeight * lines.Length;
            var lane = new Canvas { Width = 1920, Height = height, ClipToBounds = true, IsHitTestVisible = false, Background = mode == "marquee" ? fill : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            double top = style.Str("position") switch { "bottom" => 1080 - height - 60, "center" => (1080 - height) / 2, _ => 60 };
            lane.Margin = new Thickness(0, top, 0, 0); stage.Children.Add(lane);
            lane.Loaded += (_, _) => {
                if (lane.Children.Count != 0) return;
                for (int i = 0; i < lines.Length; i++) {
                    var text = new TextBlock { Text = lines[i], FontFamily = family, FontSize = size, FontWeight = weight, Foreground = foreground, Padding = new Thickness(28, 18, 28, 18), LineHeight = size * 1.4, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
                    var bubble = new Border { Background = mode == "barrage" ? fill : Brushes.Transparent, CornerRadius = new CornerRadius(mode == "barrage" ? radius : 0), Child = text };
                    bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetTop(bubble, i * lineHeight + 6); lane.Children.Add(bubble);
                    bool right = style.Str("direction") == "right";
                    double start = right ? -bubble.DesiredSize.Width : 1920, end = right ? 1920 : -bubble.DesiredSize.Width;
                    var movement = new TranslateTransform(start, 0); bubble.RenderTransform = movement;
                    movement.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(start, end, TimeSpan.FromSeconds(Math.Clamp(style.Int("flowSeconds", 14), 6, 40))) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(i * 1.6) });
                }
            };
        }
        return new Viewbox { Child = stage, Stretch = Stretch.Uniform };
    }

    public void Show(JsonObject command, NativeRect monitor, DateTimeOffset clock)
    {
        var notice = command["notice"]!.AsObject();
        string mode = notice.Str("mode"), message = notice.Str("message");
        double remaining = notice.Num("endsAt") - clock.ToUnixTimeMilliseconds() / 1000d;
        if (remaining <= 0) throw new InvalidOperationException("提醒已到期");
        Dismiss();
        var root = new Grid();
        var active = new Window {
            Title = "ClassGlow · 醒目提醒", Content = root, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            Left = monitor.Left, Top = monitor.Top, Width = 600, Height = 400,
        };
        // 窗口先落到目标屏幕，再按该屏 DPI 将物理像素换成 WPF 逻辑像素。
        active.SourceInitialized += (_, _) => {
            var dpi = VisualTreeHelper.GetDpi(active);
            active.Left = monitor.Left / dpi.DpiScaleX; active.Top = monitor.Top / dpi.DpiScaleY;
            active.Width = (monitor.Right - monitor.Left) / dpi.DpiScaleX;
            active.Height = (monitor.Bottom - monitor.Top) / dpi.DpiScaleY;
        };
        var color = (Color)ColorConverter.ConvertFromString(notice.Str("tone") switch { "red" => "#B72C43", "amber" => "#916013", _ => "#17458C" });
        var fill = new SolidColorBrush(Color.FromArgb(242, color.R, color.G, color.B));
        double width = Math.Max(320, monitor.Right - monitor.Left);
        var close = new Button { Content = "关闭提醒 ×", FontSize = 16, Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.White, Foreground = new SolidColorBrush(color), BorderThickness = new Thickness(0) };
        close.Click += (_, _) => Dismiss();
        TextBlock? counter = null;
        if (notice["style"] is JsonObject) {
            root.Children.Add(DesignedNotice(notice, root, out counter));
        } else if (mode is "popup" or "countdown") {
            root.Background = fill;
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(64) };
            content.Children.Add(new TextBlock { Text = mode == "countdown" ? "课堂倒计时" : "课堂提醒", FontSize = 24, Opacity = .75, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 28) });
            var text = new TextBlock { Text = message, FontSize = 48, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Brushes.White };
            content.Children.Add(text);
            if (mode == "countdown") { counter = new TextBlock { FontSize = 132, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 28, 0, 0) }; content.Children.Add(counter); }
            var fit = new Viewbox { StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(20, 80, 20, 35), Child = content };
            content.MaxWidth = Math.Max(600, width * .72); root.Children.Add(fit);
        } else {
            var lane = new Canvas { ClipToBounds = true, Height = mode == "barrage" ? 270 : 110, Margin = new Thickness(0, 36, 0, 0), VerticalAlignment = VerticalAlignment.Top, Background = mode == "marquee" ? fill : Brushes.Transparent, IsHitTestVisible = false };
            root.Children.Add(lane);
            active.Loaded += (_, _) => {
                var lines = mode == "barrage" ? message.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3).ToArray() : new[] { message.Replace('\n', ' ') };
                for (int i = 0; i < lines.Length; i++) {
                    var text = new TextBlock { Text = lines[i], FontSize = 42, Foreground = Brushes.White, Padding = new Thickness(28, 10, 28, 10) };
                    var bubble = new Border { Background = fill, CornerRadius = new CornerRadius(mode == "barrage" ? 28 : 0), Child = text };
                    bubble.Measure(new Size(double.PositiveInfinity, 100));
                    Canvas.SetTop(bubble, i * 82 + 14); lane.Children.Add(bubble);
                    var movement = new TranslateTransform(); bubble.RenderTransform = movement;
                    double distance = lane.ActualWidth + bubble.DesiredSize.Width;
                    movement.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(lane.ActualWidth, -bubble.DesiredSize.Width, TimeSpan.FromSeconds(Math.Max(6, distance / 130))) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(i * 1.6) });
                }
            };
        }
        root.Children.Add(close);
        active.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Dismiss(); };
        active.Closed += (_, _) => { if (window == active) { timer?.Stop(); timer = null; window = null; ActiveId = ""; EndsAt = 0; } };
        window = active; ActiveId = command.Str("id"); EndsAt = notice.Num("endsAt");
        // 用单调时钟累计本次播放，避免现场手动改系统时间造成无限播放。
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        void Tick() {
            double seconds = remaining - elapsed.Elapsed.TotalSeconds;
            if (counter is not null) { int whole = (int)Math.Max(0, Math.Ceiling(seconds)); counter.Text = whole > 0 ? $"{whole / 60:00}:{whole % 60:00}" : "时间到"; }
            if (seconds <= (mode == "countdown" ? -5 : 0)) Dismiss();
        }
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; timer.Tick += (_, _) => Tick();
        active.Show();
        // 使用物理坐标最终定位，兼容扩展屏不同 DPI 与负坐标，不激活抢焦点。
        if (!SetWindowPos(new WindowInteropHelper(active).Handle, new IntPtr(-1), monitor.Left, monitor.Top, monitor.Right - monitor.Left, monitor.Bottom - monitor.Top, 0x0010)) {
            Dismiss(); throw new InvalidOperationException("提醒窗口无法定位到目标显示器");
        }
        Tick(); timer?.Start();
    }
    public void Dismiss() { timer?.Stop(); timer = null; var closing = window; window = null; ActiveId = ""; EndsAt = 0; closing?.Close(); }
    public void Dispose() => Dismiss();
}
