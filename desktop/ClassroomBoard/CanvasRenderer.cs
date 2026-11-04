using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using SystemColors = System.Windows.SystemColors;

namespace ClassroomBoard;

public sealed partial class Renderer
{
    private sealed record CanvasPart(JsonNode Widget, Rect Bounds, List<List<TextSlice>> Pages);

    private static Geometry ShapeOutline(JsonNode item, Rect box, double radius)
    {
        string shape = item.Str("kind") == "shape" ? item.Str("shape") : "rectangle";
        if (shape == "ellipse") return new EllipseGeometry(box);
        if (shape == "pill") {
            double corner = Math.Min(box.Width, box.Height) / 2;
            return new RectangleGeometry(box, corner, corner);
        }
        if (shape == "custom" && item["vertices"] is JsonArray { Count: >= 3 } vertices) {
            var points = vertices.Select(point => new Point(box.X + box.Width * point.Int("x") / 100,
                box.Y + box.Height * point.Int("y") / 100)).ToArray();
            var polygon = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var drawing = polygon.Open()) {
                drawing.BeginFigure(points[0], isFilled: true, isClosed: true);
                drawing.PolyLineTo(points.Skip(1).ToArray(), isStroked: true, isSmoothJoin: false);
            }
            return polygon;
        }
        string path = shape switch {
            "triangle" => "M50,0 L100,100 0,100 Z",
            "diamond" => "M50,0 L100,50 50,100 0,50 Z",
            "hexagon" => "M25,0 L75,0 100,50 75,100 25,100 0,50 Z",
            "star" => "M50,0 L61,35 98,35 68,57 79,94 50,71 21,94 32,57 2,35 39,35 Z",
            _ => ""
        };
        if (path.Length == 0) return new RectangleGeometry(box, radius, radius);
        var outline = Geometry.Parse(path).Clone();
        outline.Transform = new MatrixTransform(box.Width / 100, 0, 0, box.Height / 100, box.X, box.Y);
        return outline;
    }

    private Frame RenderCanvas(ClientState state, DateTimeOffset clock, int width, int height, int requestedPage)
    {
        var snapshot = state.Snapshot!;
        var classroom = snapshot["classroom"]!;
        var layout = EffectiveLayout(state, clock);
        bool highContrast = SystemParameters.HighContrast;
        string theme = highContrast ? "system_high_contrast" : layout.Str("theme", "mica_light");
        bool dark = theme == "mica_dark";
        bool reduced = highContrast || state.ReduceEffects || !SystemParameters.ClientAreaAnimation || !TransparencyEnabled();
        string material = reduced ? "solid" : layout.Str("material", "subtle");
        string reason = highContrast ? "系统高对比" : state.ReduceEffects ? "本机减少效果" : reduced ? "系统减少效果或关闭透明" : material == "solid" ? "班级配置" : "";
        Brush text = highContrast ? SystemColors.WindowTextBrush : BrushOf(dark ? "#F1F3F7" : "#252A32");
        Brush muted = highContrast ? text : BrushOf(dark ? "#C4CBD5" : "#565F6D");
        Brush accent = highContrast ? text : BrushOf(dark ? "#A6D9BE" : "#316B59");
        double left = layout.Str("iconMargin") == "left" ? 264 : 48;
        double right = layout.Str("iconMargin") == "right" ? 264 : 48;
        double areaWidth = 1920 - left - right - 48;
        bool expired = state.ClockUncertain || state.LastContact > 0 && clock.ToUnixTimeSeconds() - state.ClockOffset - state.LastContact > 72 * 3600;
        var contents = (snapshot["contents"] as JsonArray ?? new()).Where(n => n is not null && n.Str("state") == "published" &&
            DateTimeOffset.TryParse(n.Str("from"), out var start) && DateTimeOffset.TryParse(n.Str("until"), out var end) && start <= clock && clock < end && !expired).Select(n => n!).ToList();
        var courses = Periods(classroom, clock, out bool configured, out int currentCourse, out _);
        var parts = new List<CanvasPart>();
        int daysLeft = DateTime.TryParseExact(classroom.Str("eventDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var eventDate)
            ? (eventDate.Date - clock.Date).Days : -1;
        string ResolveText(JsonNode item)
        {
            if (!item.Flag("dynamicText")) return item.Str("text");
            return Regex.Replace(item.Str("text"), @"\{\{(班级|日期|时间|作业数|活动|倒计时|寄语)\}\}", match => match.Groups[1].Value switch {
                "班级" => classroom.Str("name"),
                "日期" when !state.ClockUncertain => clock.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "时间" when !state.ClockUncertain => clock.ToString("HH:mm", CultureInfo.InvariantCulture),
                "作业数" when !expired => contents.Count(entry => entry.Str("kind") == "homework").ToString(CultureInfo.InvariantCulture),
                "活动" when !expired && classroom.Str("event").Length > 0 => classroom.Str("event"),
                "倒计时" when !expired && daysLeft >= 0 && classroom.Str("event").Length > 0 => $"{daysLeft} 天",
                "寄语" when !expired && classroom.Str("message").Length > 0 => classroom.Str("message"),
                _ => "—"
            });
        }
        // 编辑器与客户端共用 1600×900 设计坐标；所有组件按图层顺序绘制，允许叠放。
        foreach (var widget in (layout["canvas"] as JsonArray ?? new()).Where(w => w is not null && w.Flag("visible", true))) {
            var item = widget!;
            string kind = item.Str("kind");
            var bounds = new Rect(left + 24 + item.Int("x") / 1600d * areaWidth, 72 + item.Int("y"), item.Int("w") / 1600d * areaWidth, item.Int("h"));
            var entries = new List<JsonNode>();
            if (!expired && kind is "homework" or "todo") {
                string subject = kind == "homework" ? item.Str("subjectFilter") : "";
                var ordered = contents.Where(c => c.Str("kind") == kind && (subject.Length == 0 || c.Str("subject") == subject)).OrderByDescending(c => c.Flag("priority"));
                if (kind == "homework") {
                    ordered = ordered.ThenBy(c => c.Str("subject"), StringComparer.Ordinal)
                        .ThenByDescending(c => c.Str("created"), StringComparer.Ordinal)
                        .ThenBy(c => c.Str("id"), StringComparer.Ordinal);
                }
                foreach (var content in ordered)
                    entries.Add(new JsonObject { ["subject"] = content.Str("subject"), ["body"] = kind == "homework" ? content.Str("subject") + " · " + content.Str("body") : content.Str("body") });
            } else if (!expired && kind == "timetable") {
                for (int index = 0; index < courses.Count; index++) entries.Add(new JsonObject { ["subject"] = index.ToString(), ["body"] = $"{index + 1}  {courses[index]}" });
            } else if (kind == "text" || !expired && kind == "message") {
                entries.Add(new JsonObject { ["subject"] = "", ["body"] = kind == "text" ? ResolveText(item) : classroom.Str("message") });
            }
            bool hasTitle = kind != "text" && item.Flag("showTitle", true);
            double size = item.Int("fontSize", 32), pad = hasTitle ? 48 : 32;
            double inset = hasTitle ? 46 + item.Int("titleSize", 28) * 1.5 : 32;
            var pages = Paginate(entries, size, bounds.Width - pad, Math.Max(1, (bounds.Height - inset) / (size * item.Num("lineHeight", 1.5))), WidgetFont(item), keepWhole: kind == "homework");
            parts.Add(new(item, bounds, pages));
        }
        int count = Math.Max(1, parts.Select(p => p.Pages.Count).DefaultIfEmpty(1).Max());
        string stable = "canvas-composition-v1:" + string.Join("|", contents.Select(c => $"{c.Str("id")}:{c.Int("revision")}")) + layout.ToJsonString() + classroom.Str("message") + string.Join("|", courses) + clock.ToString("yyyy-MM-dd") + $"{width}x{height}:{expired}:{count}";
        string pageSet = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stable)));
        bool sameSet = pageSet == state.PageSetId;
        int page = requestedPage > 0 ? requestedPage : sameSet && state.ManualUntil > clock.ToUnixTimeSeconds() ? state.Page :
            sameSet && state.AutoPageStartedAt > 0 ? (int)(Math.Max(0, clock.ToUnixTimeSeconds() - state.AutoPageStartedAt) / Math.Max(15, layout.Int("interval", 20)) % count) + 1 : 1;
        page = Math.Clamp(page, 1, count);
        if (requestedPage > 0 && (!sameSet || requestedPage > count)) throw new InvalidDataException("页集合已改变，旧翻页命令已拒绝。");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) {
            dc.PushTransform(new ScaleTransform(width / 1920d, height / 1080d));
            dc.DrawDrawing(Background(layout, theme, material, left, right, highContrast, []));
            foreach (var part in parts) {
                var item = part.Widget; var box = part.Bounds;
                string kind = item.Str("kind"), surface = item.Str("surface", "glass");
                double radius = item.Int("radius", 24), size = item.Int("fontSize", 32);
                double lineHeight = size * item.Num("lineHeight", 1.5);
                var typeface = WidgetFont(item);
                Brush ink = !highContrast && item.Str("textColor").Length > 0 ? BrushOf(item.Str("textColor")) : text;
                var outline = ShapeOutline(item, box, radius);
                dc.PushClip(outline);
                if (!highContrast && item.Str("backgroundImage").Length > 0) {
                    dc.DrawRectangle(BrushOf(item.Str("fillColor", "#FFFFFF")), null, box);
                    dc.DrawRectangle(PictureBrush(item.Str("backgroundImage"), item.Str("backgroundFit", "cover"), item.Str("backgroundPosition", "center")), null, box);
                }
                if (surface != "none") {
                    var color = (Color)ColorConverter.ConvertFromString(item.Str("fillColor", "#FFFFFF"));
                    color.A = (byte)Math.Round(item.Int("opacity", 45) * 2.55);
                    Brush fill = highContrast ? SystemColors.WindowBrush : new SolidColorBrush(color);
                    dc.DrawGeometry(fill, surface == "glass" ? new Pen(highContrast ? text : BrushOf("#80FFFFFF"), 1) : null, outline);
                }
                void Label(string value, double x, double y, double font, double span, Brush? brush = null) {
                    var formatted = Text(value, font, brush ?? ink, typeface);
                    formatted.MaxTextWidth = Math.Max(1, span); formatted.MaxLineCount = 1; formatted.Trimming = TextTrimming.CharacterEllipsis;
                    if (item.Str("align") == "center") formatted.TextAlignment = TextAlignment.Center;
                    if (item.Str("align") == "right") formatted.TextAlignment = TextAlignment.Right;
                    dc.DrawText(formatted, new Point(x, y));
                }
                if (kind == "image" && item.Str("image").Length > 0) {
                    using var stream = new MemoryStream(Convert.FromBase64String(item.Str("image").Split(',', 2)[1]));
                    var picture = new BitmapImage(); picture.BeginInit(); picture.CacheOption = BitmapCacheOption.OnLoad; picture.StreamSource = stream; picture.EndInit(); picture.Freeze();
                    double scale = Math.Min(box.Width / picture.PixelWidth, box.Height / picture.PixelHeight);
                    double w = picture.PixelWidth * scale, h = picture.PixelHeight * scale;
                    dc.DrawImage(picture, new Rect(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h));
                } else if (kind == "datetime") {
                    double large = size + 24, top = box.Y + (box.Height - (34 + 8 + large * 1.2 + 31)) / 2;
                    Label(classroom.Str("name"), box.X + 24, top, 24, box.Width - 48);
                    Label(state.ClockUncertain ? "--:--" : clock.ToString("HH:mm"), box.X + 24, top + 38, large, box.Width - 48);
                    Label(state.ClockUncertain ? "请现场校准系统时间" : clock.ToString("M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN")), box.X + 24, top + 42 + large * 1.2, 22, box.Width - 48);
                } else if (kind == "countdown" && !expired) {
                    double large = size + 24, top = box.Y + (box.Height - (34 + large * 1.2 + 28)) / 2;
                    bool valid = DateTime.TryParseExact(classroom.Str("eventDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && classroom.Str("event").Length > 0;
                    int days = (date.Date - clock.Date).Days;
                    Label(valid ? "距离" + classroom.Str("event") : "事件倒计时", box.X + 24, top, 24, box.Width - 48);
                    Label(valid && days >= 0 ? days == 0 ? "就是今天" : $"{days} 天" : "等待设置", box.X + 24, top + 34, large, box.Width - 48);
                    Label(valid ? classroom.Str("eventDate") : "在班级设置中填写事件和日期", box.X + 24, top + 34 + large * 1.2, 20, box.Width - 48);
                } else if (kind is not ("image" or "shape")) {
                    var slices = part.Pages[(page - 1) % part.Pages.Count];
                    string title = item.Str("title");
                    if (kind == "homework" && slices.FirstOrDefault() is { Continued: true } first)
                        title = "续 · " + (first.Subject.Length > 0 ? first.Subject : "作业");
                    bool hasTitle = kind != "text" && item.Flag("showTitle", true);
                    if (hasTitle) Label(title, box.X + 24, box.Y + 18, item.Int("titleSize", 28), box.Width - 48);
                    double padding = hasTitle ? 24 : 16, y = box.Y + (hasTitle ? 26 + item.Int("titleSize", 28) * 1.5 : 16);
                    if (expired && kind != "text") Label("等待恢复连接", box.X + padding, y, 24, box.Width - padding * 2, muted);
                    else {
                        // 每个组件独立轮换。短组件循环显示，不会在其他组件翻页时突然空白。
                        string subject = item.Str("subjectFilter");
                        if (slices.Count == 0) Label(kind == "homework" ? subject.Length > 0 ? $"暂无{subject}作业" : "今日暂无作业" : kind == "todo" ? "暂无提醒" : kind == "timetable" ? configured ? "今日无课程" : "课表待配置" : "", box.X + padding, y, 24, box.Width - padding * 2, muted);
                        foreach (var slice in slices) {
                            Brush lineBrush = kind == "timetable" && slice.Subject == currentCourse.ToString() ? accent : ink;
                            foreach (string line in slice.Lines) { Label(line, box.X + padding, y, size, box.Width - padding * 2, lineBrush); y += lineHeight; }
                            y += lineHeight * .45;
                        }
                    }
                }
                dc.Pop();
            }
            string lastSync = state.LastContentSync > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)state.LastContentSync).ToOffset(TimeSpan.FromHours(8)).ToString("M月d日 HH:mm") : "尚未同步";
            string status = expired ? state.ClockUncertain ? "系统时间异常，时效内容已暂停，请现场校时" : "离线超过 72 小时，时效内容已暂停" : $"内容同步 {lastSync} · 画面生成 {clock:MM月dd日 HH:mm}";
            DrawText(dc, status, left + 24, 990, 22, muted, areaWidth - 220);
            DrawText(dc, $"第 {page} / {count} 页", 1920 - right - 204, 990, 22, muted, 180);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string output = Path.Combine(directory, $"board-{pageSet[..12]}-{clock:yyyyMMddHHmm}-{page}-{Guid.NewGuid():N}.png"), temp = output + ".tmp";
        using (var stream = File.Create(temp)) encoder.Save(stream);
        File.Move(temp, output, true);
        return new(output, pageSet, page, count, theme, material, reason);
    }
}
