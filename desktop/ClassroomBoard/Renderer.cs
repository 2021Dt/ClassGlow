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

namespace ClassroomBoard;

public sealed record Frame(string Path, string PageSetId, int Page, int Count, string Theme, string Material, string Reason);
public sealed record TextSlice(string Subject, List<string> Lines, bool Continued);

public static class JsonRead
{
    public static string Str(this JsonNode? node, string key, string fallback = "") => node?[key]?.GetValue<string>() ?? fallback;
    public static int Int(this JsonNode? node, string key, int fallback = 0) => node?[key]?.GetValue<int>() ?? fallback;
    public static bool Flag(this JsonNode? node, string key, bool fallback = false) => node?[key]?.GetValue<bool>() ?? fallback;
    public static double Num(this JsonNode? node, string key, double fallback = 0) => node?[key]?.GetValue<double>() ?? fallback;
}

public sealed class Renderer(string directory)
{
    private static readonly Typeface Font = new("Microsoft YaHei");
    private readonly Dictionary<string, DrawingGroup> backgrounds = new();
    private static Brush BrushOf(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    private static FormattedText Text(string text, double size, Brush brush) => new(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, Font, size, brush, 1);
    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush, double width = 1800)
    {
        var formatted = Text(text, size, brush); formatted.MaxTextWidth = Math.Max(1, width);
        dc.DrawText(formatted, new Point(x, y));
    }
    public static List<string> Wrap(string text, double size, double width)
    {
        var result = new List<string>();
        foreach (string paragraph in text.Split('\n')) {
            string line = "";
            foreach (var rune in paragraph.EnumerateRunes()) {
                string next = line + rune;
                if (line.Length > 0 && Text(next, size, Brushes.Black).WidthIncludingTrailingWhitespace > width) { result.Add(line); line = rune.ToString(); }
                else line = next;
            }
            result.Add(line);
        }
        return result;
    }
    public static List<List<TextSlice>> Paginate(IEnumerable<JsonNode> contents, double size, double width, double budget)
    {
        if (budget < 1 || width <= 0) throw new InvalidDataException("当前布局没有足够空间，请调整模块或字号。");
        var pages = new List<List<TextSlice>> { new() }; double used = 0;
        foreach (var item in contents) {
            var lines = Wrap(item.Str("body"), size, width); int offset = 0;
            while (offset < lines.Count) {
                double gap = pages[^1].Count > 0 ? .45 : 0;
                if (budget - used - gap < 1) { pages.Add(new()); used = 0; gap = 0; }
                int take = Math.Min(lines.Count - offset, (int)Math.Floor(budget - used - gap));
                pages[^1].Add(new(item.Str("subject"), lines.GetRange(offset, take), offset > 0));
                offset += take; used += take + gap;
                if (pages.Count > 10000) throw new InvalidDataException("内容分页过多，保留上个完整画面。");
            }
        }
        return pages;
    }
    public Frame Render(ClientState state, DateTimeOffset clock, int width, int height, int requestedPage = 0)
    {
        var snapshot = state.Snapshot ?? throw new InvalidDataException("没有可用快照。");
        var classroom = snapshot["classroom"]!; var layout = classroom["layout"]!; var modules = layout["modules"]!;
        bool highContrast = SystemParameters.HighContrast;
        string theme = highContrast ? "system_high_contrast" : layout.Str("theme", "mica_light");
        bool dark = theme == "mica_dark";
        bool reduced = highContrast || state.ReduceEffects || !SystemParameters.ClientAreaAnimation || !TransparencyEnabled();
        string material = reduced ? "solid" : layout.Str("material", "solid");
        string reason = highContrast ? "系统高对比" : state.ReduceEffects ? "本机减少效果" : reduced ? "系统减少效果或关闭透明" : material == "solid" ? "班级配置" : "";
        Brush background = highContrast ? SystemColors.WindowBrush : BrushOf(dark ? "#102A29" : "#E9EEF2");
        Brush surface = highContrast ? SystemColors.WindowBrush : BrushOf(dark ? "#183B35" : "#F8FAFC");
        Brush text = highContrast ? SystemColors.WindowTextBrush : BrushOf(dark ? "#F1F7F4" : "#152C2A");
        Brush muted = highContrast ? text : BrushOf(dark ? "#C1D2CB" : "#435954");
        Brush accent = highContrast ? text : BrushOf(dark ? "#92C6AF" : "#2B6654");
        double fontSize = layout.Str("font") switch { "small" => 32, "large" => 40, _ => 36 };
        double left = layout.Str("iconMargin") == "left" ? 264 : 48, right = layout.Str("iconMargin") == "right" ? 264 : 48;
        double total = 1920 - left - right;
        bool reverse = layout.Flag("reverse");
        Rect ModuleRect(string key, int x, int y, int w, int h) {
            var node = layout["grid"]?[key]; x = node.Int("x", x); y = node.Int("y", y); w = node.Int("w", w); h = node.Int("h", h);
            if (x < 0 || y < 0 || w < 5 || h < 4 || x + w > 12 || y + h > 12) throw new InvalidDataException("模块超出网格边界。");
            if (reverse) x = 12 - x - w;
            return new Rect(left + x * (total + 28) / 12, 224 + y * 688 / 12, w * (total + 28) / 12 - 28, h * 688 / 12 - 28);
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
            string cacheKey = $"{theme}:{material}:{left}:{right}:{layout.ToJsonString()}";
            if (!backgrounds.TryGetValue(cacheKey, out var layer)) {
                layer = new DrawingGroup(); using (var bg = layer.Open()) {
                    bg.DrawRectangle(background, null, new Rect(0, 0, 1920, 1080));
                    if (material == "subtle" && !highContrast) {
                        var glow = new RadialGradientBrush((Color)ColorConverter.ConvertFromString(dark ? "#28483E" : "#D8E4EA"), Colors.Transparent);
                        bg.DrawEllipse(glow, null, new Point(1500, 250), 800, 650);
                    }
                    void Panel(double x, double y, double w, double h) => bg.DrawRoundedRectangle(surface, new Pen(accent, .5), new Rect(x, y, w, h), 30, 30);
                    Panel(left, 48, total, 144); Panel(left, 916, total, 116);
                    if (modules.Flag("homework")) Panel(jobRect.X, jobRect.Y, jobRect.Width, jobRect.Height);
                    if (modules.Flag("todo")) Panel(todoRect.X, todoRect.Y, todoRect.Width, todoRect.Height);
                    if (modules.Flag("timetable")) Panel(tableRect.X, tableRect.Y, tableRect.Width, tableRect.Height);
                }
                layer.Freeze(); if (backgrounds.Count >= 6) backgrounds.Clear(); backgrounds[cacheKey] = layer;
            }
            dc.DrawDrawing(layer);
            DrawText(dc, classroom.Str("name"), left + 35, 66, 48, text, total - 650);
            if (modules.Flag("datetime", true) && !state.ClockUncertain) {
                DrawText(dc, clock.ToString("yyyy年M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN")), left + 35, 130, 28, muted);
                DrawText(dc, clock.ToString("HH:mm"), 1920 - right - 265, 60, 72, text);
            }
            if (!offlineExpired && modules.Flag("countdown") && DateTime.TryParseExact(classroom.Str("eventDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var target)) {
                int days = (target.Date - clock.Date).Days;
                if (days >= 0 && classroom.Str("event").Length > 0) { DrawText(dc, "距离" + classroom.Str("event"), 1920 - right - 650, 74, 24, muted, 350); DrawText(dc, days == 0 ? "就是今天" : $"还有 {days} 天", 1920 - right - 650, 115, 36, text); }
            }
            void Title(string title, double x, double y) { dc.DrawRoundedRectangle(accent, null, new Rect(x + 30, y + 35, 5, 38), 2, 2); DrawText(dc, title, x + 52, y + 24, 40, text); }
            if (modules.Flag("homework") && !offlineExpired) {
                Title("今日作业", jobRect.X, jobRect.Y); double y = jobRect.Y + 96;
                if (jobs[page - 1 < jobs.Count ? page - 1 : 0].Count == 0) DrawText(dc, "今天还没有发布作业", jobRect.X + 40, y + 20, 32, muted);
                if (page <= jobs.Count) foreach (var slice in jobs[page - 1]) {
                    DrawText(dc, slice.Subject + (slice.Continued ? " 续" : ""), jobRect.X + 32, y, fontSize, accent, 140);
                    foreach (var line in slice.Lines) { DrawText(dc, line, jobRect.X + 177, y, fontSize, text, jobRect.Width - 205); y += fontSize * 1.55; }
                    y += fontSize * 1.55 * .45;
                }
            }
            if (modules.Flag("todo") && !offlineExpired) {
                Title("今日提醒", todoRect.X, todoRect.Y); double y = todoRect.Y + 96;
                if (contents.All(c => c.Str("kind") != "todo")) DrawText(dc, "暂无提醒", todoRect.X + 40, y, 32, muted);
                if (page <= todos.Count) foreach (var slice in todos[page - 1]) {
                    DrawText(dc, "•", todoRect.X + 32, y, fontSize, accent);
                    foreach (var line in slice.Lines) { DrawText(dc, line, todoRect.X + 70, y, fontSize, text, todoRect.Width - 100); y += fontSize * 1.55; }
                    y += fontSize * 1.55 * .45;
                }
            }
            if (modules.Flag("timetable") && !offlineExpired) {
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
            }
            double syncY=986;
            if (modules.Flag("message") && !offlineExpired) {
                string message=string.Join(" ",classroom.Str("message").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
                var formatted=Text(message,32,text); formatted.MaxTextWidth=total-300; formatted.LineHeight=38.4;
                if(formatted.Height>78) throw new InvalidDataException("寄语超出两行，请缩短后再发布");
                dc.DrawText(formatted,new Point(left+32,924)); syncY=Math.Max(986,924+formatted.Height+3);
            }
            if (offlineExpired) DrawText(dc, state.ClockUncertain ? "系统时间异常，时效内容已暂停，请现场校时" : "设备离线超过 72 小时，等待重新连接", left + 40, 290, 36, text);
            string lastSync = state.LastContentSync > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)state.LastContentSync).ToOffset(TimeSpan.FromHours(8)).ToString("M月d日 HH:mm") : "尚未同步";
            bool offline = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - state.LastContact > 90;
            DrawText(dc, $"内容同步 {lastSync} · 画面生成 {clock:MM月dd日 HH:mm}" + (offline ? " · 离线" : ""), left + 32, syncY, 22, muted, total - 280);
            DrawText(dc, $"第 {page} / {count} 页", 1920 - right - 220, 958, 26, muted);
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
