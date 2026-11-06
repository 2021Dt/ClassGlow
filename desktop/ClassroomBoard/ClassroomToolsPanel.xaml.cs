using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;

namespace ClassroomBoard;

public partial class ClassroomToolsPanel : System.Windows.Controls.UserControl
{
    private readonly Engine engine;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool ready, hasResults;
    private string savedText = "";
    public ClassroomToolsPanel(Engine engine)
    {
        this.engine = engine; InitializeComponent();
        RosterInput.Text = savedText = string.Join(Environment.NewLine, engine.Tools.Roster);
        ready = true; Refresh();
        timer.Tick += (_, _) => RefreshTimer();
        IsVisibleChanged += (_, _) => { if (IsVisible) { RefreshTimer(); timer.Start(); } else timer.Stop(); };
        Unloaded += (_, _) => timer.Stop();
    }
    private bool Dirty => RosterInput.Text != savedText;
    private void Tell(string message) => Feedback.Text = message;
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { Tell(error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or COMException) { Tell("操作未完成，请检查本机存储权限或显示器状态后重试。"); }
    }
    private static int Number(string input, string label, int min, int max)
    {
        if (!int.TryParse(input.Trim(), out int value) || value < min || value > max) throw new ArgumentException($"{label}须为 {min}–{max} 的整数。");
        return value;
    }
    private void RequireSaved()
    {
        if (Dirty) throw new InvalidOperationException("名单有未保存修改，请先点击“保存并使用”。");
        if (engine.Tools.Roster.Count == 0) throw new InvalidOperationException("请先输入并保存名单。");
    }
    private void Refresh()
    {
        int count = engine.Tools.Roster.Count;
        RosterSummary.Text = Dirty ? "名单有未保存修改" : $"已保存 {count} 项";
        RoundSummary.Text = $"本轮剩余 {engine.Tools.Remaining} / {count}";
        DrawButton.IsEnabled = ResetRoundButton.IsEnabled = count > 0 && !Dirty;
        GroupButton.IsEnabled = count > 1 && !Dirty;
        CopyButton.IsEnabled = hasResults;
        RefreshTimer();
    }
    private void RefreshTimer()
    {
        StopTimerButton.IsEnabled = engine.LocalCountdownActive;
        StartTimerButton.Content = engine.LocalCountdownActive ? "重新开始倒计时" : "开始倒计时";
        TimerStatus.Text = engine.LocalCountdownActive ? "本地倒计时运行中，关闭工具窗口后继续计时。" : "本地倒计时未运行。";
    }
    private void RosterInput_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (ready) Refresh(); }
    private void SaveRoster_Click(object sender, RoutedEventArgs e) => Run(() => {
        engine.SaveLocalRoster(RosterInput.Text);
        RosterInput.Text = savedText = string.Join(Environment.NewLine, engine.Tools.Roster);
        hasResults = false; Results.Text = engine.Tools.Roster.Count > 0 ? "准备好，开始抽取" : "先保存一份名单";
        Results.FontSize = 28; ResultTitle.Text = "抽取和分组结果";
        Refresh(); Tell($"名单已保存，共 {engine.Tools.Roster.Count} 项，已开始新一轮。");
    });
    private void ClearRoster_Click(object sender, RoutedEventArgs e) => Run(() => {
        engine.SaveLocalRoster(""); RosterInput.Text = savedText = "";
        hasResults = false; Results.Text = "先保存一份名单"; ResultTitle.Text = "抽取和分组结果";
        Refresh(); Tell("本机名单和本轮抽取记录已清空。");
    });
    private void Draw_Click(object sender, RoutedEventArgs e) => Run(() => {
        RequireSaved(); var selected = engine.Tools.Draw(Number(DrawCountInput.Text, "抽取数量", 1, 50), NoRepeat.IsChecked == true);
        Results.Text = string.Join(Environment.NewLine, selected); Results.FontSize = selected.Length == 1 ? 48 : 28;
        ResultTitle.Text = $"本次抽取 · {selected.Length} 项"; hasResults = true; Refresh(); Tell("抽取完成。");
    });
    private void ResetRound_Click(object sender, RoutedEventArgs e) => Run(() => {
        RequireSaved(); engine.Tools.ResetRound(); hasResults = false; Results.Text = "新一轮，准备抽取";
        ResultTitle.Text = "抽取和分组结果"; Refresh(); Tell("本轮记录已重置，完整名单可重新抽取。");
    });
    private void Group_Click(object sender, RoutedEventArgs e) => Run(() => {
        RequireSaved(); var groups = engine.Tools.Group(Number(GroupCountInput.Text, "组数", 2, 50));
        Results.Text = string.Join(Environment.NewLine + Environment.NewLine, groups.Select((g, i) => $"第 {i + 1} 组（{g.Length} 人）{Environment.NewLine}{string.Join("、", g)}"));
        Results.FontSize = 18; ResultTitle.Text = $"分组结果 · {groups.Length} 组"; hasResults = true; Refresh(); Tell("分组完成，抽签轮次保持原状。");
    });
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!hasResults) return;
        try { System.Windows.Clipboard.SetText(Results.Text); Tell("结果已复制。"); }
        catch (ExternalException) { Tell("剪贴板正忙，请稍后重试。"); }
    }
    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        MinutesInput.Text = ((FrameworkElement)sender).Tag?.ToString() ?? "5"; SecondsInput.Text = "0";
    }
    private void StartTimer_Click(object sender, RoutedEventArgs e) => Run(() => {
        int seconds = Number(MinutesInput.Text, "分钟", 0, 60) * 60 + Number(SecondsInput.Text, "秒数", 0, 59);
        engine.StartLocalCountdown(seconds, TimerLabelInput.Text, TimerCard.IsChecked == true);
        RefreshTimer(); Tell("本地倒计时已开始。");
    });
    private void StopTimer_Click(object sender, RoutedEventArgs e) => Run(() => {
        engine.StopLocalCountdown(); RefreshTimer(); Tell("本地倒计时已结束。");
    });
}
