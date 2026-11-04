using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using Point = System.Windows.Point;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using SystemColors = System.Windows.SystemColors;
using Pen = System.Windows.Media.Pen;
using FontFamily = System.Windows.Media.FontFamily;

namespace ClassroomBoard;

public sealed record Frame(string Path, string PageSetId, int Page, int Count, string Theme, string Material, string Reason);
public sealed record TextSlice(string Subject, List<string> Lines, bool Continued, bool ShowSubject = false);

public static class JsonRead
{
    public static string Str(this JsonNode? node, string key, string fallback = "") => node?[key]?.GetValue<string>() ?? fallback;
    public static int Int(this JsonNode? node, string key, int fallback = 0) => node?[key]?.GetValue<int>() ?? fallback;
    public static bool Flag(this JsonNode? node, string key, bool fallback = false) => node?[key]?.GetValue<bool>() ?? fallback;
    public static double Num(this JsonNode? node, string key, double fallback = 0) => node?[key]?.GetValue<double>() ?? fallback;
}

public sealed partial class Renderer(string directory)
{
    private static readonly Typeface Font = new("Microsoft YaHei");
    private readonly Dictionary<string, DrawingGroup> backgrounds = new();
    private readonly Dictionary<string, BitmapSource> pictures = new();
    private static Brush BrushOf(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    private static FormattedText Text(string text, double size, Brush brush, Typeface? typeface = null) => new(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, typeface ?? Font, size, brush, 1);
    private static Typeface WidgetFont(JsonNode widget)
    {
        string family = widget.Str("fontFamily", "sans") switch {
            "serif" => "SimSun, Microsoft YaHei", "kai" => "KaiTi, Microsoft YaHei", _ => "Microsoft YaHei UI, Microsoft YaHei"
        };
        return new(new FontFamily(family), FontStyles.Normal, FontWeight.FromOpenTypeWeight(widget.Int("fontWeight", 400)), FontStretches.Normal);
    }
    private ImageBrush PictureBrush(string data, string fit, string position)
    {
        if (!pictures.TryGetValue(data, out var picture)) {
            using var stream = new MemoryStream(Convert.FromBase64String(data.Split(',', 2)[1]));
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); picture = bitmap;
            // 仅缓存少量解码结果，不把每次换图永久积累在客户端内存中。
            if (pictures.Count >= 8) pictures.Clear();
            pictures[data] = picture;
        }
        return new ImageBrush(picture) {
            Stretch = fit == "contain" ? Stretch.Uniform : Stretch.UniformToFill,
            AlignmentX = position == "left" ? AlignmentX.Left : position == "right" ? AlignmentX.Right : AlignmentX.Center,
            AlignmentY = position == "top" ? AlignmentY.Top : position == "bottom" ? AlignmentY.Bottom : AlignmentY.Center
        };
    }
    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush, double width = 1800)
    {
        var formatted = Text(text, size, brush); formatted.MaxTextWidth = Math.Max(1, width);
        // 正文已经按实际字宽分页；标题、学科等单行标签不能隐式换行撑出卡片。
        formatted.MaxLineCount = 1; formatted.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(formatted, new Point(x, y));
    }
    public static List<string> Wrap(string text, double size, double width, Typeface? typeface = null)
    {
        var result = new List<string>();
        foreach (string paragraph in text.Split('\n')) {
            string line = "";
            foreach (var rune in paragraph.EnumerateRunes()) {
                string next = line + rune;
                if (line.Length > 0 && Text(next, size, Brushes.Black, typeface).WidthIncludingTrailingWhitespace > width) {
                    var glyphs = line.EnumerateRunes().ToArray();
                    string last = glyphs[^1].ToString(), current = rune.ToString();
                    // 与网页预览一致：优先避免中文标点悬在行首，且不突破实际字宽。
                    bool carry = glyphs.Length > 1 && ("，。！？；：、）】》”’,.!?;:)]}".Contains(current) || "（【《“‘([{".Contains(last)) && Text(last + current, size, Brushes.Black, typeface).WidthIncludingTrailingWhitespace <= width;
                    result.Add(carry ? line[..^last.Length] : line);
                    line = carry ? last + current : current;
                }
                else line = next;
            }
            result.Add(line);
        }
        return result;
    }
    public static List<List<TextSlice>> Paginate(IEnumerable<JsonNode> contents, double size, double width, double budget, Typeface? typeface = null, bool keepWhole = false, bool subjectLabels = false)
    {
        if (budget < 1 || width <= 0) throw new InvalidDataException("当前布局没有足够空间，请调整模块或字号。");
        var pages = new List<List<TextSlice>> { new() }; double used = 0;
        foreach (var item in contents) {
            var lines = Wrap(item.Str("body"), size, width, typeface); int offset = 0;
            double label = subjectLabels && item.Str("subject").Length > 0 && budget >= 1.9 ? .9 : 0;
            // 短作业整条换页，只有一页装不下的作业才拆成续页。
            if (keepWhole && used > 0 && lines.Count + label <= budget && used + .45 + label + lines.Count > budget) {
                pages.Add(new());
                used = 0;
            }
            while (offset < lines.Count) {
                double gap = pages[^1].Count > 0 ? .45 : 0;
                if (budget - used - gap - label < 1) { pages.Add(new()); used = 0; gap = 0; }
                int take = Math.Min(lines.Count - offset, (int)Math.Floor(budget - used - gap - label + 1e-9));
                pages[^1].Add(new(item.Str("subject"), lines.GetRange(offset, take), offset > 0, label > 0));
                offset += take; used += take + gap + label;
                if (pages.Count > 10000) throw new InvalidDataException("内容分页过多，保留上个完整画面。");
            }
        }
        return pages;
    }
    public static JsonNode EffectiveLayout(ClientState state, DateTimeOffset clock)
    {
        var classroom = state.Snapshot!["classroom"]!;
        double until = classroom.Num("premiumUntil");
        // 离线时也遵守已同步的许可期限；保留定制快照，使用服务端给出的默认布局回退。
        bool expired = until > 0 && (state.ClockUncertain || clock.ToUnixTimeSeconds() >= until);
        return expired && classroom["basicLayout"] is JsonObject basic ? basic : classroom["layout"]!;
    }
    public Frame Render(ClientState state, DateTimeOffset clock, int width, int height, int requestedPage = 0)
    {
        var snapshot = state.Snapshot ?? throw new InvalidDataException("没有可用快照。");
        var classroom = snapshot["classroom"]!; var layout = EffectiveLayout(state, clock);
        if (layout.Str("editor") == "canvas") return RenderCanvas(state, clock, width, height, requestedPage);
        var modules = layout["modules"]!;
        bool highContrast = SystemParameters.HighContrast;
        string theme = highContrast ? "system_high_contrast" : layout.Str("theme", "mica_light");
        bool dark = theme == "mica_dark";
        bool reduced = highContrast || state.ReduceEffects || !SystemParameters.ClientAreaAnimation || !TransparencyEnabled();
        string material = reduced ? "solid" : layout.Str("material", "solid");
        string reason = highContrast ? "系统高对比" : state.ReduceEffects ? "本机减少效果" : reduced ? "系统减少效果或关闭透明" : material == "solid" ? "班级配置" : "";
        Brush text = highContrast ? SystemColors.WindowTextBrush : BrushOf(dark ? "#F1F3F7" : "#252A32");
        Brush muted = highContrast ? text : BrushOf(dark ? "#C4CBD5" : "#565F6D");
        Brush accent = highContrast ? text : BrushOf(dark ? "#A6D9BE" : "#316B59");
        double fontSize = layout.Str("font") switch { "small" => 32, "large" => 40, _ => 36 };
        double left = layout.Str("iconMargin") == "left" ? 264 : 48, right = layout.Str("iconMargin") == "right" ? 264 : 48;
        double total = 1920 - left - right;
        bool reverse = layout.Flag("reverse");
        Rect ModuleRect(string key, int x, int y, int w, int h) {
            var node = layout["grid"]?[key]; x = node.Int("x", x); y = node.Int("y", y); w = node.Int("w", w); h = node.Int("h", h);
            if (x < 0 || y < 0 || w < 5 || h < 4 || x + w > 12 || y + h > 12) throw new InvalidDataException("模块超出网格边界。");
            if (reverse) x = 12 - x - w;
            return new Rect(left + 24 + x * (total - 48 + 28) / 12, 224 + y * 688 / 12, w * (total - 48 + 28) / 12 - 28, h * 688 / 12 - 28);
        }
        var jobRect = ModuleRect("homework", 0, 0, 7, 12); var todoRect = ModuleRect("todo", 7, 0, 5, 5); var tableRect = ModuleRect("timetable", 7, 5, 5, 7);
        bool offlineExpired = state.ClockUncertain || state.LastContact > 0 && (clock.ToUnixTimeSeconds() - state.ClockOffset - state.LastContact) > 72 * 3600;
        var contents = (snapshot["contents"] as JsonArray ?? new()).Where(n => n is not null && n.Str("state") == "published" &&
            DateTimeOffset.TryParse(n.Str("from"), out var start) && DateTimeOffset.TryParse(n.Str("until"), out var end) && start <= clock && clock < end && !offlineExpired).Select(n => n!).ToList();
        var jobs = Paginate(modules.Flag("homework") ? contents.Where(c => c.Str("kind") == "homework") : [], fontSize, jobRect.Width - 206, Math.Max(1, (jobRect.Height - 126) / (fontSize * 1.55)));
        var todos = Paginate(modules.Flag("todo") ? contents.Where(c => c.Str("kind") == "todo").OrderByDescending(c => c.Flag("priority")) : [], fontSize, todoRect.Width - 110, Math.Max(1, (todoRect.Height - 112) / (fontSize * 1.55)));
        int count = Math.Max(jobs.Count, todos.Count);
        string stableInput = string.Join("|", contents.Select(c => $"{c.Str("id")}:{c.Int("revision")}")) + layout.ToJsonString() + clock.ToString("yyyy-MM-dd") + $"{width}x{height}";
        string pageSet = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableInput)));
        bool sameSet = pageSet == state.PageSetId;
        int page = requestedPage > 0 ? requestedPage : sameSet && state.ManualUntil > clock.ToUnixTimeSeconds() ? state.Page :
            sameSet && state.AutoPageStartedAt>0 ? (int)(Math.Max(0,clock.ToUnixTimeSeconds()-state.AutoPageStartedAt) / Math.Max(15, layout.Int("interval", 20)) % count) + 1 : 1;
        page = Math.Clamp(page, 1, count);
        if (requestedPage > 0 && (!sameSet || requestedPage > count)) throw new InvalidDataException("页集合已改变，旧翻页命令已拒绝。");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) {
            dc.PushTransform(new ScaleTransform(width / 1920d, height / 1080d));
            var panels = new List<Rect>();
            if (modules.Flag("homework")) panels.Add(jobRect);
            if (modules.Flag("todo")) panels.Add(todoRect);
            if (modules.Flag("timetable")) panels.Add(tableRect);
            dc.DrawDrawing(Background(layout, theme, material, left, right, highContrast, panels));
            DrawText(dc, classroom.Str("name"), left + 59, 66, 48, text, total - 698);
            if (modules.Flag("datetime", true) && !state.ClockUncertain) {
                DrawText(dc, clock.ToString("yyyy年M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN")), left + 59, 130, 28, muted);
                DrawText(dc, clock.ToString("HH:mm"), 1920 - right - 289, 60, 72, text);
            }
            if (!offlineExpired && modules.Flag("countdown") && DateTime.TryParseExact(classroom.Str("eventDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var target)) {
                int days = (target.Date - clock.Date).Days;
                if (days >= 0 && classroom.Str("event").Length > 0) { DrawText(dc, "距离" + classroom.Str("event"), 1920 - right - 674, 74, 24, muted, 350); DrawText(dc, days == 0 ? "就是今天" : $"还有 {days} 天", 1920 - right - 674, 115, 36, text); }
            }
            void Title(string title, double x, double y) { dc.DrawRoundedRectangle(accent, null, new Rect(x + 30, y + 35, 5, 38), 2, 2); DrawText(dc, title, x + 52, y + 24, 40, text); }
            if (modules.Flag("homework") && !offlineExpired) {
                dc.PushClip(new RectangleGeometry(jobRect, 32, 32));
                Title("今日作业", jobRect.X, jobRect.Y); double y = jobRect.Y + 96;
                if (jobs[page - 1 < jobs.Count ? page - 1 : 0].Count == 0) DrawText(dc, "今天还没有发布作业", jobRect.X + 40, y + 20, 32, muted);
                if (page <= jobs.Count) foreach (var slice in jobs[page - 1]) {
                    DrawText(dc, slice.Subject, jobRect.X + 32, y + 5, 28, accent, 112);
                    if (slice.Continued) DrawText(dc, "续", jobRect.X + 150, y + 12, 18, accent, 24);
                    foreach (var line in slice.Lines) { DrawText(dc, line, jobRect.X + 177, y, fontSize, text, jobRect.Width - 205); y += fontSize * 1.55; }
                    y += fontSize * 1.55 * .45;
                }
                dc.Pop();
            }
            if (modules.Flag("todo") && !offlineExpired) {
                dc.PushClip(new RectangleGeometry(todoRect, 32, 32));
                Title("今日提醒", todoRect.X, todoRect.Y); double y = todoRect.Y + 96;
                if (contents.All(c => c.Str("kind") != "todo")) DrawText(dc, "暂无提醒", todoRect.X + 40, y, 32, muted);
                if (page <= todos.Count) foreach (var slice in todos[page - 1]) {
                    DrawText(dc, "•", todoRect.X + 32, y, fontSize, accent);
                    foreach (var line in slice.Lines) { DrawText(dc, line, todoRect.X + 70, y, fontSize, text, todoRect.Width - 100); y += fontSize * 1.55; }
                    y += fontSize * 1.55 * .45;
                }
                dc.Pop();
            }
            if (modules.Flag("timetable") && !offlineExpired) {
                dc.PushClip(new RectangleGeometry(tableRect, 32, 32));
                double top = tableRect.Y; Title("今日课表", tableRect.X, top);
                var periods = Periods(classroom, clock, out bool configured, out int currentCourse, out string nextCourse);
                if (nextCourse.Length > 0) DrawText(dc, nextCourse, tableRect.X + tableRect.Width * .5, top + 36, 22, muted, tableRect.Width * .5 - 30);
                for (int i = 0; i < periods.Count; i++) {
                    double courseSize = periods.Count > 8 || periods.Any(p => p.Length > 4) ? 32 : fontSize;
                    double y = top + 100 + (i / 2) * (periods.Count > 8 ? 40 : 56);
                    // 最多 12 节需六行；较多节次时使用单独文字布局，不覆盖页脚。
                    if (y + courseSize > top + tableRect.Height - 12) throw new InvalidDataException("课表超出当前模块尺寸，请调整布局后再发布。");
                    Brush courseBrush = i == currentCourse ? accent : text;
                    double x = tableRect.X + 34 + i % 2 * (tableRect.Width / 2);
                    if (i == currentCourse) dc.DrawRectangle(accent, null, new Rect(x - 10, y + 5, 3, courseSize));
                    DrawText(dc, $"{i + 1} {periods[i]}", x, y, courseSize, courseBrush, tableRect.Width / 2 - 40);
                }
                if (periods.Count == 0) DrawText(dc, configured ? "今日无课程" : "课表待配置", tableRect.X + 36, top + 120, 28, muted, tableRect.Width - 70);
                dc.Pop();
            }
            double syncY=986;
            if (modules.Flag("message") && !offlineExpired) {
                string message=string.Join(" ",classroom.Str("message").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
                var formatted=Text(message,30,text); formatted.MaxTextWidth=total-348; formatted.LineHeight=36;
                if(formatted.Height>78) throw new InvalidDataException("寄语超出两行，请缩短后再发布");
                dc.DrawText(formatted,new Point(left+56,924)); syncY=Math.Max(986,924+formatted.Height+3);
            }
            if (offlineExpired) DrawText(dc, state.ClockUncertain ? "系统时间异常，时效内容已暂停，请现场校时" : "设备离线超过 72 小时，等待重新连接", left + 40, 290, 36, text);
            string lastSync = state.LastContentSync > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)state.LastContentSync).ToOffset(TimeSpan.FromHours(8)).ToString("M月d日 HH:mm") : "尚未同步";
            bool offline = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - state.LastContact > 90;
            DrawText(dc, $"内容同步 {lastSync} · 画面生成 {clock:MM月dd日 HH:mm}" + (offline ? " · 离线" : ""), left + 56, syncY, 22, muted, total - 328);
            DrawText(dc, $"第 {page} / {count} 页", 1920 - right - 244, 958, 26, muted);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        // 画面使用独立文件，设置失败不会改写系统当前指向的旧图片。
        string output = Path.Combine(directory, $"board-{pageSet[..12]}-{clock:yyyyMMddHHmm}-{page}-{Guid.NewGuid():N}.png"), temp = output + ".tmp";
        using (var stream = File.Create(temp)) encoder.Save(stream);
        File.Move(temp, output, true);
        return new(output, pageSet, page, count, theme, material, reason);
    }
    private DrawingGroup Background(JsonNode layout, string theme, string material, double left, double right, bool highContrast, IEnumerable<Rect> panels)
    {
        bool dark = theme == "mica_dark";
        string backgroundMode = layout.Str("backgroundMode", "mica");
        double opacity = layout.Int("surfaceOpacity", 38) / 100d;
        Brush background = highContrast ? SystemColors.WindowBrush : BrushOf(backgroundMode == "color" ? layout.Str("backgroundColor", "#E7E8EB") : dark ? "#25272C" : "#E7E8EB");
        Brush surface = highContrast ? SystemColors.WindowBrush : BrushOf(dark ? "#2D3036" : "#F6F7F9");
        Brush accent = highContrast ? SystemColors.WindowTextBrush : BrushOf(dark ? "#A6D9BE" : "#316B59");
        string cacheKey = $"{theme}:{material}:{left}:{right}:{layout.ToJsonString()}";
        if (!backgrounds.TryGetValue(cacheKey, out var layer)) {
            layer = new DrawingGroup(); using (var bg = layer.Open()) {
                bg.DrawRectangle(background, null, new Rect(0, 0, 1920, 1080));
                BitmapSource? blurred = null;
                if (!highContrast && backgroundMode == "mica") {
                    var mica = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(dark ? "#292C32" : "#ECEDEF"), (Color)ColorConverter.ConvertFromString(dark ? "#202227" : "#DDE0E5"), 35);
                    bg.DrawRectangle(mica, null, new Rect(0, 0, 1920, 1080));
                }
                if (!highContrast && backgroundMode == "image" && layout.Str("backgroundImage").Length > 0) {
                    // 背景包含在快照内，断网时仍可还原；只在背景层变化时解码和模糊。
                    using var stream = new MemoryStream(Convert.FromBase64String(layout.Str("backgroundImage").Split(',', 2)[1]));
                    var picture = new BitmapImage(); picture.BeginInit(); picture.CacheOption = BitmapCacheOption.OnLoad;
                    picture.StreamSource = stream; picture.DecodePixelWidth = 1920; picture.EndInit(); picture.Freeze();
                    var imageBrush = new ImageBrush(picture) { Stretch = Stretch.UniformToFill };
                    bg.DrawRectangle(imageBrush, null, new Rect(0, 0, 1920, 1080));
                    if (material == "subtle" && layout.Str("editor") != "canvas") {
                        var backdrop = new DrawingVisual { Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 32 } };
                        using (var context = backdrop.RenderOpen()) context.DrawRectangle(imageBrush, null, new Rect(0, 0, 1920, 1080));
                        var bitmapBackground = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                        bitmapBackground.Render(backdrop); bitmapBackground.Freeze(); blurred = bitmapBackground;
                    }
                }
                void Panel(double x, double y, double w, double h, bool inner = false) {
                    var bounds = new Rect(x, y, w, h);
                    double radius = inner ? 32 : 0;
                    if (!inner && !highContrast && layout.Str("panelImage").Length > 0) {
                        // 底板始终覆盖完整显示区域，图片在底板内按老师选择的方式铺放。
                        bg.PushClip(new RectangleGeometry(bounds, radius, radius));
                        bg.DrawRectangle(surface, null, bounds);
                        bg.DrawRectangle(PictureBrush(layout.Str("panelImage"), layout.Str("panelImageFit", "cover"), layout.Str("panelImagePosition", "center")), null, bounds);
                        bg.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), (byte)(dark ? 38 : 249), (byte)(dark ? 41 : 250), (byte)(dark ? 46 : 252))), null, bounds);
                        bg.Pop();
                        bg.DrawRoundedRectangle(null, new Pen(BrushOf("#80FFFFFF"), 1), bounds, radius, radius);
                        return;
                    }
                    if (material != "subtle" || highContrast) {
                        bg.DrawRoundedRectangle(surface, new Pen(accent, .5), bounds, radius, radius);
                        return;
                    }
                    // 子卡片共享大容器的背景模糊，仅绘制薄底与亮边，防止玻璃越叠越白。
                    if (inner) {
                        var fill = new LinearGradientBrush(Color.FromArgb((byte)(dark ? 13 : 38), 255, 255, 255), Color.FromArgb(10, (byte)(dark ? 0 : 255), (byte)(dark ? 0 : 255), (byte)(dark ? 0 : 255)), 45);
                        var rim = new LinearGradientBrush(Color.FromArgb(132, 255, 255, 255), Color.FromArgb(24, 255, 255, 255), 45);
                        bg.DrawRoundedRectangle(fill, new Pen(rim, 1), bounds, radius, radius);
                        return;
                    }
                    // 多层低透明阴影只缓存一次；亮边与渐变表面分层绘制，不降低文字透明度。
                    // 阴影仅落在卡片外侧，透明表面下不能再叠一整块暗色底。
                    bg.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(x - 28, y - 28, w + 56, h + 64)), new RectangleGeometry(bounds, radius, radius)));
                    for (int step = 12; step >= 1; step--) {
                        var shadow = new SolidColorBrush(Color.FromArgb((byte)(dark ? 3 : 1), 25, 28, 32));
                        bg.DrawRoundedRectangle(shadow, null, new Rect(x - step * 2, y + 8 - step * 2, w + step * 4, h + step * 4), radius + step * 2, radius + step * 2);
                    }
                    bg.Pop();
                    if (blurred is not null) {
                        bg.PushClip(new RectangleGeometry(bounds, radius, radius));
                        bg.DrawImage(blurred, new Rect(0, 0, 1920, 1080)); bg.Pop();
                    }
                    Color Tint(double alpha) => dark ? Color.FromArgb((byte)(alpha * 255), 38, 41, 46) : Color.FromArgb((byte)(alpha * 255), 249, 250, 252);
                    var glass = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                    glass.GradientStops.Add(new(Tint(Math.Min(.98, opacity + .03)), 0));
                    glass.GradientStops.Add(new(Tint(Math.Max(.10, opacity - .10)), 1));
                    var edge = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                    edge.GradientStops.Add(new((Color)ColorConverter.ConvertFromString(dark ? "#90FFFFFF" : "#F0FFFFFF"), 0));
                    edge.GradientStops.Add(new((Color)ColorConverter.ConvertFromString("#10FFFFFF"), .48));
                    edge.GradientStops.Add(new((Color)ColorConverter.ConvertFromString(dark ? "#38FFFFFF" : "#A0FFFFFF"), 1));
                    bg.DrawRoundedRectangle(glass, new Pen(edge, 1.2), bounds, radius, radius);
                    if (inner) bg.DrawRoundedRectangle(null, new Pen(BrushOf(dark ? "#30000000" : "#12000000"), .7), new Rect(x + 1.5, y + 1.5, w - 3, h - 3), radius - 1.5, radius - 1.5);
                }
                Panel(0, 0, 1920, 1080);
                foreach (var panel in panels) Panel(panel.X, panel.Y, panel.Width, panel.Height, true);
            }
            layer.Freeze(); if (backgrounds.Count >= 2) backgrounds.Clear(); backgrounds[cacheKey] = layer;
        }
        return layer;
    }
    private static bool TransparencyEnabled()
    {
        try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) is int value && value != 0; }
        catch { return false; }
    }
    private static List<string> Periods(JsonNode classroom, DateTimeOffset clock, out bool configured, out int currentCourse, out string nextCourse)
    {
        string day = clock.ToString("yyyy-MM-dd"); int weekday = (int)clock.DayOfWeek == 0 ? 7 : (int)clock.DayOfWeek;
        var template = (classroom["templates"] as JsonArray ?? new()).FirstOrDefault(t => string.CompareOrdinal(t.Str("effectiveFrom"), day) <= 0 && string.CompareOrdinal(day, t.Str("effectiveUntil")) <= 0);
        var exception = classroom["overrides"]?[day];
        configured = template is not null || exception.Str("mode") is "closed" or "custom";
        currentCourse = -1; nextCourse = "";
        if (exception.Str("mode") == "closed") return new();
        if (exception.Str("mode") == "weekday") weekday = exception.Int("weekday", weekday);
        var periods = exception.Str("mode") == "custom" ? exception?["periods"] : template?["weekdays"]?[weekday.ToString()];
        var values = (periods as JsonArray ?? new()).ToList();
        string current = clock.ToString("HH:mm");
        currentCourse = values.FindIndex(p => string.CompareOrdinal(p.Str("start"), current) <= 0 && string.CompareOrdinal(current, p.Str("end")) < 0);
        var next = values.FirstOrDefault(p => string.CompareOrdinal(p.Str("start"), current) > 0);
        if (currentCourse < 0 && next is not null) nextCourse = $"下一节 {next.Str("subject")} {next.Str("start")}";
        return values.Select(p => p.Str("subject")).ToList();
    }
}
