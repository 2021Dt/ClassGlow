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
    private string activeClass = "", excluded = "";
    private bool selectingClass;
    private string[][]? lastGroups;
    public ClassroomToolsPanel(Engine engine)
    {
        this.engine = engine; InitializeComponent();
        RosterInput.Text = savedText = string.Join(Environment.NewLine, engine.Tools.Roster);
        Results.Text = engine.Tools.Roster.Count > 0 ? "准备好，开始抽取" : "先保存一份名单";
        ready = true;
        ToolTabs.Items.Add(new System.Windows.Controls.TabItem { Header = "课堂积分", Content = new ScorePanel(engine) });
        ToolTabs.Items.Add(new System.Windows.Controls.TabItem { Header = "屏幕工具", Content = new ScreenToolsPanel(engine) });
        ToolTabs.Items.Add(new System.Windows.Controls.TabItem { Header = "座位编排", Content = new SeatsPanel(engine.Book) });
        ToolTabs.Items.Add(new System.Windows.Controls.TabItem { Header = "任务轮值", Content = new DutiesPanel(engine.Book) });
        engine.Book.Changed += RefreshClasses; RefreshClasses(); Refresh();
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
        RosterSummary.Text = Dirty ? "名单有未保存修改" : $"已保存 {count} 项 · 本次到场 {engine.Tools.PresentCount}";
        RoundSummary.Text = $"本轮剩余 {engine.Tools.Remaining} / {engine.Tools.PresentCount}";
        DrawButton.IsEnabled = ResetRoundButton.IsEnabled = engine.Tools.PresentCount > 0 && !Dirty;
        GroupButton.IsEnabled = engine.Tools.PresentCount > 1 && !Dirty;
        CopyButton.IsEnabled = hasResults;
        SendGroupsButton.IsEnabled = lastGroups is not null && !Dirty;
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
        if (Dirty && engine.Book.Active.Seats.Count + engine.Book.Active.Duties.Count > 0 && !ToolUi.Confirm(Window.GetWindow(this), "修改名单会重置该班座位表和轮值安排，积分保留。继续保存吗？")) return;
        engine.SaveLocalRoster(RosterInput.Text);
        RosterInput.Text = savedText = string.Join(Environment.NewLine, engine.Tools.Roster);
        hasResults = false; Results.Text = engine.Tools.Roster.Count > 0 ? "准备好，开始抽取" : "先保存一份名单";
        Results.FontSize = 28; ResultTitle.Text = "抽取和分组结果";
        Refresh(); Tell($"名单已保存，共 {engine.Tools.Roster.Count} 项，已开始新一轮。");
    });
    private void ClearRoster_Click(object sender, RoutedEventArgs e) => Run(() => {
        if (engine.Tools.Roster.Count > 0 && !ToolUi.Confirm(Window.GetWindow(this), "清空当前班名单并重置座位和轮值？其他班级不受影响。")) return;
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
        lastGroups = groups;
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
    private void RefreshClasses()
    {
        string nextExcluded = string.Join('\n', engine.Book.Absent.Order());
        bool switched = activeClass != engine.Book.Active.Id;
        bool changedRoster = savedText != string.Join(Environment.NewLine, engine.Tools.Roster);
        if (switched || changedRoster || excluded != nextExcluded) {
            lastGroups = null; hasResults = false; Results.Text = "名单已更新，准备抽取"; ResultTitle.Text = "抽取和分组结果";
        }
        if (switched || changedRoster && !Dirty) RosterInput.Text = savedText = string.Join(Environment.NewLine, engine.Tools.Roster);
        activeClass = engine.Book.Active.Id; excluded = nextExcluded;
        selectingClass = true; ClassSelector.ItemsSource = engine.Book.Data.Classes; ClassSelector.SelectedValue = activeClass; selectingClass = false; Refresh();
    }
    private void ClassSelector_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!ready || selectingClass || ClassSelector.SelectedValue is not string id || id == activeClass) return;
        Run(() => { if (Dirty) { selectingClass = true; ClassSelector.SelectedValue = activeClass; selectingClass = false; throw new InvalidOperationException("请先保存当前名单修改，再切换班级。"); } try { engine.Book.Switch(id); } catch { RefreshClasses(); throw; } });
    }
    private void AddClass_Click(object sender, RoutedEventArgs e) => Run(() => { if (Dirty) throw new InvalidOperationException("请先保存当前名单修改。"); string? name = ToolUi.Prompt(Window.GetWindow(this), "新增本地班级名称", ""); if (name is not null) engine.Book.Add(name); });
    private void RenameClass_Click(object sender, RoutedEventArgs e) => Run(() => { string? name = ToolUi.Prompt(Window.GetWindow(this), "班级名称", engine.Book.Active.Name); if (name is not null) engine.Book.Rename(name); });
    private void DeleteClass_Click(object sender, RoutedEventArgs e) => Run(() => { if (ToolUi.Confirm(Window.GetWindow(this), $"删除“{engine.Book.Active.Name}”的名单、积分、座位与轮值？此操作无法撤销。")) engine.Book.DeleteActive(); });
    private void Absence_Click(object sender, RoutedEventArgs e) => Run(() => { RequireSaved(); new AbsenceWindow(Window.GetWindow(this), engine.Book).ShowDialog(); });
    private void ImportRoster_Click(object sender, RoutedEventArgs e) => Run(() => {
        if (Dirty && !ToolUi.Confirm(Window.GetWindow(this), "导入将替换编辑框中未保存的名单，继续吗？")) return;
        var window = new RosterImportWindow(Window.GetWindow(this)); if (window.ShowDialog() == true && window.Imported is not null) { RosterInput.Text = string.Join(Environment.NewLine, window.Imported); Tell("导入已填入编辑框，核对后点击“保存并使用”。"); }
    });
    private void SendGroups_Click(object sender, RoutedEventArgs e) => Run(() => {
        RequireSaved(); if (lastGroups is null) throw new InvalidOperationException("请先生成分组。");
        if (engine.Book.Active.Teams.Any(t => t.Score != 0) && !ToolUi.Confirm(Window.GetWindow(this), "使用新分组会清零当前积分，继续吗？")) return;
        engine.Book.SetTeams(lastGroups); ToolTabs.SelectedIndex = 2; Tell("分组已送到积分板。");
    });
}
