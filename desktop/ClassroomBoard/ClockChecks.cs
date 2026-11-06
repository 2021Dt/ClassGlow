using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ClassroomBoard;

/// <summary>真实引擎与渲染器，隔离网络、时钟和壁纸出口；绝不改系统时间或壁纸。</summary>
public static class ClockChecks
{
    private sealed class TestTime : TimeProvider
    {
        private readonly DateTimeOffset origin = DateTimeOffset.Parse("2026-11-06T04:00:59.200Z");
        private readonly global::System.Diagnostics.Stopwatch elapsed = global::System.Diagnostics.Stopwatch.StartNew();
        private double advanced;
        public double WallShift;
        public bool RealTime;
        public double Seconds => advanced + (RealTime ? elapsed.Elapsed.TotalSeconds : 0);
        public DateTimeOffset ServerNow => origin.AddSeconds(Seconds);
        public override DateTimeOffset GetUtcNow() => ServerNow.AddSeconds(WallShift);
        public override long GetTimestamp() => (long)(Seconds * 1000);
        public override long TimestampFrequency => 1000;
        public void Advance(double seconds) => advanced += seconds;
    }
    private sealed class Desktop : IWallpaper
    {
        public bool Safe = true, Conflict = false, FailSet = false;
        public string Path = "";
        public int Sets;
        public (string Id, NativeRect Rect, string Original) Read(int index) => ("isolated", new() { Right = 640, Bottom = 360 }, Path);
        public bool SafeToApply() => Safe;
        public bool HasConflict(bool refresh = false) => Conflict;
        public void Set(string monitor, string path) { if (FailSet) throw new IOException("isolated-set-failure"); Path = path; Sets++; }
        public string Current(string monitor) => Path;
    }
    private sealed class Notices : IScreenNotice
    {
        public string ActiveId => "";
        public double EndsAt => 0;
        public void Show(JsonObject command, NativeRect monitor, DateTimeOffset clock) { }
        public void Dismiss() { }
        public void Dispose() { }
    }
    private sealed class Server(TestTime time) : HttpMessageHandler
    {
        public TaskCompletionSource? SnapshotGate, AssetGate, HeartbeatGate;
        public TaskCompletionSource SnapshotStarted = new(), AssetStarted = new();
        public int Requests, NotModified;
        public int Version = 1;
        public double ServerOffset;
        public bool WithAsset, Offline, ClockHeader = true;
        public string ETag = "\"clock-fixture\"";
        public static readonly byte[] Image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jM1kAAAAASUVORK5CYII=");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (Offline) throw new HttpRequestException("isolated-offline");
            var status = HttpStatusCode.OK;
            HttpContent body;
            if (path == "/device/snapshot") {
                Requests++; SnapshotStarted.TrySetResult();
                if (SnapshotGate is not null) await SnapshotGate.Task.WaitAsync(cancellation);
                if (request.Headers.IfNoneMatch.Any(tag => tag.ToString() == ETag)) { NotModified++; status = HttpStatusCode.NotModified; body = new StringContent(""); }
                else {
                    var snapshot = Snapshot(); snapshot["serverTime"] = time.ServerNow.ToUnixTimeMilliseconds() / 1000d + ServerOffset;
                    snapshot["classVersion"] = Version;
                    if (WithAsset) snapshot["classroom"]!["layout"]!["backgroundImage"] = "asset:" + Convert.ToHexString(SHA256.HashData(Image)).ToLowerInvariant();
                    body = new StringContent(snapshot.ToJsonString(), Encoding.UTF8, "application/json");
                }
            } else if (path.StartsWith("/device/assets/", StringComparison.Ordinal)) {
                AssetStarted.TrySetResult(); if (AssetGate is not null) await AssetGate.Task.WaitAsync(cancellation);
                body = new ByteArrayContent(Image); body.Headers.ContentType = new("image/png");
            } else {
                if (path == "/device/heartbeat" && HeartbeatGate is not null) await HeartbeatGate.Task.WaitAsync(cancellation);
                body = new StringContent("{}", Encoding.UTF8, "application/json");
            }
            var response = new HttpResponseMessage(status) { Content = body };
            response.Headers.ETag = new(ETag); response.Headers.Date = time.ServerNow.AddSeconds(ServerOffset);
            if (ClockHeader) response.Headers.TryAddWithoutValidation("X-ClassGlow-Time", (time.ServerNow.ToUnixTimeMilliseconds() / 1000d + ServerOffset).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return response;
        }
    }
    private static JsonObject Snapshot() => JsonNode.Parse("""
        {"schema":1,"deviceId":"test-device","bindingId":"test-binding","syncEpoch":"test-epoch","classVersion":1,
        "classroom":{"id":"test-class","name":"时钟验证（非真实数据）","layout":{"theme":"mica_light","material":"solid","font":"medium","iconMargin":"none","interval":20,"modules":{"homework":true,"todo":true,"timetable":true,"countdown":true,"message":true,"datetime":true}},"templates":[],"overrides":{},"message":"隔离检查"},"contents":[],"commands":[]}
        """)!.AsObject();
    private sealed class Fixture : IDisposable
    {
        public readonly TestTime Time = new();
        public readonly Desktop Desktop = new();
        public readonly Server Server;
        public readonly Engine Engine;
        public Fixture(string output, string name)
        {
            Server = new(Time);
            var state = new ClientState { Server = "http://127.0.0.1/", Credential = "isolated", BindingId = "test-binding", DeviceId = "test-device", ClassId = "test-class", SyncEpoch = "test-epoch", Snapshot = Snapshot(), CachedVersion = 1, EnableWallpaper = true, ReduceEffects = true, LastContact = Time.GetUtcNow().ToUnixTimeSeconds() };
            Engine = new(new StateStore(Path.Combine(output, name)), state, Desktop, new Notices(), Time, Server, listenForSignals: false);
        }
        public void Dispose() => Engine.Dispose();
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    private static async Task RunCases(string output, JsonArray results)
    {
        async Task Case(string name, Func<Task> test) {
            try { await test(); results.Add(new JsonObject { ["name"] = name, ["passed"] = true }); }
            catch (Exception error) { results.Add(new JsonObject { ["name"] = name, ["passed"] = false, ["detail"] = error.Message }); }
        }
        await Case("minute-boundary", async () => {
            using var f = new Fixture(output, "minute"); await f.Engine.Tick(); int before = f.Desktop.Sets;
            f.Time.Advance(1); await f.Engine.Tick();
            Require(f.Desktop.Sets > before, "分钟已变化，一秒后仍未更新画面");
        });
        await Case("new-content-does-not-wait-an-extra-tick", async () => {
            using var f = new Fixture(output, "new-content"); await f.Engine.Tick(); f.Server.Version = 2; f.Server.ETag = "\"second\"";
            f.Engine.RequestSync(); await f.Engine.Tick(); Require(f.Engine.State.AppliedVersion == 2, "新内容接收完成后额外等待下一轮 Tick");
        });
        await Case("slow-network-does-not-block-clock", async () => {
            using var f = new Fixture(output, "network"); await f.Engine.Tick(); int before = f.Desktop.Sets;
            f.Server.SnapshotGate = new(); f.Engine.RequestSync(); Task request = f.Engine.Tick();
            try {
                f.Time.Advance(61); await f.Engine.Tick();
                Require(f.Desktop.Sets > before && f.Engine.State.RenderedAt >= f.Time.ServerNow.ToUnixTimeSeconds() - 1, "在途网络请求挡住本地时钟");
            } finally { f.Server.SnapshotGate.TrySetResult(); await request; }
        });
        await Case("backward-system-clock", async () => {
            using var f = new Fixture(output, "backward"); await f.Engine.Tick(); int before = f.Desktop.Sets, requests = f.Server.Requests;
            f.Time.WallShift = -600; f.Time.Advance(61); await f.Engine.Tick();
            Require(f.Desktop.Sets > before && f.Server.Requests > requests, "系统时间回拨导致刷新与同步等待旧截止时间");
        });
        await Case("304-recalibrates-clock", async () => {
            using var f = new Fixture(output, "not-modified"); await f.Engine.Tick();
            f.Server.ServerOffset = 3; f.Time.Advance(11); await f.Engine.Tick();
            Require(f.Server.NotModified == 1 && Math.Abs(f.Engine.State.ClockOffset - 3) < .01, "304 响应没有持续校准时间");
        });
        await Case("asset-download-is-not-clock-offset", async () => {
            using var f = new Fixture(output, "asset"); f.Server.WithAsset = true; f.Server.AssetGate = new();
            Task request = f.Engine.Tick(); await f.Server.AssetStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            f.Time.Advance(6); f.Server.AssetGate.TrySetResult(); await request;
            Require(Math.Abs(f.Engine.State.ClockOffset) < 1, "图片下载耗时被记成了系统时差");
        });
        await Case("fullscreen-return-refreshes-immediately", async () => {
            using var f = new Fixture(output, "fullscreen"); await f.Engine.Tick();
            f.Desktop.Safe = false; f.Time.Advance(61); await f.Engine.Tick(); int before = f.Desktop.Sets;
            f.Desktop.Safe = true; f.Time.Advance(1); await f.Engine.Tick();
            Require(f.Desktop.Sets > before && f.Engine.State.ApplyState == "applied", "退出保护后继续显示旧分钟");
        });
        await Case("slow-heartbeat-does-not-block-clock", async () => {
            using var f = new Fixture(output, "heartbeat"); await f.Engine.Tick(); int before = f.Desktop.Sets;
            f.Server.HeartbeatGate = new(); f.Time.Advance(31); Task request = f.Engine.Tick();
            try { f.Time.Advance(61); await f.Engine.Tick(); Require(f.Desktop.Sets > before && f.Engine.State.RenderedAt >= f.Time.ServerNow.ToUnixTimeSeconds() - 1, "心跳等待挡住本地时钟"); }
            finally { f.Server.HeartbeatGate.TrySetResult(); await request; }
        });
        await Case("forward-system-clock", async () => {
            using var f = new Fixture(output, "forward"); await f.Engine.Tick();
            f.Time.WallShift = 3600; f.Time.Advance(61); await f.Engine.Tick();
            Require(Math.Abs(f.Engine.State.RenderedAt - f.Time.ServerNow.ToUnixTimeSeconds()) <= 1, "系统时间前拨让时钟跳到未来");
        });
        await Case("legacy-server-clock", async () => {
            using var f = new Fixture(output, "legacy"); f.Server.ClockHeader = false;
            await f.Engine.Tick(); f.Time.Advance(11); await f.Engine.Tick();
            Require(f.Server.NotModified == 1 && Math.Abs(f.Engine.State.ClockOffset) < 1, "兼容旧服务器的 Date 校时失败");
        });
        await Case("offline-keeps-ticking-without-clock-rollback", async () => {
            using var f = new Fixture(output, "offline"); await f.Engine.Tick(); f.Server.Offline = true;
            f.Time.WallShift = -3600;
            for (int i = 0; i < 10; i++) { f.Time.Advance(61); await f.Engine.Tick(); }
            Require(f.Engine.State.RenderedAt >= f.Time.ServerNow.ToUnixTimeSeconds() - 1, "断网后时钟没有继续走时");
            Require(f.Engine.State.SecondsSinceContact >= 610, "离线年龄被系统回拨重置");
            Require(Directory.GetFiles(Path.GetDirectoryName(f.Desktop.Path)!, "board-*.png").Length <= 10, "时钟刷新缓存无限增长");
        });
        await Case("offline-expiry-survives-system-clock-change", async () => {
            using var f = new Fixture(output, "offline-expiry"); await f.Engine.Tick(); f.Server.Offline = true;
            f.Time.Advance(73 * 3600); f.Time.WallShift = -72 * 3600; await f.Engine.Tick();
            Require(Renderer.ContactAge(f.Engine.State, f.Time.ServerNow) >= 73 * 3600, "时间回拨绕过离线时效保护");
        });
        await Case("same-minute-does-not-rewrite-wallpaper", async () => {
            using var f = new Fixture(output, "idle"); f.Time.Advance(5); await f.Engine.Tick(); await f.Engine.Tick(); int before = f.Desktop.Sets;
            for (int i = 0; i < 20; i++) { f.Time.Advance(1); await f.Engine.Tick(); }
            Require(f.Desktop.Sets == before, "每秒检查造成不必要的壁纸重写");
        });
        await Case("local-pause-and-resume", async () => {
            using var f = new Fixture(output, "pause"); await f.Engine.Tick();
            f.Engine.LocalPause(true); await f.Engine.Tick(); int paused = f.Desktop.Sets;
            f.Time.Advance(61); await f.Engine.Tick(); Require(f.Desktop.Sets == paused, "现场暂停后仍写画面");
            f.Engine.LocalPause(false); await f.Engine.Tick(); Require(f.Desktop.Sets > paused && f.Engine.State.ApplyState == "applied", "现场恢复未立即更新");
        });
        await Case("session-lock-and-unlock", async () => {
            using var f = new Fixture(output, "lock"); await f.Engine.Tick(); int before = f.Desktop.Sets;
            f.Engine.SessionChanged(true); f.Time.Advance(61); await f.Engine.Tick(); Require(f.Desktop.Sets == before, "锁屏期间写入壁纸");
            f.Engine.SessionChanged(false); await f.Engine.Tick(); Require(f.Desktop.Sets > before, "解锁后没有立即恢复");
        });
        await Case("resume-catches-up-without-replaying-minutes", async () => {
            using var f = new Fixture(output, "resume"); await f.Engine.Tick(); int before = f.Desktop.Sets;
            f.Time.Advance(8 * 3600); f.Engine.Resume(); await f.Engine.Tick();
            Require(f.Desktop.Sets == before + 1 && f.Engine.State.RenderedAt >= f.Time.ServerNow.ToUnixTimeSeconds() - 1, "唤醒后未直接应用当前时间");
        });
        await Case("wallpaper-conflict-recovery", async () => {
            using var f = new Fixture(output, "conflict"); await f.Engine.Tick(); int before = f.Desktop.Sets;
            f.Desktop.Conflict = true; f.Time.Advance(61); await f.Engine.Tick(); Require(f.Desktop.Sets == before, "冲突期间覆盖其他壁纸");
            f.Desktop.Conflict = false; await f.Engine.Tick(); Require(f.Desktop.Sets > before, "冲突结束后没有恢复");
        });
        await Case("wallpaper-failure-retries-with-current-time", async () => {
            using var f = new Fixture(output, "retry"); await f.Engine.Tick(); double before = f.Engine.State.RenderedAt;
            f.Desktop.FailSet = true; f.Time.Advance(61); await f.Engine.Tick(); Require(f.Engine.State.RenderedAt == before, "失败画面被误报为已显示");
            f.Desktop.FailSet = false; f.Time.Advance(5); await f.Engine.Tick(); Require(f.Engine.State.RenderedAt >= f.Time.ServerNow.ToUnixTimeSeconds() - 1, "失败后未重试最新分钟");
        });
        await Case("midnight-updates-date", async () => {
            using var f = new Fixture(output, "midnight"); f.Time.Advance(11 * 3600 + 59 * 60); await f.Engine.Tick(); string before = f.Desktop.Path;
            f.Time.Advance(1); await f.Engine.Tick();
            Require(f.Desktop.Path != before && Path.GetFileName(f.Desktop.Path).Contains("202611070000"), "跨日日期没有更新");
        });
        await Case("shutdown-cancels-pending-network", async () => {
            using var f = new Fixture(output, "shutdown"); await f.Engine.Tick(); f.Server.SnapshotGate = new(); f.Engine.RequestSync(); Task tick = f.Engine.Tick();
            await f.Engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(2)); await tick;
            int before = f.Desktop.Sets; f.Time.Advance(61); await f.Engine.Tick(); Require(f.Desktop.Sets == before, "退出后仍更新画面");
        });
        await Case("late-response-does-not-rebind", async () => {
            using var f = new Fixture(output, "late"); await f.Engine.Tick(); f.Server.SnapshotGate = new(); f.Engine.RequestSync(); Task tick = f.Engine.Tick();
            f.Engine.Unbind(); f.Server.SnapshotGate.TrySetResult(); await tick;
            Require(f.Engine.State.Snapshot is null && f.Engine.State.BindingId.Length == 0, "迟到的网络响应复活了绑定");
        });
        await Case("material-creation-cancels-and-releases-late-controller", async () => {
            var pending = new TaskCompletionSource<object>(); using var stop = new CancellationTokenSource(); int released = 0;
            Task<object> wait = GlassMaterial.WaitForCreation(pending.Task, stop.Token, _ => released++);
            stop.Cancel(); try { await wait; throw new InvalidOperationException("创建等待未中止"); } catch (OperationCanceledException) { }
            pending.SetResult(new object()); await Task.Yield(); Require(released == 1, "迟到控制器没有关闭");
        });
        await Case("multi-monitor-fullscreen-boundary", () => {
            var target = new NativeRect { Left = -1920, Top = 0, Right = 0, Bottom = 1080 };
            var other = new NativeRect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            Require(!Wallpaper.CoversMonitor(other, target) && Wallpaper.CoversMonitor(target, target), "副屏全屏判断影响了目标桌面");
            other.Left = -8; other.Right += 8; other.Top -= 8; other.Bottom += 8;
            Require(!Wallpaper.CoversMonitor(other, target), "另一屏窗口的阴影边界阻止目标屏刷新");
            return Task.CompletedTask;
        });
    }
    private static async Task RunRealTime(string output, JsonArray results)
    {
        using var f = new Fixture(output, "real-time"); f.Time.RealTime = true; await f.Engine.Tick();
        f.Server.SnapshotGate = new(); f.Engine.RequestSync(); Task inFlight = f.Engine.Tick();
        async Task Release() { await Task.Delay(6500); f.Server.SnapshotGate.TrySetResult(); }
        Task release = Release();
        var delays = new JsonArray(); bool progressedWhileWaiting = false;
        long lastMinute = (long)f.Engine.State.RenderedAt / 60;
        try {
            while (f.Time.Seconds < 65) {
                await Task.Delay(1000); await f.Engine.Tick();
                long minute = (long)f.Engine.State.RenderedAt / 60;
                if (minute != lastMinute) {
                    double late = f.Time.ServerNow.ToUnixTimeMilliseconds() / 1000d - minute * 60;
                    delays.Add(Math.Round(late, 3)); lastMinute = minute;
                    if (!inFlight.IsCompleted) progressedWhileWaiting = true;
                    Require(late < 2.5, "真实一秒 Tick 跨分钟刷新超出 2.5 秒");
                }
            }
            Require(progressedWhileWaiting && delays.Count >= 2, "真实慢请求期间未跨过两次分钟边界");
            results.Add(new JsonObject { ["name"] = "real-time-65-seconds", ["passed"] = true, ["minuteRefreshDelaySeconds"] = delays, ["progressedWhileNetworkWaiting"] = true });
        } finally { f.Server.SnapshotGate.TrySetResult(); await release; await inFlight; await f.Engine.StopAsync(); }
    }
    public static int Run(string[] args)
    {
        string output = Path.GetFullPath(args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "ClassGlow-clock-check"));
        Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        app.Dispatcher.InvokeAsync(async () => {
            var cases = new JsonArray();
            try { if (args.Contains("--real-time")) await RunRealTime(output, cases); else await RunCases(output, cases); }
            catch (Exception error) { cases.Add(new JsonObject { ["name"] = "harness", ["passed"] = false, ["detail"] = error.ToString() }); }
            bool passed = cases.All(c => c.Flag("passed"));
            File.WriteAllText(Path.Combine(output, "result.json"), new JsonObject { ["passed"] = passed, ["cases"] = cases, ["physicalWallpaperModified"] = false, ["systemClockModified"] = false }.ToJsonString(new() { WriteIndented = true }));
            app.Shutdown(passed ? 0 : 1);
        });
        return app.Run();
    }
}
