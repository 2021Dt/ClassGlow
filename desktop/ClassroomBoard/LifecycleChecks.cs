using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClassroomBoard;

public static class LifecycleChecks
{
    private sealed class DesktopDouble(string current) : IWallpaper
    {
        public string Path = current;
        public bool Fail;
        public string Current(string monitor) => Path;
        public bool SafeToApply() => true;
        public (string Id, NativeRect Rect, string Original) Read(int index) => ("test-monitor", new() { Right = 1920, Bottom = 1080 }, Path);
        public void Set(string monitor, string path) { if (Fail) throw new COMException("用于验证失败保留数据"); Path = path; }
    }

    public static int Run(string[] args)
    {
        string? output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault();
        if (output is null) return 2;
        string root = System.IO.Path.Combine(System.IO.Path.GetFullPath(output), "lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        try {
            string data = System.IO.Path.Combine(root, "data");
            var store = new StateStore(data); store.Load();
            Check(!Directory.Exists(data), "只读启动创建了数据目录");
            Directory.CreateDirectory(data);
            string original = System.IO.Path.Combine(root, "original.png"); File.WriteAllText(original, "test-only");
            string frame = System.IO.Path.Combine(data, "board-0123456789ab-202611031300-1-0123456789abcdef0123456789abcdef.png"); File.WriteAllText(frame, "test-only");
            var desktop = new DesktopDouble(frame);
            var state = new ClientState { OriginalCaptured = true, OriginalMonitor = "test-monitor", OriginalWallpaper = original };
            using var engine = new Engine(store, state, desktop);
            engine.RestoreWallpaper(); Check(desktop.Path == original, "没有恢复原壁纸");
            desktop.Path = "teacher-selected.jpg";
            engine.RestoreWallpaper(); Check(desktop.Path == "teacher-selected.jpg", "覆盖了用户后来选择的壁纸");
            desktop.Path = frame; desktop.Fail = true;
            try { engine.RestoreWallpaper(); throw new InvalidOperationException("没有传递壁纸恢复失败"); }
            catch (COMException) { Check(File.Exists(frame), "恢复失败时删了恢复材料"); }
            desktop.Fail = false; File.Delete(original);
            engine.RestoreWallpaper(); Check(desktop.Path == "", "原图丢失时仍依赖本产品图片");
            foreach (string name in new[] { "state.bin", "state.bin.tmp", "privacy.json", "runtime.log", "runtime.log.old" }) File.WriteAllText(System.IO.Path.Combine(data, name), "test-only");
            string personal = System.IO.Path.Combine(data, "teacher-notes.txt"); File.WriteAllText(personal, "keep");
            Check(ClientLifecycle.CleanupData(data, false), "未报告保留的其他文件");
            Check(Directory.GetFiles(data).Length == 1 && File.ReadAllText(personal) == "keep", "未正确区分程序文件与其他文件");
            File.Delete(personal); Check(!ClientLifecycle.CleanupData(data, false), "空数据目录未移除");
            var expiry = DateTimeOffset.UtcNow;
            state.Snapshot = new JsonObject { ["classroom"] = new JsonObject { ["premiumUntil"] = expiry.ToUnixTimeSeconds(), ["layout"] = new JsonObject { ["backgroundMode"] = "image" }, ["basicLayout"] = new JsonObject { ["backgroundMode"] = "mica" } } };
            // 走实际快照的 JSON 反序列化边界，避免测试对象中的 CLR 数值类型影响读取。
            state.Snapshot = JsonNode.Parse(state.Snapshot.ToJsonString())!.AsObject();
            state.ClockUncertain = false;
            Check(Renderer.EffectiveLayout(state, expiry.AddSeconds(-1)).Str("backgroundMode") == "image", "有效期内丢失定制布局");
            Check(Renderer.EffectiveLayout(state, expiry).Str("backgroundMode") == "mica", "离线到期没有恢复默认布局");
            Check(state.Snapshot["classroom"]!["layout"].Str("backgroundMode") == "image", "到期删除了定制方案");
            state.ClockUncertain = true;
            Check(Renderer.EffectiveLayout(state, expiry.AddSeconds(-1)).Str("backgroundMode") == "mica", "时钟异常时仍使用限时高级布局");
            File.WriteAllText(System.IO.Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { passed = true, checks = 10, physicalWallpaperChanged = false, registryChanged = false }));
            return 0;
        } catch (Exception e) {
            File.WriteAllText(System.IO.Path.Combine(root, "failure.txt"), e.ToString()); return 1;
        }
    }
}
