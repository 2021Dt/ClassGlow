using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClassroomBoard;

public static class Diagnostics
{
    // 严格使用白名单：不导出凭证、业务快照、图片、班名或用户文件路径。
    public static JsonObject Create(ClientState state) => new() {
        ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O"), ["version"] = AppPaths.Version,
        ["windows"] = Environment.OSVersion.Version.ToString(), ["runtime"] = Environment.Version.ToString(),
        ["bound"] = state.BindingId.Length > 0, ["enabled"] = state.EnableWallpaper,
        ["cachedVersion"] = state.CachedVersion, ["appliedVersion"] = state.AppliedVersion,
        ["applyState"] = state.ApplyState, ["localPaused"] = state.LocalPaused, ["remotePaused"] = state.RemotePaused,
        ["lastContact"] = state.LastContact, ["lastContentSync"] = state.LastContentSync,
        ["clockOffset"] = state.ClockOffset, ["clockUncertain"] = state.ClockUncertain,
        ["reduceEffects"] = state.ReduceEffects, ["monitorIndex"] = state.MonitorIndex,
        ["page"] = state.Page, ["pageCount"] = state.PageCount,
        ["effectiveTheme"] = state.EffectiveTheme, ["effectiveMaterial"] = state.EffectiveMaterial,
    };
    public static void Write(ClientState state, string file) => File.WriteAllText(file, Create(state).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    public static void Failure(string phase, Exception error) => AppPaths.QuietLog("runtime.log", $"phase={phase};type={error.GetType().Name};code=0x{error.HResult:X8}");
}
