using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ClassroomBoard;

public static class ReliabilityChecks
{
    public static int Run(string[] args)
    {
        string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "ClassGlow-reliability");
        Directory.CreateDirectory(output);
        try {
            byte[] bytes = Encoding.UTF8.GetBytes("isolated-test-image");
            string id = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var assets = new LayoutAssets(); int fetches = 0;
            Task<(byte[], string)> Fetch(string _) { fetches++; return Task.FromResult((bytes, "image/jpeg")); }
            var layout = new JsonObject { ["backgroundImage"] = "asset:" + id, ["canvas"] = new JsonArray(new JsonObject { ["image"] = "asset:" + id }) };
            assets.ResolveAsync(layout, Fetch).GetAwaiter().GetResult();
            if (fetches != 1 || !layout.Str("backgroundImage").StartsWith("data:image/jpeg;base64,")) throw new InvalidDataException("图片去重失败。");
            var afterRestart = new LayoutAssets(); afterRestart.Seed(layout);
            var cached = new JsonObject { ["backgroundImage"] = "asset:" + id };
            afterRestart.ResolveAsync(cached, _ => throw new InvalidOperationException("重启后缓存未复用")).GetAwaiter().GetResult();
            var invalid = new JsonObject { ["image"] = "asset:../private" };
            try { assets.ResolveAsync(invalid, Fetch).GetAwaiter().GetResult(); throw new InvalidOperationException("无效引用未拒绝"); }
            catch (InvalidDataException) { }
            var corrupt = new JsonObject { ["image"] = "asset:" + new string('a', 64) };
            try { assets.ResolveAsync(corrupt, Fetch).GetAwaiter().GetResult(); throw new InvalidOperationException("损坏图片未拒绝"); }
            catch (InvalidDataException) { }
            var state = new ClientState { Credential = "secret-credential", Server = "https://user:private-password@example.com/api/", BindingId = "private-binding", Snapshot = new JsonObject { ["body"] = "private-teaching-body" } };
            string diagnostic = Diagnostics.Create(state).ToJsonString();
            if (new[] { "secret-credential", "private-password", "private-binding", "private-teaching-body" }.Any(diagnostic.Contains)) throw new InvalidDataException("诊断泄露业务数据。");
            File.WriteAllText(Path.Combine(output, "result.json"), new JsonObject { ["passed"] = true, ["cases"] = 5, ["wallpaperChanged"] = false }.ToJsonString());
            return 0;
        } catch (Exception error) {
            File.WriteAllText(Path.Combine(output, "result.json"), new JsonObject { ["passed"] = false, ["errorType"] = error.GetType().Name }.ToJsonString());
            return 1;
        }
    }
}
