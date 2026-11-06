using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClassroomBoard;

public sealed class DeviceAuthorizationException(string message) : Exception(message);

public sealed class Engine : IDisposable
{
    public ClientState State { get; }
    public bool HasWallpaperConflict(bool refresh = false) => wallpaper.HasConflict(refresh);
    public string Status => State.EnableWallpaper && !State.LocalPaused && !State.RemotePaused && HasWallpaperConflict() ? "请先退出 Wallpaper Engine" : State.Error.Length > 0 ? State.Error : State.ApplyState switch { "applied" => "看板运行中", "paused" => "看板已暂停", "deferred" => sessionLocked ? "等待解锁后更新" : "等待全屏课件结束或桌面恢复后更新", _ => "等待同步或启用展示" };
    private const string ConflictMessage = "其他壁纸软件正在运行，请在教室电脑退出后恢复展示";
    private readonly StateStore store;
    private readonly Renderer renderer;
    private readonly GlassMaterial glass;
    private readonly LayoutAssets assets = new();
    private readonly IWallpaper wallpaper;
    private readonly IScreenNotice screenNotice;
    private string reportedNotice = "";
    private DeviceSignals? signals;
    // 教室客户端直连所配置的服务，不继承个人代理软件遗留的本机端口；不修改系统代理设置。
    private readonly HttpClient http;
    private readonly DisplayClock clock;
    private readonly bool listenForSignals;
    private double nextPoll, nextHeartbeat, nextRender;
    private long syncRequestSeq;
    private double nextCleanup;
    private int failures;
    private bool busy;
    private bool displayBusy, sessionLocked;
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private double contactAge, contactAt;
    private bool stopping;
    private int pendingRequests;
    private readonly CancellationTokenSource shutdown = new();
    private bool changed = true;
    private bool requireFullSnapshot = true;
    private string pendingDisplayRequest = "";
    private double nextDisplayUpload;
    private double Utc => clock.UtcNow.ToUnixTimeMilliseconds() / 1000d;
    private double Elapsed => clock.Elapsed;
    private DateTimeOffset Clock => clock.Now;

    public Engine(StateStore stateStore, ClientState? initial = null, IWallpaper? desktop = null, IScreenNotice? notices = null,
        TimeProvider? timeProvider = null, HttpMessageHandler? transport = null, bool listenForSignals = true)
    {
        store = stateStore; this.listenForSignals = listenForSignals;
        http = new(transport ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(8) };
        wallpaper = desktop ?? new Wallpaper(); screenNotice = notices ?? new ScreenNotice(); State = initial ?? store.Load();
        clock = new(timeProvider ?? TimeProvider.System, State.ClockOffset);
        contactAge = State.LastContact > 0 ? Math.Max(0, Utc - State.LastContact) : double.PositiveInfinity;
        State.ClockUncertain = IsTimeUncertain(Utc, State.LastContact); renderer = new(store.DirectoryPath); glass = new(store.DirectoryPath);
        assets.Seed(State.Snapshot?["classroom"]?["layout"]);
    }
    public static bool IsTimeUncertain(double current, double lastContact) => current<1577836800 || current>4102444800 || lastContact>0 && current<lastContact-120;
    public void Save() { if (!stopping) store.Save(State); }
    public async Task StopAsync()
    {
        stopping = true; shutdown.Cancel();
        screenNotice.Dismiss();
        if (signals is not null) { signals.Dispose(); await signals.Completion; signals = null; }
        // 卸载先取消并等待在途请求，避免清理后异步续写 state.bin。
        while (busy || displayBusy || pendingRequests > 0) await Task.Delay(25);
        try { GlassMaterial.CleanupProfile(store.DirectoryPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* 运行时仍占用文件时，由下次启动生成或卸载清理。 */ }
    }
    public static Uri ValidateServer(string value)
    {
        if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || (!uri.IsLoopback && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo) || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidOperationException("服务地址须为 HTTPS；仅本机开发允许 HTTP，不能包含账号、查询或片段。");
        return uri;
    }
    private sealed record DeviceResponse(JsonObject? Body, string ETag, double? ServerTime, double ReceivedAt);
    private async Task<DeviceResponse> Request(string path, string method = "GET", JsonObject? body = null, string? etag = null, bool authenticated = true)
    {
        shutdown.Token.ThrowIfCancellationRequested();
        pendingRequests++;
        try {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(ValidateServer(State.Server), path.TrimStart('/')));
        request.Headers.Add("X-ClassGlow-Client-Version", AppPaths.Version);
        request.Headers.Add("X-ClassGlow-Assets", Protocol.AssetVersion);
        if (authenticated && State.Credential.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", State.Credential);
        if (etag is not null && etag.Length > 0) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        // ResponseHeadersRead 的 HttpClient.Timeout 只覆盖响应头；正文读取必须共用截止令牌，防止慢速响应卡死轮询。
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        double receivedAt = Elapsed;
        double? serverTime = response.Headers.TryGetValues("X-ClassGlow-Time", out var values) &&
            double.TryParse(values.FirstOrDefault(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var stamp)
            ? stamp : null;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new DeviceAuthorizationException("设备授权已失效，请重新绑定。");
        if (response.StatusCode == HttpStatusCode.NotModified)
            return new(null, response.Headers.ETag?.ToString() ?? State.ETag, serverTime ?? response.Headers.Date?.ToUnixTimeSeconds(), receivedAt);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"服务返回 {(int)response.StatusCode}，保留现有缓存与输入。");
        if (response.Content.Headers.ContentLength > 8 * 1024 * 1024) throw new InvalidDataException("响应超过允许大小。");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token); using var buffer = new MemoryStream();
        var chunk = new byte[16384]; int count;
        while ((count = await stream.ReadAsync(chunk.AsMemory(), deadline.Token)) > 0) { if (buffer.Length + count > 8 * 1024 * 1024) throw new InvalidDataException("响应超过允许大小。"); buffer.Write(chunk, 0, count); }
        shutdown.Token.ThrowIfCancellationRequested();
        var parsed = JsonNode.Parse(buffer.ToArray()) as JsonObject ?? throw new InvalidDataException("服务响应结构不合法。");
        return new(parsed, response.Headers.ETag?.ToString() ?? "", serverTime ?? (parsed.ContainsKey("serverTime") ? parsed.Num("serverTime") : response.Headers.Date?.ToUnixTimeSeconds()), receivedAt);
        } finally { pendingRequests--; }
    }
    private async Task<(byte[] Data, string Mime)> RequestAsset(string id)
    {
        pendingRequests++;
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ValidateServer(State.Server), $"device/assets/{id}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", State.Credential);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new DeviceAuthorizationException("设备授权已失效，请重新绑定。");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 512 * 1024) throw new InvalidDataException("图片超过允许大小。");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384]; int count;
            while ((count = await stream.ReadAsync(chunk.AsMemory(), deadline.Token)) > 0) {
                if (buffer.Length + count > 512 * 1024) throw new InvalidDataException("图片超过允许大小。");
                buffer.Write(chunk, 0, count);
            }
            return (buffer.ToArray(), response.Content.Headers.ContentType?.MediaType ?? "");
        } finally { pendingRequests--; }
    }
    public async Task RevokeForUninstall() => await Request("/device/uninstall", "POST");
    public async Task Pair(string server)
    {
        ValidateServer(server);
        if (State.BindingId.Length > 0) throw new InvalidOperationException("已绑定设备不能直接改绑。请先撤销并在本机解绑。");
        State.Server = server.TrimEnd('/');
        var result = (await Request("/device-pairings", "POST", new() { ["name"] = Environment.MachineName }, authenticated: false)).Body!;
        State.PairId = result.Str("id"); State.PairCode = result.Str("code"); State.PairUrl = result.Str("pairingUrl"); State.PairQr = result.Str("qrPngBase64"); State.Credential = result.Str("secret");
        State.SetupCompleted = false;
        State.Error = "等待手机登录、核对并确认绑定"; Save(); nextPoll = 0;
    }
    public void RequestSync() { if (stopping) return; syncRequestSeq++; nextPoll = nextHeartbeat = nextRender = 0; changed = true; requireFullSnapshot=true; }
    public void Resume()
    {
        if (stopping) return;
        signals?.Dispose(); signals = null; RequestSync();
    }
    public void SessionChanged(bool locked)
    {
        sessionLocked = locked;
        if (locked) screenNotice.Dismiss();
        else Resume();
    }
    public async Task<string> CheckVersion()
    {
        var result = (await Request("/client/version", authenticated: false)).Body!;
        return Version.TryParse(result.Str("latestVersion"), out var latest) && Version.Parse(AppPaths.Version) >= latest ? $"当前已是 {AppPaths.Version}。升级由管理员安排，不自动替换或重启程序。" : $"当前 {AppPaths.Version}，可用 {result.Str("latestVersion")}。请管理员人工升级并保留本机数据目录。";
    }
    public void LocalPause(bool paused)
    {
        if (paused) screenNotice.Dismiss();
        State.LocalControlEpoch++; State.LocalPaused = paused;
        State.ManualUntil=0; State.AutoPageStartedAt=Clock.ToUnixTimeSeconds();
        if (!paused) State.RemotePaused = false;
        // 先持久化现场意图，再触碰壁纸；即使崩溃，旧远程命令也不能跨世代执行。
        Save(); changed = true; nextHeartbeat = 0;
    }
    public async Task Tick()
    {
        if (stopping) return;
        if (clock.ObserveSystemChange()) { RequestSync(); Diagnostics.Event("clock-change", store.DirectoryPath); }
        State.ClockOffset = clock.Offset;
        State.SecondsSinceContact = contactAge + Elapsed - contactAt;
        // 两条异步链仍在同一 UI 线程修改状态，但互不占用对方的等待标志。
        // 在途同步期间，下一次 Dispatcher Tick 仍能更新本地时钟和翻页。
        var before = State.Snapshot;
        await Task.WhenAll(DisplayTick(), NetworkTick());
        // 新快照完成后直接展示，不为拆分网络与时钟额外增加一轮轮询延迟。
        if (!stopping && !ReferenceEquals(before, State.Snapshot)) await DisplayTick();
    }
    private async Task DisplayTick()
    {
        if (displayBusy || stopping) return;
        displayBusy = true;
        try {
            State.LastDisplayTick = Utc;
            if (!State.EnableWallpaper || State.LocalPaused || State.RemotePaused || sessionLocked) screenNotice.Dismiss();
            if (reportedNotice != screenNotice.ActiveId) { reportedNotice = screenNotice.ActiveId; nextHeartbeat = 0; }
            if (State.PendingRestore && State.EnableWallpaper && SafeToApply() && renderGate.CurrentCount > 0) {
                try { RestoreWallpaper(); State.PendingRestore = false; Save(); }
                catch (Exception e) when (e is IOException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { State.Error = "原壁纸恢复失败，等待人工检查桌面策略"; }
            }
            if (Elapsed >= nextCleanup) { PruneFrames(); nextCleanup = Elapsed + 60; }
            if (State.Snapshot is not null && (changed || Elapsed >= nextRender)) {
                try { await ApplyCurrent(); nextRender = 0; }
                catch (Exception e) when (e is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException) {
                    Diagnostics.Failure("render", e, store.DirectoryPath);
                    State.ApplyState = "error"; State.DisplayBlockReason = "render-error";
                    State.Error = "生成或设置画面失败，保留上个已应用版本"; nextRender = Elapsed + 5; changed = false; Save();
                }
            }
        } catch (OperationCanceledException) when (stopping) { }
        finally { displayBusy = false; }
    }
    private bool SafeToApply() => !sessionLocked && wallpaper.SafeToApply(State.MonitorIndex);
    private async Task NetworkTick()
    {
        if (busy || stopping) return;
        busy = true;
        try {
            if (listenForSignals && signals is null && State.BindingId.Length > 0 && State.Credential.Length > 0) {
                string binding = State.BindingId;
                signals = new DeviceSignals(new Uri(ValidateServer(State.Server), "device/events"), State.Credential, () => {
                    if (!stopping && State.BindingId == binding) { syncRequestSeq++; nextPoll = 0; }
                });
            }
            if (Elapsed >= nextPoll && State.Credential.Length > 0) {
                long startedRequest = syncRequestSeq;
                try {
                    if (State.BindingId.Length == 0) {
                        var result = (await Request($"/device-pairings/{State.PairId}")).Body!;
                        if (result.Str("state") == "bound") {
                            State.DeviceId = result.Str("deviceId"); State.ClassId = result.Str("classId"); State.BindingId = result.Str("bindingId"); State.SyncEpoch = result.Str("syncEpoch");
                            State.Snapshot = null; State.CachedVersion = State.AppliedVersion = 0; State.PageSetId = "";
                            State.Results.Clear(); State.GroupSeq.Clear(); State.LocalPaused = State.RemotePaused = false; State.ETag = ""; State.Error = ""; Save();
                        }
                    }
                    if (State.BindingId.Length > 0) await PollSnapshot();
                    // 拉取期间也可能发布新内容，不能用下一次轮询时间覆盖刚收到的唤醒通知。
                    failures = 0; nextPoll = syncRequestSeq == startedRequest ? Elapsed + (State.BindingId.Length > 0 ? 10 : 2) : 0;
                } catch (DeviceAuthorizationException) { Unbind(); }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException) {
                    Diagnostics.Failure("sync", e, store.DirectoryPath);
                    failures++; nextPoll = Elapsed + Math.Min(20, 5 * Math.Pow(2, Math.Min(failures - 1, 2))); State.Error = "同步未完成，保留最近完整缓存"; Save();
                }
            }
            if (stopping) return;
            if (State.BindingId.Length > 0 && Elapsed >= nextHeartbeat) {
                try { await Request("/device/heartbeat", "POST", Report()); nextHeartbeat = Elapsed + 30; }
                catch (DeviceAuthorizationException) { Unbind(); }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { Diagnostics.Failure("heartbeat", e, store.DirectoryPath); nextHeartbeat = Elapsed + 10; }
            }
            if (State.BindingId.Length > 0 && pendingDisplayRequest.Length > 0 && Elapsed >= nextDisplayUpload) {
                try { await UploadDisplay(); pendingDisplayRequest = ""; }
                catch (DeviceAuthorizationException) { Unbind(); }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or InvalidOperationException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) {
                    // 回传失败不影响本机展示；请求保留并限频重试，网页会明确显示等待或超时。
                    nextDisplayUpload = Elapsed + 10;
                }
            }
        } catch (OperationCanceledException) when (stopping) {
            // 退出时取消材质准备属于正常收尾，不触发启动故障对话框。
        } finally { busy = false; }
    }
    private async Task PollSnapshot()
    {
        string binding = State.BindingId, credential = State.Credential;
        var response = await Request("/device/snapshot", etag: requireFullSnapshot || State.ClockUncertain ? "" : State.ETag);
        if (stopping || binding != State.BindingId || credential != State.Credential) return;
        var snapshot = response.Body; string etag = response.ETag;
        void Calibrate() {
            if (response.ServerTime is double timestamp) {
                clock.Synchronize(timestamp, response.ReceivedAt); State.ClockOffset = clock.Offset;
                State.ClockUncertain = false; State.LastClockSync = Clock.ToUnixTimeMilliseconds() / 1000d;
            }
            State.LastContact = Utc; contactAge = 0; contactAt = Elapsed; State.SecondsSinceContact = 0;
        }
        if (snapshot is null) { Calibrate(); State.LastContentSync = Clock.ToUnixTimeMilliseconds() / 1000d; return; }
        if (snapshot.Int("schema") != Protocol.Schema || snapshot.Str("bindingId") != State.BindingId || snapshot.Str("syncEpoch") != State.SyncEpoch || snapshot["classroom"] is not JsonObject || snapshot["contents"] is not JsonArray)
            throw new InvalidDataException("快照结构、绑定或同步世代不匹配。");
        if (snapshot.Str("deviceId") != State.DeviceId || snapshot["classroom"].Str("id") != State.ClassId ||
            (snapshot["contents"] as JsonArray)!.Any(c => c.Str("classId") != State.ClassId || c.Str("state") != "published" || c.Str("body").EnumerateRunes().Count() > 500 ||
                c.Str("kind") is not ("homework" or "todo") || !DateTimeOffset.TryParse(c.Str("from"), out var start) || !DateTimeOffset.TryParse(c.Str("until"), out var end) || start >= end))
            throw new InvalidDataException("快照包含其他班级或非正式内容，已拒绝缓存。");
        var incomingLayout = snapshot["classroom"]!["layout"];
        if (incomingLayout is not JsonObject || incomingLayout.Str("theme") is not ("mica_light" or "mica_dark") || incomingLayout.Str("material") is not ("subtle" or "solid"))
            throw new InvalidDataException("布局结构不兼容，保留上个完整快照。");
        if (incomingLayout.Str("font") is not ("small" or "medium" or "large") || incomingLayout.Str("iconMargin") is not ("left" or "right" or "none") || incomingLayout.Int("interval") is not (15 or 20 or 30) || incomingLayout["modules"] is not JsonObject moduleConfig ||
            new[] { "homework", "todo", "timetable", "countdown", "message" }.Any(key => moduleConfig[key] is not JsonValue value || !value.TryGetValue<bool>(out _)))
            throw new InvalidDataException("模块或字号不兼容，保留上个完整快照。");
        int version = snapshot.Int("classVersion");
        if (version < State.CachedVersion) return;
        Calibrate();
        await assets.ResolveAsync(incomingLayout, RequestAsset);
        if (stopping || binding != State.BindingId || credential != State.Credential) return;
        // 保存完整快照后才推进缓存版本；应用版本在壁纸成功设置后单独推进。
        bool newer = State.Snapshot is null || version > State.CachedVersion;
        var previousSnapshot = State.Snapshot; int previousVersion = State.CachedVersion; string previousEtag = State.ETag; double previousSync = State.LastContentSync;
        State.Snapshot = snapshot.DeepClone().AsObject(); State.CachedVersion = version; State.ETag = etag; State.LastContentSync = Utc+State.ClockOffset;
        try { Save(); }
        catch { State.Snapshot = previousSnapshot; State.CachedVersion = previousVersion; State.ETag = previousEtag; State.LastContentSync = previousSync; throw; }
        changed |= newer || previousEtag != etag;
        requireFullSnapshot=false;
        State.DeviceName = snapshot.Str("deviceName");
        pendingDisplayRequest = snapshot.Str("displayRequest");
        // 先确认已接收，再生成较耗时的新材质；网页能区分传输延迟与画面准备。
        if (newer) await Request("/device/ack", "POST", Report());
        foreach (var cmd in (snapshot["commands"] as JsonArray ?? new()).OfType<JsonObject>()) await Execute(cmd);
    }
    private async Task UploadDisplay()
    {
        string availability = State.LocalPaused || State.RemotePaused ? "paused" : !State.EnableWallpaper ? "disabled" : "unavailable";
        string image = "";
        if (availability == "unavailable" && !HasWallpaperConflict(refresh: true) && State.AppliedVersion > 0 && ClientLifecycle.OwnedWallpaper(State.AppliedWallpaper, store.DirectoryPath) && File.Exists(State.AppliedWallpaper)) {
            // 动态壁纸会盖在系统壁纸上，存在冲突时不能将底下的缓存图报告为当前展示。
            // 只读取已成功设置的本程序 PNG。绝不捕获桌面、其他窗口或用户原壁纸。
            bool current = string.Equals(wallpaper.Current(State.OriginalMonitor), State.AppliedWallpaper, StringComparison.OrdinalIgnoreCase);
            availability = current ? "displayed" : "replaced";
            if (current) {
                using var source = File.OpenRead(State.AppliedWallpaper);
                var bitmap = BitmapFrame.Create(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                double scale = Math.Min(1, 1280d / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
                BitmapSource thumbnail = scale < 1 ? new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)) : bitmap;
                using var buffer = new MemoryStream();
                var encoder = new JpegBitmapEncoder { QualityLevel = 72 };
                encoder.Frames.Add(BitmapFrame.Create(thumbnail)); encoder.Save(buffer);
                if (buffer.Length > 400000) {
                    buffer.SetLength(0); buffer.Position = 0;
                    encoder = new JpegBitmapEncoder { QualityLevel = 45 };
                    encoder.Frames.Add(BitmapFrame.Create(thumbnail)); encoder.Save(buffer);
                }
                if (buffer.Length > 400000) throw new InvalidDataException("看板缩略图超过回传上限。");
                image = Convert.ToBase64String(buffer.ToArray());
            }
        }
        var report = Report(); report["requestId"] = pendingDisplayRequest;
        report["availability"] = availability; report["imageBase64"] = image;
        await Request("/device/display", "POST", report);
    }
    public void DismissNotice() { screenNotice.Dismiss(); State.LocalControlEpoch++; nextHeartbeat = 0; Save(); }
    private string Group(string action) => action == "sync" ? "sync" : action is "pause" or "resume" ? "pause" : action is "notice" or "stop_notice" ? "notice" : "page";
    private async Task Execute(JsonObject command)
    {
        string id = command.Str("id"), action = command.Str("action"), group = Group(action); int sequence = command.Int("seq");
        if (State.Results.TryGetValue(id, out var old)) { await Request("/device/command-acks", "POST", old.DeepClone().AsObject()); return; }
        if (command.Str("bindingId") != State.BindingId || command.Str("syncEpoch") != State.SyncEpoch || command.Num("expiresAt") <= Clock.ToUnixTimeSeconds()) return;
        JsonObject result = Report(); result["commandId"] = id;
        void Reject(string message) { result["result"] = "rejected"; result["error"] = message; }
        if (action is not ("sync" or "pause" or "resume" or "set_page" or "auto_page" or "notice" or "stop_notice")) Reject("不支持该控制类型，请管理员安排更新");
        else if (sequence <= State.GroupSeq.GetValueOrDefault(group)) Reject("旧命令序号已处理");
        else if (group != "sync" && command.Num("localControlEpoch") != State.LocalControlEpoch) Reject("现场操作已改变");
        else if (action == "resume" && State.LocalPaused) Reject("本机暂停需现场恢复");
        else if (action == "notice" && (!State.EnableWallpaper || State.LocalPaused || State.RemotePaused)) Reject("请在教室电脑启用并恢复展示后再发提醒");
        else if (action == "notice" && (command["notice"] is not JsonObject notice || notice.Num("endsAt") <= Clock.ToUnixTimeMilliseconds() / 1000d)) Reject("提醒已到期，不再补播");
        else if (group == "page" && (State.LocalPaused || State.RemotePaused)) Reject("暂停期间不能翻页");
        else if (group == "page" && command.Str("pageSetId") != State.PageSetId) Reject("页集合已改变");
        else if (action is "resume" or "set_page" or "auto_page" && HasWallpaperConflict(refresh: true)) {
            DeferWallpaperConflict();
            result = Report(); result["commandId"] = id;
            result["result"] = "deferred"; result["error"] = ConflictMessage;
            await Request("/device/command-acks", "POST", result); return;
        }
        else if (action is not ("sync" or "stop_notice") && (sessionLocked || !(action == "notice" && command["notice"]?["overFullscreen"]?.GetValue<bool>() == true) && (!State.EnableWallpaper || !SafeToApply()))) {
            result["result"] = "deferred"; result["error"] = "等待全屏课件结束后展示";
            await Request("/device/command-acks", "POST", result); return;
        } else {
            double started = Clock.ToUnixTimeMilliseconds() / 1000d;
            try {
                // 写入执行意图后再产生物理效果；崩溃后不重复开始同一命令。
                State.GroupSeq[group] = sequence; Save();
                if (action == "notice") screenNotice.Show(command, wallpaper.Read(State.MonitorIndex).Rect, Clock);
                else if (action == "stop_notice" || action == "pause") screenNotice.Dismiss();
                if (action == "pause") { bool previous = State.RemotePaused; State.RemotePaused = true; try { RestoreWallpaper(); } catch { State.RemotePaused = previous; throw; } State.ApplyState = "paused"; State.ManualUntil=0; }
                else if (action == "resume") {
                    bool oldRemotePause = State.RemotePaused;
                    State.RemotePaused = false;
                    try { if (!await ApplyCurrent(force: true)) throw new InvalidOperationException("前台状态已变化，尚未应用恢复画面"); }
                    catch { State.RemotePaused = oldRemotePause; throw; }
                }
                else if (action == "set_page") { if (!await ApplyCurrent(command.Int("page"), true)) throw new InvalidOperationException("前台状态已变化，尚未应用目标页"); State.ManualUntil = Clock.ToUnixTimeSeconds() + 120; }
                else if (action == "auto_page") {
                    double oldManualUntil = State.ManualUntil, oldAutoStart=State.AutoPageStartedAt; State.ManualUntil = 0; State.AutoPageStartedAt=Clock.ToUnixTimeSeconds();
                    try { if (!await ApplyCurrent(force: true)) throw new InvalidOperationException("前台状态已变化，尚未恢复自动翻页"); }
                    catch { State.ManualUntil = oldManualUntil; State.AutoPageStartedAt=oldAutoStart; throw; }
                }
                result = Report(); result["commandId"] = id; result["result"] = "executed"; result["startedAt"] = started; result["finishedAt"] = Clock.ToUnixTimeMilliseconds() / 1000d;
            } catch (Exception e) when (e is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException) { Reject("执行失败，未报告应用成功"); }
        }
        State.GroupSeq[group] = Math.Max(sequence, State.GroupSeq.GetValueOrDefault(group));
        State.Results[id] = result.DeepClone().AsObject(); Save();
        await Request("/device/command-acks", "POST", result);
        // 已处理组序号长期保留，详细回执只保留近期上限，避免常驻进程无界增长。
        if (State.Results.Count > 1000) { foreach (string oldId in State.Results.Keys.Take(State.Results.Count - 1000).ToList()) State.Results.Remove(oldId); Save(); }
    }
    private async Task<bool> ApplyCurrent(int requestedPage = 0, bool force = false)
    {
        if (force) await renderGate.WaitAsync(shutdown.Token);
        else if (!await renderGate.WaitAsync(0, shutdown.Token)) return false;
        try { return await RenderCurrent(requestedPage, force); }
        finally { renderGate.Release(); }
    }
    private async Task<bool> RenderCurrent(int requestedPage, bool force)
    {
        if (State.Snapshot is null || stopping) return false;
        if (!State.EnableWallpaper) { State.ApplyState = "waiting"; State.DisplayBlockReason = "disabled"; State.Error = "请在本机状态页启用桌面展示"; return false; }
        if (!State.LocalPaused && !State.RemotePaused && HasWallpaperConflict()) return DeferWallpaperConflict();
        if (!SafeToApply()) {
            if (State.ApplyState != "deferred") nextHeartbeat = 0;
            State.ApplyState = "deferred"; State.DisplayBlockReason = sessionLocked ? "session-locked" : "fullscreen-or-desktop-unavailable";
            changed = true; return false;
        }
        if (State.LocalPaused || State.RemotePaused) {
            if (State.ApplyState != "paused") { RestoreWallpaper(); State.ApplyState = "paused"; State.DisplayBlockReason = "paused"; Save(); }
            changed = false; return true;
        }
        var snapshot = State.Snapshot;
        long controlEpoch = State.LocalControlEpoch;
        int monitorIndex = State.MonitorIndex;
        var monitor = wallpaper.Read(State.MonitorIndex);
        if (State.OriginalCaptured && State.OriginalMonitor != monitor.Id) throw new InvalidOperationException("显示器目标已变化，请先恢复旧目标，再重新选择。");
        if (!State.OriginalCaptured || !ClientLifecycle.OwnedWallpaper(monitor.Original, store.DirectoryPath)) { State.OriginalMonitor = monitor.Id; State.OriginalWallpaper = monitor.Original; State.OriginalCaptured = true; Save(); }
        var layout = Renderer.EffectiveLayout(State, Clock);
        int interval = Math.Max(15, layout.Int("interval", 20));
        if(State.ManualUntil>0 && State.ManualUntil<=Clock.ToUnixTimeSeconds()) { State.ManualUntil=0; State.AutoPageStartedAt=Clock.ToUnixTimeSeconds(); }
        double phase=State.ManualUntil>Clock.ToUnixTimeSeconds() ? -State.Page : State.PageCount>1 ? Math.Floor(Math.Max(0,Clock.ToUnixTimeSeconds()-State.AutoPageStartedAt)/interval) : 0;
        bool licenseFallback = !ReferenceEquals(layout, State.Snapshot!["classroom"]!["layout"]);
        string Key(DateTimeOffset at) => $"{State.CachedVersion}:{State.ETag}:{licenseFallback}:{at:yyyyMMddHHmm}:{phase}:{State.ClockUncertain}:{State.ReduceEffects}:{System.Windows.SystemParameters.HighContrast}:{System.Windows.SystemParameters.ClientAreaAnimation}";
        string renderKey = Key(Clock);
        if (!force && !changed && renderKey == lastRenderKey) return true;
        double started = Elapsed;
        renderer.MaterialLayer = await glass.GetAsync(State, Clock, monitor.Rect.Right - monitor.Rect.Left, monitor.Rect.Bottom - monitor.Rect.Top, shutdown.Token);
        // 首次生成材质期间，老师仍可暂停或打开课件；应用前重新核对现场状态。
        if (stopping || State.LocalPaused || State.RemotePaused || !State.EnableWallpaper || !SafeToApply()) return false;
        if (!ReferenceEquals(snapshot, State.Snapshot) || controlEpoch != State.LocalControlEpoch || monitorIndex != State.MonitorIndex || !ReferenceEquals(layout, Renderer.EffectiveLayout(State, Clock))) { changed = true; return false; }
        if (HasWallpaperConflict(refresh: true)) return DeferWallpaperConflict();
        var renderClock = Clock; renderKey = Key(renderClock);
        Frame frame;
        try { frame = renderer.Render(State, renderClock, monitor.Rect.Right - monitor.Rect.Left, monitor.Rect.Bottom - monitor.Rect.Top, requestedPage); }
        catch when (!State.ReduceEffects) {
            State.ReduceEffects = true;
            try { frame = renderer.Render(State, renderClock, monitor.Rect.Right - monitor.Rect.Left, monitor.Rect.Bottom - monitor.Rect.Top, requestedPage) with { Reason = "材质生成失败，已回退纯色" }; }
            finally { State.ReduceEffects = false; }
        }
        if (frame.Material == "subtle" && glass.Reason.Length > 0) frame = frame with { Reason = glass.Reason };
        wallpaper.Set(monitor.Id, frame.Path);
        // 只有实际设置成功后更新这些字段，渲染或 COM 失败均不能提前推进。
        if (State.PageSetId != frame.PageSetId || State.AutoPageStartedAt<=0) { State.ManualUntil = 0; State.AutoPageStartedAt=Clock.ToUnixTimeSeconds(); }
        State.PageSetId = frame.PageSetId; State.Page = frame.Page; State.PageCount = frame.Count;
        State.AppliedVersion = State.CachedVersion; State.AppliedWallpaper = frame.Path; State.RenderedAt = renderClock.ToUnixTimeSeconds(); State.EffectiveTheme = frame.Theme; State.EffectiveMaterial = frame.Material; State.MaterialReason = frame.Reason;
        State.LastRenderDurationMs = Math.Max(0, (Elapsed - started) * 1000); State.DisplayBlockReason = "";
        State.ApplyState = State.ClockUncertain ? "error" : "applied"; State.Error = State.ClockUncertain ? "系统时间异常，时效组件已暂停，请在现场校时" : Math.Abs(State.ClockOffset) > 120 ? "本机时间偏差超过 2 分钟，当前使用服务端校准时间" : "";
        changed = false; lastRenderKey = renderKey; nextHeartbeat = 0; Save();
        PruneFrames();
        return true;
    }
    private bool DeferWallpaperConflict()
    {
        // 不关闭其他程序。继续同步内容，退出壁纸软件后重新生成并应用，不能误命中旧渲染键。
        changed = true;
        if (State.ApplyState != "deferred" || State.Error != ConflictMessage) {
            State.ApplyState = "deferred"; State.DisplayBlockReason = "wallpaper-conflict"; State.Error = ConflictMessage; nextHeartbeat = 0; Save();
        }
        return false;
    }
    private void PruneFrames()
    {
        // 清理与应用分离；失败图片同样有上限，清理失败不能把已成功的画面改报为失败。
        try {
            foreach(var file in new DirectoryInfo(store.DirectoryPath).GetFiles("board-*.png*").Where(f => ClientLifecycle.OwnedRaster(f.Name)).OrderByDescending(f=>f.LastWriteTimeUtc).Skip(8)) {
                if(string.Equals(file.FullName,State.AppliedWallpaper,StringComparison.OrdinalIgnoreCase) || string.Equals(file.FullName,State.OriginalWallpaper,StringComparison.OrdinalIgnoreCase)) continue;
                try { file.Delete(); } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
            }
        } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
    }
    private string lastRenderKey = "";
    public JsonObject Report()
    {
        State.StateReportSeq++; Save();
        return new() { ["clientVersion"] = AppPaths.Version, ["bindingId"] = State.BindingId, ["syncEpoch"] = State.SyncEpoch, ["stateReportSeq"] = State.StateReportSeq,
            ["localControlEpoch"] = State.LocalControlEpoch, ["localPaused"] = State.LocalPaused, ["remotePaused"] = State.RemotePaused,
            ["cachedVersion"] = State.CachedVersion, ["appliedVersion"] = State.AppliedVersion, ["applyState"] = State.ApplyState,
            ["pageSetId"] = State.PageSetId, ["page"] = State.Page, ["pageCount"] = State.PageCount, ["manualUntil"] = State.ManualUntil,
            ["effectiveTheme"] = State.EffectiveTheme, ["effectiveMaterial"] = State.EffectiveMaterial, ["materialReason"] = State.MaterialReason,
            ["renderedAt"] = State.RenderedAt, ["error"] = State.Error,
            ["activeNoticeId"] = screenNotice.ActiveId, ["noticeEndsAt"] = screenNotice.EndsAt };
    }
    public void RestoreWallpaper()
    {
        if (!State.OriginalCaptured) return;
        // 用户后来手动选择了其他壁纸时，退出/卸载不能把它覆盖成更早的记录。
        if (!ClientLifecycle.OwnedWallpaper(wallpaper.Current(State.OriginalMonitor), store.DirectoryPath)) return;
        string original = State.OriginalWallpaper;
        if (original.Length > 0 && (!File.Exists(original) || ClientLifecycle.OwnedWallpaper(original, store.DirectoryPath))) { original = ""; State.Error = "原壁纸不存在，已恢复系统纯色背景"; }
        wallpaper.Set(State.OriginalMonitor, original);
    }
    public void Unbind()
    {
        assets.Clear();
        screenNotice.Dismiss();
        signals?.Dispose(); signals = null;
        pendingDisplayRequest = "";
        State.PendingRestore = State.EnableWallpaper && State.OriginalCaptured;
        string restoreError = "";
        if (State.PendingRestore && SafeToApply()) {
            try { RestoreWallpaper(); State.PendingRestore=false; }
            catch(Exception e) when(e is IOException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { restoreError="设备已撤销，原壁纸恢复失败，请检查桌面策略"; }
        }
        State.Credential = State.BindingId = State.SyncEpoch = State.DeviceId = State.ClassId = State.PairId = State.PairCode = State.ETag = "";
        State.Snapshot = null; State.CachedVersion = State.AppliedVersion = 0; State.PageSetId = "";
        State.Page = State.PageCount = 1; State.ManualUntil = 0; State.StateReportSeq = 0; State.LocalControlEpoch = 0;
        State.LocalPaused = State.RemotePaused = false; State.Results.Clear(); State.GroupSeq.Clear(); State.Error = restoreError.Length>0 ? restoreError : "设备未绑定，请重新取得绑定码"; Save();
    }
    public void Dispose() { shutdown.Cancel(); signals?.Dispose(); screenNotice.Dispose(); http.Dispose(); shutdown.Dispose(); }
}
