using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClassroomBoard;

public sealed class ClientState
{
    public string Server { get; set; } = "http://127.0.0.1:8000";
    public string Credential { get; set; } = "";
    public string PairId { get; set; } = "";
    public string PairCode { get; set; } = "";
    public string PairUrl { get; set; } = "";
    public string PairQr { get; set; } = "";
    public string AppliedWallpaper { get; set; } = "";
    public string BindingId { get; set; } = "";
    public string SyncEpoch { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string ETag { get; set; } = "";
    public JsonObject? Snapshot { get; set; }
    public int CachedVersion { get; set; }
    public int AppliedVersion { get; set; }
    public long StateReportSeq { get; set; }
    public long LocalControlEpoch { get; set; }
    public bool LocalPaused { get; set; }
    public bool RemotePaused { get; set; }
    public bool ReduceEffects { get; set; }
    public bool EnableWallpaper { get; set; }
    public int MonitorIndex { get; set; }
    public string OriginalWallpaper { get; set; } = "";
    public string OriginalMonitor { get; set; } = "";
    public bool OriginalCaptured { get; set; }
    public bool PendingRestore { get; set; }
    public string PageSetId { get; set; } = "";
    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;
    public double ManualUntil { get; set; }
    public double AutoPageStartedAt { get; set; }
    public double LastContact { get; set; }
    public double LastContentSync { get; set; }
    public double ClockOffset { get; set; }
    public bool ClockUncertain { get; set; }
    public double RenderedAt { get; set; }
    public string EffectiveTheme { get; set; } = "mica_light";
    public string EffectiveMaterial { get; set; } = "solid";
    public string MaterialReason { get; set; } = "";
    public string ApplyState { get; set; } = "waiting";
    public string Error { get; set; } = "";
    public Dictionary<string, JsonObject> Results { get; set; } = new();
    public Dictionary<string, int> GroupSeq { get; set; } = new();
}

public sealed class StateStore
{
    public string DirectoryPath { get; }
    private string StatePath => Path.Combine(DirectoryPath, "state.bin");
    public StateStore(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassroomBoard");
        Directory.CreateDirectory(DirectoryPath);
    }
    public ClientState Load()
    {
        if (!File.Exists(StatePath)) return new();
        // 凭证和班级缓存使用当前 Windows 用户的 DPAPI 加密，不能跨用户直接复制使用。
        byte[] plaintext = ProtectedData.Unprotect(File.ReadAllBytes(StatePath), null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<ClientState>(plaintext) ?? throw new InvalidDataException("本机状态损坏，请先备份 state.bin 再重新绑定。");
    }
    public void Save(ClientState state)
    {
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(state), null, DataProtectionScope.CurrentUser);
        var temp = StatePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
        // 写完并落盘后原子替换，失败时继续保留上个完整文件。
        File.Move(temp, StatePath, true);
    }
}
