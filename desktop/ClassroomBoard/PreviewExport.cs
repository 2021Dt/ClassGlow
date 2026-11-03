using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ClassroomBoard;

public static class PreviewExport
{
    // 使用正式渲染器生成设计对照图，不创建窗口、写壁纸或读取用户绑定凭证。
    public static int Run(string[] args)
    {
        string source = args.SkipWhile(a => a != "--snapshot").Skip(1).First();
        string output = args.SkipWhile(a => a != "--output").Skip(1).First();
        Directory.CreateDirectory(output);
        var snapshot = JsonNode.Parse(File.ReadAllText(source))!.AsObject();
        var clock = DateTimeOffset.Parse(snapshot.Str("previewClock"));
        var state = new ClientState {
            Snapshot = snapshot, EnableWallpaper = false,
            LastContact = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), LastContentSync = clock.ToUnixTimeSeconds()
        };
        var layout = snapshot["classroom"]!["layout"]!;
        // 临时帧与公开对照图分开，避免开发服务器监听到正在写入的中间文件。
        string frameDirectory = Path.Combine(Path.GetTempPath(), "ClassGlow-render-preview");
        Directory.CreateDirectory(frameDirectory);
        var renderer = new Renderer(frameDirectory);
        var results = new JsonArray();
        foreach (var (name, theme, material, width, height) in new[] {
            ("windows-light", "mica_light", "subtle", 1920, 1080),
            ("windows-dark", "mica_dark", "subtle", 1920, 1080),
            ("windows-solid", "mica_light", "solid", 1920, 1080),
            ("windows-dark-solid", "mica_dark", "solid", 1920, 1080),
            ("windows-small", "mica_light", "subtle", 1366, 768) }) {
            layout["theme"] = theme; layout["material"] = material;
            var frame = renderer.Render(state, clock, width, height);
            File.Copy(frame.Path, Path.Combine(output, name + ".png"), true);
            results.Add(new JsonObject { ["file"] = name + ".png", ["pages"] = frame.Count, ["material"] = frame.Material, ["reason"] = frame.Reason });
        }
        // 中性银灰背景仅用于对照透明层，正式班级的默认背景仍为纯净云母。
        var sample = new DrawingVisual();
        using (var dc = sample.RenderOpen()) {
            var baseBrush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#B8C0C6"), (Color)ColorConverter.ConvertFromString("#E9E8E4"), 30);
            dc.DrawRectangle(baseBrush, null, new Rect(0, 0, 1920, 1080));
            var ribbon = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#F4F3EE"), (Color)ColorConverter.ConvertFromString("#7B8792"), 75);
            dc.DrawGeometry(ribbon, null, Geometry.Parse("M-200,1000 C300,1050 680,300 1300,-180 L2000,-180 C1450,400 1100,1060 400,1300 Z"));
            var lower = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#ABB4BB"), (Color)ColorConverter.ConvertFromString("#ECEAE3"), 15);
            dc.DrawGeometry(lower, null, Geometry.Parse("M0,1130 C500,1130 1100,600 2000,500 L2000,1200 Z"));
        }
        var sampleBitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32); sampleBitmap.Render(sample);
        var jpeg = new JpegBitmapEncoder { QualityLevel = 88 }; jpeg.Frames.Add(BitmapFrame.Create(sampleBitmap));
        using var picture = new MemoryStream(); jpeg.Save(picture);
        File.WriteAllBytes(Path.Combine(output, "custom-background.jpg"), picture.ToArray());
        layout["backgroundMode"] = "image"; layout["backgroundImage"] = "data:image/jpeg;base64," + Convert.ToBase64String(picture.ToArray());
        foreach (var (name, theme, material) in new[] {
            ("light", "mica_light", "subtle"), ("dark", "mica_dark", "subtle"),
            ("solid", "mica_light", "solid"), ("dark-solid", "mica_dark", "solid") }) {
            layout["theme"] = theme; layout["material"] = material;
            var custom = renderer.Render(state, clock, 1920, 1080);
            File.Copy(custom.Path, Path.Combine(output, $"windows-custom-{name}.png"), true);
            results.Add(new JsonObject { ["file"] = $"windows-custom-{name}.png", ["pages"] = custom.Count, ["material"] = custom.Material });
        }
        layout["theme"] = "mica_light"; layout["material"] = "subtle";
        var originalGrid = layout["grid"]!.DeepClone();
        layout["grid"] = JsonNode.Parse("""{"timetable":{"x":0,"y":0,"w":5,"h":12},"todo":{"x":5,"y":0,"w":7,"h":4},"homework":{"x":5,"y":4,"w":7,"h":8}}""");
        var arranged = renderer.Render(state, clock, 1920, 1080);
        File.Copy(arranged.Path, Path.Combine(output, "windows-layout.png"), true);
        results.Add(new JsonObject { ["file"] = "windows-layout.png", ["pages"] = arranged.Count, ["material"] = arranged.Material });
        layout["grid"] = originalGrid;
        layout["backgroundMode"] = "mica"; layout["backgroundImage"] = "";
        // 边界样例：正文含大量换行，最后一行必须留在最后一页，而不是伸出卡片。
        snapshot["contents"]![0]!["body"] = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"第{i}行"));
        var first = renderer.Render(state, clock, 1920, 1080);
        state.PageSetId = first.PageSetId;
        var last = renderer.Render(state, clock, 1920, 1080, first.Count);
        File.Copy(first.Path, Path.Combine(output, "windows-overflow-first.png"), true);
        File.Copy(last.Path, Path.Combine(output, "windows-overflow-last.png"), true);
        File.WriteAllText(Path.Combine(output, "preview-result.json"), new JsonObject { ["frames"] = results, ["overflowPages"] = first.Count, ["wallpaperChanged"] = false }.ToJsonString());
        return 0;
    }
}
