using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Color = System.Windows.Media.Color;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;

namespace ClassroomBoard;

/// <summary>名单、抽取不变量与实际面板操作；所有数据和输出均在隔离目录。</summary>
public static partial class ToolsChecks
{
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("无效输入没有被拒绝"); }
    private sealed class Desktop : IWallpaper
    {
        public int Writes;
        public (string Id, NativeRect Rect, string Original) Read(int index) => ("isolated", new() { Right = 640, Bottom = 360 }, "");
        public bool SafeToApply() => true;
        public void Set(string monitor, string path) { Writes++; throw new InvalidOperationException("检查禁止写壁纸"); }
        public string Current(string monitor) => "";
    }
    private sealed class Notices : IScreenNotice
    {
        public string ActiveId { get; set; } = "";
        public double EndsAt { get; private set; }
        public double Duration;
        public void Show(JsonObject command, NativeRect monitor, DateTimeOffset clock) { ActiveId = command.Str("id"); EndsAt = command["notice"].Num("endsAt"); Duration = EndsAt - clock.ToUnixTimeMilliseconds() / 1000d; }
        public void Dismiss() { ActiveId = ""; EndsAt = 0; }
        public void Dispose() => Dismiss();
    }
    private sealed class Transport : HttpMessageHandler
    {
        public int Requests;
        public JsonArray Commands = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Requests++;
            JsonObject body = new();
            if (request.RequestUri!.AbsolutePath == "/device/snapshot") {
                body = JsonNode.Parse("""{"schema":1,"deviceId":"test-device","bindingId":"test-binding","syncEpoch":"test-epoch","classVersion":1,"classroom":{"id":"test-class","name":"隔离检查","layout":{"theme":"mica_light","material":"solid","font":"medium","iconMargin":"none","interval":20,"modules":{"homework":true,"todo":true,"timetable":true,"countdown":true,"message":true}}},"contents":[]} """)!.AsObject();
                body["commands"] = Commands.DeepClone(); body["serverTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") });
        }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly Desktop Desktop = new();
        public readonly Notices Notices = new();
        public readonly Transport Transport = new();
        public readonly StateStore Store;
        public readonly Engine Engine;
        public Fixture(string output, string name) {
            Store = new(Path.Combine(output, name));
            Engine = new(Store, new ClientState { Server = "http://127.0.0.1/" }, Desktop, Notices, transport: Transport, listenForSignals: false);
        }
        public void Bind() { var s = Engine.State; s.BindingId = "test-binding"; s.DeviceId = "test-device"; s.ClassId = "test-class"; s.SyncEpoch = "test-epoch"; s.Credential = "isolated"; }
        public void Dispose() => Engine.Dispose();
    }
    private static async Task Cases(string output, JsonArray results)
    {
        async Task Case(string name, Func<Task> test) {
            try { await test(); results.Add(new JsonObject { ["name"] = name, ["passed"] = true }); }
            catch (Exception e) { results.Add(new JsonObject { ["name"] = name, ["passed"] = false, ["detail"] = e.ToString() }); }
        }
        await Case("roster-parsing-and-limits", () => {
            Require(LocalClassroomTools.Parse(" 小林 01\r\n\n小林 02\n小林 01\n").SequenceEqual(new[] { "小林 01", "小林 02" }), "空行、同名编号或重复项处理错误");
            Reject(() => LocalClassroomTools.Parse(new string('名', 61))); Reject(() => LocalClassroomTools.Parse("甲\t乙"));
            Reject(() => LocalClassroomTools.Parse(string.Join('\n', Enumerable.Range(0, 501)))); return Task.CompletedTask;
        });
        await Case("no-repeat-round-and-reset", () => {
            var roster = Enumerable.Range(1, 23).Select(i => $"同学 {i}").ToArray(); var tools = new LocalClassroomTools(roster);
            var drawn = tools.Draw(10, true).Concat(tools.Draw(10, true)).Concat(tools.Draw(3, true)).ToArray();
            Require(drawn.Distinct().Count() == 23 && drawn.All(roster.Contains) && tools.Remaining == 0, "本轮重复或遗漏");
            Reject(() => tools.Draw(1, true)); Require(tools.Remaining == 0, "失败抽取改变了本轮状态");
            tools.ResetRound(); Require(tools.Draw(23, true).Distinct().Count() == 23, "新一轮没有恢复完整名单"); return Task.CompletedTask;
        });
        await Case("replacement-draw-is-distinct-within-batch", () => {
            var tools = new LocalClassroomTools(new[] { "甲", "乙", "丙" });
            for (int i = 0; i < 8; i++) Require(tools.Draw(3, false).Distinct().Count() == 3, "同批抽取重复");
            Require(tools.Remaining == 3, "允许重复模式消耗了不重复轮次"); return Task.CompletedTask;
        });
        await Case("groups-are-balanced-complete-and-independent", () => {
            for (int size = 2; size <= 55; size++) for (int count = 2; count <= Math.Min(12, size); count++) {
                var tools = new LocalClassroomTools(Enumerable.Range(0, size).Select(i => $"编号{i}")); tools.Draw(1, true);
                var groups = tools.Group(count); var entries = groups.SelectMany(g => g).ToArray();
                Require(groups.Length == count && entries.Length == size && entries.Distinct().Count() == size && entries.All(tools.Roster.Contains), "分组遗漏或重复成员");
                Require(groups.Max(g => g.Length) - groups.Min(g => g.Length) <= 1 && tools.Remaining == size - 1, "分组不均衡或改变抽签轮次");
            }
            return Task.CompletedTask;
        });
        await Case("invalid-operations-preserve-roster-and-round", () => {
            var tools = new LocalClassroomTools(new[] { "甲", "乙", "丙" }); tools.Draw(1, true);
            Reject(() => tools.Draw(3, true)); Reject(() => tools.Group(4)); Reject(() => tools.Replace(new[] { new string('甲', 61) }));
            Require(tools.Roster.Count == 3 && tools.Remaining == 2, "无效操作改变名单或轮次");
            tools.Replace(new[] { "新甲", "新乙" }); Require(tools.Remaining == 2 && tools.Draw(2, true).All(n => n.StartsWith("新")), "换名单残留旧轮次"); return Task.CompletedTask;
        });
        await Case("roster-encrypted-and-excluded-from-reports", () => {
            using var f = new Fixture(output, "storage"); f.Engine.SaveLocalRoster("NeverUploadStudentA\nNeverUploadStudentB");
            Require(f.Store.Load().LocalRoster.Count == 2, "名单没有持久保存");
            f.Engine.Tools.Draw(1, true);
            using (var reopened = new Engine(f.Store, desktop: new Desktop(), notices: new Notices(), transport: new Transport(), listenForSignals: false))
                Require(reopened.Tools.Roster.Count == 2 && reopened.Tools.Remaining == 2, "重启后名单丢失或未开始新一轮");
            Require(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(f.Store.DirectoryPath, "state.bin"))).Contains("NeverUploadStudent"), "名单明文落盘");
            Require(!f.Engine.Report().ToJsonString().Contains("NeverUploadStudent") && !Diagnostics.Create(f.Engine.State).ToJsonString().Contains("NeverUploadStudent"), "名单进入回报或诊断");
            f.Engine.SaveLocalRoster(""); Require(f.Store.Load().LocalRoster.Count == 0, "清空后仍保存名单"); return Task.CompletedTask;
        });
        await Case("roster-save-failure-preserves-memory", () => {
            using var f = new Fixture(output, "blocked"); File.WriteAllText(f.Store.DirectoryPath, "blocks-directory");
            try { f.Engine.SaveLocalRoster("甲"); throw new InvalidOperationException("应写入失败"); } catch (IOException) { }
            Require(f.Engine.Tools.Roster.Count == 0 && f.Engine.State.LocalRoster.Count == 0, "保存失败仍推进名单"); return Task.CompletedTask;
        });
        await Case("countdown-works-unbound-paused-and-offline", async () => {
            using var f = new Fixture(output, "countdown"); f.Engine.State.LocalPaused = f.Engine.State.RemotePaused = true;
            f.Engine.StartLocalCountdown(90, "本地任务"); await f.Engine.Tick(); f.Engine.LocalPause(true); await f.Engine.Tick();
            Require(f.Engine.LocalCountdownActive && Math.Abs(f.Notices.Duration - 90) < .01, "未绑定、暂停或关闭壁纸时无法本地计时");
            Require(f.Transport.Requests == 0 && f.Desktop.Writes == 0, "本地计时产生网络请求或改写壁纸");
            var report = f.Engine.Report(); Require(report.Str("activeNoticeId") == "" && report.Num("noticeEndsAt") == 0, "本地计时进入远程提醒状态");
            f.Engine.StopLocalCountdown(); Require(!f.Engine.LocalCountdownActive, "本地停止无效");
        });
        await Case("invalid-countdown-does-not-replace-active-one", () => {
            using var f = new Fixture(output, "limits"); f.Engine.StartLocalCountdown(60, "原任务"); string id = f.Notices.ActiveId;
            Reject(() => f.Engine.StartLocalCountdown(0, "")); Reject(() => f.Engine.StartLocalCountdown(3601, "")); Reject(() => f.Engine.StartLocalCountdown(5, new string('甲', 61)));
            Require(f.Notices.ActiveId == id, "输入错误仍替换当前计时"); return Task.CompletedTask;
        });
        await Case("local-stop-does-not-dismiss-remote-notice", () => {
            using var f = new Fixture(output, "remote-owned"); f.Notices.ActiveId = "remote-command"; f.Engine.StopLocalCountdown();
            Require(f.Notices.ActiveId == "remote-command", "本地停止误关远程提醒"); return Task.CompletedTask;
        });
        await Case("remote-stop-does-not-dismiss-local-countdown", async () => {
            using var f = new Fixture(output, "local-owned"); f.Bind(); f.Engine.StartLocalCountdown(60, "现场任务");
            f.Transport.Commands.Add(new JsonObject { ["id"] = "remote-stop", ["action"] = "stop_notice", ["seq"] = 1, ["bindingId"] = "test-binding", ["syncEpoch"] = "test-epoch", ["localControlEpoch"] = f.Engine.State.LocalControlEpoch, ["expiresAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60 });
            await f.Engine.Tick(); Require(f.Engine.LocalCountdownActive && f.Engine.State.Results["remote-stop"].Str("result") == "executed", "远程提醒停止影响了本机计时");
        });
        await Case("lock-and-shutdown-dismiss-local-countdown", async () => {
            using var f = new Fixture(output, "shutdown"); f.Engine.StartLocalCountdown(60, "任务"); f.Engine.SessionChanged(true);
            Require(!f.Engine.LocalCountdownActive, "锁屏后仍显示本地提醒"); f.Engine.SessionChanged(false); f.Engine.StartLocalCountdown(60, "任务");
            await f.Engine.StopAsync(); Require(!f.Engine.LocalCountdownActive, "退出后本地提醒残留");
        });
        await Case("actual-tools-panel-flow", () => {
            using var f = new Fixture(output, "panel"); var panel = new ClassroomToolsPanel(f.Engine);
            T Control<T>(string name) => (T)panel.FindName(name);
            void Click(string name) => Control<ButtonBase>(name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Control<TextBox>("RosterInput").Text = "林同学 01\n林同学 02\n陈同学 03\n王同学 04\n周同学 05\n许同学 06\n郑同学 07";
            Require(!Control<ButtonBase>("DrawButton").IsEnabled, "未保存名单仍可抽取"); Click("SaveRosterButton");
            Click("DrawButton"); Require(f.Engine.Tools.Remaining == 6 && Control<ButtonBase>("CopyButton").IsEnabled, "点名入口未工作");
            Control<TextBox>("GroupCountInput").Text = "3"; Click("GroupButton"); Require(Control<TextBox>("Results").Text.Contains("第 3 组"), "分组入口未工作");
            Capture(panel, Path.Combine(output, "tools-groups.png"));
            Control<System.Windows.Controls.TabControl>("ToolTabs").SelectedIndex = 1;
            Control<TextBox>("MinutesInput").Text = "0"; Control<TextBox>("SecondsInput").Text = "30"; Click("StartTimerButton");
            Require(f.Engine.LocalCountdownActive, "本地计时入口未工作"); Click("StopTimerButton"); Require(!f.Engine.LocalCountdownActive, "本地停止入口未工作");
            Capture(panel, Path.Combine(output, "tools-timer.png"));
            Control<TextBox>("SecondsInput").Text = "错误"; Click("StartTimerButton"); Require(!f.Engine.LocalCountdownActive, "无效时长仍被启动");
            return Task.CompletedTask;
        });
    }
    private static void Capture(FrameworkElement panel, string path)
    {
        var border = new System.Windows.Controls.Border { Background = new SolidColorBrush(Color.FromRgb(247, 248, 251)), Child = panel };
        border.Measure(new System.Windows.Size(824, 700)); border.Arrange(new Rect(0, 0, 824, 700)); border.UpdateLayout();
        var bitmap = new RenderTargetBitmap(824, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(border);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
        border.Child = null;
    }
    public static int Run(string[] args)
    {
        string output = Path.GetFullPath(args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "ClassGlow-tools-check")); Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; AppWindow.InitializeTheme();
        app.Dispatcher.InvokeAsync(async () => {
            var results = new JsonArray(); await Cases(output, results); await ToolkitCases(output, results); bool passed = results.All(c => c.Flag("passed"));
            File.WriteAllText(Path.Combine(output, "result.json"), new JsonObject { ["passed"] = passed, ["cases"] = results, ["physicalWallpaperModified"] = false, ["realUserStateModified"] = false }.ToJsonString(new() { WriteIndented = true })); app.Shutdown(passed ? 0 : 1);
        });
        return app.Run();
    }
    public static int Preview(string[] args)
    {
        string output = Path.GetFullPath(args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "ClassGlow-tools-preview")); Directory.CreateDirectory(output);
        var app = new System.Windows.Application(); AppWindow.InitializeTheme();
        using var fixture = new Fixture(output, "preview");
        fixture.Engine.SaveLocalRoster("林同学 01\n林同学 02\n陈同学 03\n王同学 04\n周同学 05\n许同学 06\n郑同学 07\n何同学 08");
        fixture.Engine.ScreenTools.Capture = _ => ToolUi.Render(new System.Windows.Controls.Border { Background = Brushes.White, Child = new System.Windows.Controls.TextBlock { Text = "隔离测试画面 · 没有读取桌面\n1 + 2 = 3", FontSize = 36, Foreground = Brushes.RoyalBlue, Padding = new Thickness(40) } }, 640, 360);
        var window = new AppWindow("课堂工具 · 隔离预览", 1040, 840) { ReduceMaterial = true };
        window.SetBody(new ClassroomToolsPanel(fixture.Engine)); return app.Run(window);
    }
}
