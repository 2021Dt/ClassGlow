using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;

namespace ClassroomBoard;

/// <summary>与网页共享 1600×900 坐标，只在内存解码图片，不创建持久缓存。</summary>
internal static class NoticeCanvas
{
    private static SolidColorBrush Brush(string hex, double opacity = 100)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 100) * 2.55), c.R, c.G, c.B));
        brush.Freeze(); return brush;
    }

    public static FrameworkElement Build(JsonObject design, out List<TextBlock> counters)
    {
        counters = [];
        var stage = new Canvas { Width = 1600, Height = 900, ClipToBounds = true, Background = Brush(design.Str("backgroundColor", "#102a43"), design.Int("backgroundOpacity", 35)) };
        foreach (var node in design["elements"]!.AsArray().Take(12)) {
            var item = node!.AsObject();
            if (item["hidden"]?.GetValue<bool>() == true) continue;
            double width = item.Int("w"), height = item.Int("h"), radius = item.Int("radius");
            string kind = item.Str("kind");
            var box = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(radius), Background = kind == "card" ? Brush(item.Str("fillColor", "#215544"), item.Int("opacity", 95)) : Brushes.Transparent };
            if (kind == "image") {
                string data = item.Str("image");
                if (!data.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) || data.Length > 240000) throw new InvalidOperationException("提醒图片格式不正确");
                using var stream = new MemoryStream(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]));
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                box.Child = new Image { Source = bitmap, Stretch = item.Str("imageFit") == "cover" ? Stretch.UniformToFill : Stretch.Uniform, Opacity = Math.Clamp(item.Int("opacity", 95), 0, 100) / 100d };
                box.Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
            } else {
                double padding = kind == "card" ? 16 : 4, availableWidth = Math.Max(1, width - 2 * padding), availableHeight = Math.Max(1, height - 2 * padding);
                var text = new TextBlock {
                    Text = kind == "countdown" ? "时间到" : item.Str("text"),
                    FontFamily = new FontFamily(item.Str("fontFamily") switch { "serif" => "SimSun, Microsoft YaHei", "kai" => "KaiTi, Microsoft YaHei", _ => "Microsoft YaHei UI, Microsoft YaHei" }),
                    FontSize = Math.Clamp(item.Int("fontSize", 56), 24, 160),
                    FontWeight = item["bold"]?.GetValue<bool>() == false ? FontWeights.Normal : FontWeights.Bold,
                    Foreground = Brush(item.Str("textColor", "#ffffff")),
                    TextAlignment = item.Str("align") switch { "left" => TextAlignment.Left, "right" => TextAlignment.Right, _ => TextAlignment.Center },
                    TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                    Width = availableWidth, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                };
                // 同时测量数字与结束文案，确保窄组件也不会裁掉倒计时。
                for (int i = 0; i < 70; i++) {
                    text.LineHeight = text.FontSize * 1.3;
                    text.Measure(new Size(availableWidth, double.PositiveInfinity));
                    double measured = text.DesiredSize.Height;
                    if (kind == "countdown") { text.Text = "60:00"; text.Measure(new Size(availableWidth, double.PositiveInfinity)); measured = Math.Max(measured, text.DesiredSize.Height); text.Text = "时间到"; }
                    if (measured <= availableHeight || text.FontSize <= .25) break;
                    text.FontSize = Math.Max(.25, text.FontSize * .9);
                }
                box.Padding = new Thickness(padding); box.Child = text;
                if (kind == "countdown") counters.Add(text);
            }
            Canvas.SetLeft(box, item.Int("x")); Canvas.SetTop(box, item.Int("y")); stage.Children.Add(box);
        }
        return new Viewbox { Child = stage, Stretch = Stretch.Uniform };
    }
}
