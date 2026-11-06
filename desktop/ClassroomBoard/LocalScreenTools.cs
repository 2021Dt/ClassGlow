using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ClassroomBoard;

/// <summary>只由现场点击打开的浮层。屏幕截图仅在用户框选后读取一次，不回传、不自动落盘。</summary>
public sealed class LocalScreenTools : IDisposable
{
    private readonly List<Window> windows = [];
    private Window? overlay;
    private bool closing;
    internal Func<NativeRect, BitmapSource> Capture { get; set; } = CaptureRegion;
    public int WindowCount => windows.Count;
    public void Open(string mode, int monitorIndex, Window? launcher)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var rect = screens[Math.Clamp(monitorIndex, 0, screens.Length - 1)].Bounds;
        OpenAt(mode, new() { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom }, launcher);
    }
    internal void OpenAt(string mode, NativeRect bounds, Window? launcher = null)
    {
        if (overlay is not null) { closing = true; overlay.Close(); closing = false; }
        var next = new TeachingOverlay(mode, bounds);
        next.CaptureRequested += async region => {
            next.Hide();
            try {
                await next.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(120);
                if (!windows.Contains(next)) return;
                Pin(Capture(region));
            } catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or ExternalException) {
                if (windows.Contains(next)) System.Windows.MessageBox.Show("无法读取选定画面，请退出受保护视频或远程桌面限制后重试。", "截图钉图");
            } finally { if (windows.Contains(next)) next.Close(); }
        };
        overlay = next; Track(next); launcher?.Hide();
        next.Closed += (_, _) => { if (overlay == next) overlay = null; if (!closing && launcher is not null) { launcher.Show(); launcher.Activate(); } };
        next.Show(); next.Place(bounds);
    }
    public void Track(Window window)
    {
        windows.Add(window); window.Closed += (_, _) => windows.Remove(window);
    }
    private void Pin(BitmapSource bitmap)
    {
        if (windows.Count(w => w is AppWindow) >= 8) throw new InvalidOperationException("最多同时保留 8 张钉图。");
        var window = new AppWindow("截图钉图 · 仅本机", 740, 580) { Topmost = true, ReduceMaterial = true };
        var root = new DockPanel { Margin = new Thickness(16) }; var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var image = new System.Windows.Controls.Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Stretch = Stretch.Fill };
        double zoom = Math.Min(1, 650d / bitmap.PixelWidth);
        void Size() { image.Width = bitmap.PixelWidth * zoom; image.Height = bitmap.PixelHeight * zoom; }
        Size();
        bar.Children.Add(ToolUi.Button("缩小", () => { zoom = Math.Max(.1, zoom / 1.2); Size(); }));
        bar.Children.Add(ToolUi.Button("放大", () => { zoom = Math.Min(4, zoom * 1.2); Size(); }));
        bar.Children.Add(ToolUi.Button("保存 PNG…", () => ToolUi.SavePng(bitmap, window, "课堂钉图.png")));
        bar.Children.Add(ToolUi.Button("关闭", window.Close));
        root.Children.Add(new ScrollViewer { Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.SetBody(root); window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); }; Track(window); window.Show();
    }
    private static BitmapSource CaptureRegion(NativeRect region)
    {
        int width = region.Right - region.Left, height = region.Bottom - region.Top;
        if (width < 4 || height < 4 || (long)width * height > 40_000_000) throw new ArgumentException("截图区域太小或过大。");
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(region.Left, region.Top, 0, 0, new System.Drawing.Size(width, height));
        IntPtr handle = bitmap.GetHbitmap();
        try { var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze(); return source; }
        finally { DeleteObject(handle); }
    }
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    public void CloseAll() { closing = true; try { foreach (var window in windows.ToArray()) window.Close(); overlay = null; } finally { closing = false; } }
    public void Dispose() => CloseAll();
}

internal sealed class SpotlightSurface : FrameworkElement
{
    public Point Center { get; set; } = new(500, 350);
    public double Radius { get; set; } = 150;
    protected override void OnRender(DrawingContext dc)
    {
        var cutout = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(RenderSize)), new EllipseGeometry(Center, Radius, Radius));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(205, 0, 0, 0)), null, cutout);
    }
}

internal sealed class TeachingOverlay : Window
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    internal InkCanvas Ink { get; } = new() { Background = Brushes.Transparent };
    private readonly Stack<StrokeCollection> history = new();
    private StrokeCollection previous = new();
    private bool restoring;
    private string penMode = "pen";
    private Point? arrowStart;
    private readonly Canvas surface = new() { Background = Brushes.Transparent };
    private readonly Grid root = new();
    private readonly NativeRect bounds;
    public event Action<NativeRect>? CaptureRequested;
    public TeachingOverlay(string mode, NativeRect rect)
    {
        if (mode is not ("ink" or "whiteboard" or "spotlight" or "mask" or "capture")) throw new ArgumentException("未知屏幕工具。");
        bounds = rect; Title = "ClassGlow · " + (mode switch { "ink" => "屏幕批注", "whiteboard" => "小白板", "spotlight" => "聚光灯", "mask" => "答案遮挡", _ => "截图框选" }); WindowStyle = WindowStyle.None; AllowsTransparency = true;
        // 分层窗口 alpha=0 的像素会被 Windows 鼠标命中测试跳过。极低的不透明底色保证框选/批注不会点到后面的课件。
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true; Topmost = true; Width = Math.Max(100, rect.Right - rect.Left); Height = Math.Max(100, rect.Bottom - rect.Top);
        SourceInitialized += (_, _) => { if (PresentationSource.FromVisual(this) is HwndSource source) source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly; };
        Content = root; var bar = new WrapPanel { Background = new SolidColorBrush(Color.FromRgb(242, 245, 251)), Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        if (mode is "ink" or "whiteboard") {
            Ink.Background = mode == "whiteboard" ? Brushes.White : Brushes.Transparent; root.Children.Add(Ink);
            Ink.DefaultDrawingAttributes = new DrawingAttributes { Color = Colors.Red, Width = 4, Height = 4, FitToCurve = true };
            Ink.Strokes.StrokesChanged += (_, _) => {
                if (!restoring) { history.Push(previous.Clone()); if (history.Count > 30) { var retained = history.Take(30).Reverse().ToArray(); history.Clear(); foreach (var item in retained) history.Push(item); } }
                previous = Ink.Strokes.Clone();
            };
            foreach (var (name, color) in new[] { ("红笔", Colors.Red), ("蓝笔", Colors.RoyalBlue), ("黑笔", Colors.Black) }) bar.Children.Add(ToolUi.Button(name, () => { penMode = "pen"; Ink.EditingMode = InkCanvasEditingMode.Ink; Ink.DefaultDrawingAttributes.Color = color; Ink.DefaultDrawingAttributes.IsHighlighter = false; Ink.DefaultDrawingAttributes.Width = Ink.DefaultDrawingAttributes.Height = 4; }));
            bar.Children.Add(ToolUi.Button("荧光笔", () => { penMode = "pen"; Ink.EditingMode = InkCanvasEditingMode.Ink; Ink.DefaultDrawingAttributes.Color = Colors.Gold; Ink.DefaultDrawingAttributes.IsHighlighter = true; Ink.DefaultDrawingAttributes.Width = Ink.DefaultDrawingAttributes.Height = 20; }));
            bar.Children.Add(ToolUi.Button("箭头", () => { penMode = "arrow"; Ink.EditingMode = InkCanvasEditingMode.None; }));
            bar.Children.Add(ToolUi.Button("橡皮", () => { penMode = "eraser"; Ink.EditingMode = InkCanvasEditingMode.EraseByStroke; }));
            bar.Children.Add(ToolUi.Button("撤销", Undo)); bar.Children.Add(ToolUi.Button("清空", () => Ink.Strokes.Clear()));
            bar.Children.Add(ToolUi.Button("白板 / 透明", () => Ink.Background = Ink.Background == Brushes.White ? Brushes.Transparent : Brushes.White));
            Ink.PreviewMouseLeftButtonDown += (_, e) => { if (penMode == "arrow") { arrowStart = e.GetPosition(Ink); Ink.CaptureMouse(); e.Handled = true; } };
            Ink.PreviewMouseLeftButtonUp += (_, e) => { if (arrowStart is Point start) { AddArrow(start, e.GetPosition(Ink)); arrowStart = null; Ink.ReleaseMouseCapture(); e.Handled = true; } };
        } else {
            root.Children.Add(surface);
            if (mode == "spotlight") {
                var spot = new SpotlightSurface { IsHitTestVisible = false }; root.Children.Add(spot);
                surface.MouseMove += (_, e) => { spot.Center = e.GetPosition(surface); spot.InvalidateVisual(); };
                surface.MouseWheel += (_, e) => { spot.Radius = Math.Clamp(spot.Radius + Math.Sign(e.Delta) * 20, 40, 600); spot.InvalidateVisual(); };
                bar.Children.Add(ToolUi.Text("移动鼠标聚光 · 滚轮调整范围"));
            } else {
                bar.Children.Add(ToolUi.Text(mode == "capture" ? "拖动框选钉图区域 · 只读取选中区域" : "拖动空白处盖住答案 · 拖动遮挡移动 · 双击移除"));
                Point? start = null; Rectangle? selection = null;
                surface.MouseLeftButtonDown += (_, e) => {
                    if (e.OriginalSource != surface) return;
                    start = e.GetPosition(surface); selection = new Rectangle { Stroke = Brushes.DodgerBlue, StrokeThickness = 2, Fill = mode == "capture" ? new SolidColorBrush(Color.FromArgb(45, 70, 130, 255)) : Brushes.MidnightBlue, IsHitTestVisible = false };
                    surface.Children.Add(selection); surface.CaptureMouse(); e.Handled = true;
                };
                surface.MouseMove += (_, e) => {
                    if (start is not Point a || selection is null) return; var b = e.GetPosition(surface); var region = new Rect(a, b);
                    Canvas.SetLeft(selection, region.Left); Canvas.SetTop(selection, region.Top); selection.Width = region.Width; selection.Height = region.Height;
                };
                surface.MouseLeftButtonUp += (_, e) => {
                    if (start is not Point a || selection is null) return;
                    var region = new Rect(a, e.GetPosition(surface)); start = null; surface.Children.Remove(selection); selection = null; surface.ReleaseMouseCapture();
                    if (region.Width < 8 || region.Height < 8) return;
                    if (mode == "capture") {
                        var top = surface.PointToScreen(region.TopLeft); var bottom = surface.PointToScreen(region.BottomRight);
                        var physical = new NativeRect { Left = Math.Max(bounds.Left, (int)Math.Floor(top.X)), Top = Math.Max(bounds.Top, (int)Math.Floor(top.Y)), Right = Math.Min(bounds.Right, (int)Math.Ceiling(bottom.X)), Bottom = Math.Min(bounds.Bottom, (int)Math.Ceiling(bottom.Y)) };
                        CaptureRequested?.Invoke(physical);
                    } else AddMask(region);
                    e.Handled = true;
                };
                if (mode == "mask") bar.Children.Add(ToolUi.Button("清空遮挡", () => surface.Children.Clear()));
            }
        }
        bar.Children.Add(ToolUi.Button("退出（Esc）", Close)); root.Children.Add(bar);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Undo(); e.Handled = true; } };
    }
    internal void Undo()
    {
        if (history.Count == 0) return; restoring = true; try { var saved = history.Pop(); Ink.Strokes.Clear(); Ink.Strokes.Add(saved); } finally { previous = Ink.Strokes.Clone(); restoring = false; }
    }
    internal void AddArrow(Point from, Point to)
    {
        Vector direction = to - from; if (direction.Length < 8) return; direction.Normalize(); var side = new Vector(-direction.Y, direction.X); var neck = to - direction * 18;
        var points = new StylusPointCollection(new[] { from, to, neck + side * 9, to, neck - side * 9 });
        Ink.Strokes.Add(new Stroke(points, new DrawingAttributes { Color = Colors.Red, Width = 4, Height = 4 }));
    }
    internal void AddMask(Rect region)
    {
        var content = new Grid(); var mask = new Border { Width = region.Width, Height = region.Height, Background = Brushes.MidnightBlue, BorderBrush = Brushes.SlateBlue, BorderThickness = new Thickness(2), Child = content, Cursor = System.Windows.Input.Cursors.SizeAll };
        content.Children.Add(new TextBlock { Text = "答案遮挡 · 双击揭开", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        var grip = new Thumb { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = System.Windows.Input.Cursors.SizeNWSE };
        grip.DragDelta += (_, e) => { mask.Width = Math.Clamp(mask.Width + e.HorizontalChange, 60, Math.Max(60, surface.ActualWidth - Canvas.GetLeft(mask))); mask.Height = Math.Clamp(mask.Height + e.VerticalChange, 40, Math.Max(40, surface.ActualHeight - Canvas.GetTop(mask))); }; content.Children.Add(grip);
        Point? drag = null;
        mask.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is Thumb) return; if (e.ClickCount == 2) { surface.Children.Remove(mask); e.Handled = true; return; } drag = e.GetPosition(mask); mask.CaptureMouse(); e.Handled = true; };
        mask.MouseMove += (_, e) => { if (drag is Point offset && e.LeftButton == MouseButtonState.Pressed) { var at = e.GetPosition(surface); Canvas.SetLeft(mask, Math.Clamp(at.X - offset.X, 0, Math.Max(0, surface.ActualWidth - mask.Width))); Canvas.SetTop(mask, Math.Clamp(at.Y - offset.Y, 0, Math.Max(0, surface.ActualHeight - mask.Height))); } };
        mask.MouseLeftButtonUp += (_, e) => { drag = null; mask.ReleaseMouseCapture(); e.Handled = true; };
        Canvas.SetLeft(mask, region.Left); Canvas.SetTop(mask, region.Top); surface.Children.Add(mask);
    }
    internal void Place(NativeRect rect)
    {
        if (!SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, 0)) { Close(); throw new InvalidOperationException("无法定位课堂工具窗口。"); }
    }
}
