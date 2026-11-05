using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClassroomBoard;

/// <summary>校验并缓存独立图片；完整还原布局后才允许替换加密快照。</summary>
public sealed class LayoutAssets
{
    private readonly Dictionary<string, string> cache = new();
    private static readonly HashSet<string> Fields = new() { "image", "backgroundImage", "panelImage" };
    public void Clear() => cache.Clear();
    public void Seed(JsonNode? value)
    {
        if (value is JsonObject obj) foreach (var pair in obj) {
            if (Fields.Contains(pair.Key) && pair.Value is JsonValue node && node.TryGetValue<string>(out var image) && image.StartsWith("data:image/", StringComparison.Ordinal)) {
                try {
                    int separator = image.IndexOf(',');
                    byte[] bytes = Convert.FromBase64String(image[(separator + 1)..]);
                    if (bytes.Length <= 512 * 1024) Remember(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), image);
                } catch (FormatException) { /* 损坏的历史图片不作为可信缓存。 */ }
            } else Seed(pair.Value);
        }
        else if (value is JsonArray array) foreach (var child in array) Seed(child);
    }
    private void Remember(string id, string image)
    {
        cache[id] = image;
        while (cache.Count > 64) cache.Remove(cache.Keys.First());
    }
    public async Task ResolveAsync(JsonNode? value, Func<string, Task<(byte[] Data, string Mime)>> fetch)
    {
        if (value is JsonObject obj) foreach (var pair in obj.ToList()) {
            if (Fields.Contains(pair.Key) && pair.Value is JsonValue node && node.TryGetValue<string>(out var reference) && reference.StartsWith("asset:", StringComparison.Ordinal)) {
                string id = reference[6..];
                if (!Regex.IsMatch(id, "\\A[0-9a-f]{64}\\z")) throw new InvalidDataException("图片引用无效。");
                if (!cache.TryGetValue(id, out var image)) {
                    var result = await fetch(id);
                    if (result.Data.Length > 512 * 1024 || result.Mime is not ("image/jpeg" or "image/png" or "image/webp") ||
                        Convert.ToHexString(SHA256.HashData(result.Data)).ToLowerInvariant() != id)
                        throw new InvalidDataException("图片校验未通过，保留上个完整快照。");
                    image = $"data:{result.Mime};base64," + Convert.ToBase64String(result.Data);
                    Remember(id, image);
                }
                obj[pair.Key] = image;
            } else await ResolveAsync(pair.Value, fetch);
        }
        else if (value is JsonArray array) foreach (var child in array) await ResolveAsync(child, fetch);
    }
}
