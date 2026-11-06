using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Button = Wpf.Ui.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using UserControl = System.Windows.Controls.UserControl;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ClassroomBoard;

internal sealed class ScorePanel : UserControl
{
    private readonly Engine engine;
    private readonly WrapPanel cards = new();
    private readonly TextBox goal;
    private readonly ComboBox step = new() { ItemsSource = new[] { 1, 5, 10 }, SelectedIndex = 0, Width = 70, Margin = new Thickness(0, 4, 10, 4) };
    private readonly bool presentation;
    private readonly TextBlock heading;
    internal ScorePanel(Engine value, bool full = false)
    {
        engine = value; presentation = full; var book = engine.Book;
        var root = new StackPanel { Margin = new Thickness(6) }; heading = ToolUi.Text("课堂积分板", 26); root.Children.Add(heading);
        root.Children.Add(ToolUi.Text("在“抽签与分组”生成分组后送到积分板，或直接创建队伍。积分按班级保存，支持撤销最近 100 次操作。"));
        var bar = new WrapPanel();
        bar.Children.Add(ToolUi.Button("设置队伍…", () => {
            var owner = Window.GetWindow(this); string? text = ToolUi.Prompt(owner, "队伍名称（每行一个，确认后重建积分板）", string.Join('\n', book.Active.Teams.Select(t => t.Name)), true);
            if (text is null) return; if (book.Active.Teams.Any(t => t.Score != 0) && !ToolUi.Confirm(owner, "重建队伍会清零当前积分和撤销记录，继续吗？")) return; book.SetTeamNames(text);
        }));
        bar.Children.Add(ToolUi.Text("每次")); bar.Children.Add(step); bar.Children.Add(ToolUi.Button("撤销", book.UndoScore)); bar.Children.Add(ToolUi.Button("清零积分", book.ResetScores));
        goal = ToolUi.Input(book.Active.ScoreGoal.ToString(), 70); bar.Children.Add(ToolUi.Text("目标")); bar.Children.Add(goal); bar.Children.Add(ToolUi.Button("设置目标", () => book.SetGoal(ToolUi.Number(goal.Text, 1, 9999))));
        bar.Children.Add(ToolUi.Button("复制结果", () => System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, book.Active.Teams.OrderByDescending(t => t.Score).Select((t, i) => $"{i + 1}. {t.Name}：{t.Score} 分")))));
        if (!full) bar.Children.Add(ToolUi.Button("大屏展示", () => {
            var window = new AppWindow(book.Active.Name + " · 课堂积分", 1100, 820) { WindowState = WindowState.Maximized, ReduceMaterial = true };
            window.SetBody(new ScorePanel(engine, true)); window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); }; engine.ScreenTools.Track(window); window.Show();
        }));
        else bar.Children.Add(ToolUi.Button("关闭（Esc）", () => Window.GetWindow(this).Close()));
        root.Children.Add(bar); root.Children.Add(cards); Content = ToolUi.Scroll(root);
        Loaded += (_, _) => { book.Changed += Refresh; Refresh(); }; Unloaded += (_, _) => book.Changed -= Refresh;
        Refresh();
    }
    private void Refresh()
    {
        var book = engine.Book; heading.Text = book.Active.Name + " · 课堂积分"; if (!goal.IsKeyboardFocusWithin) goal.Text = book.Active.ScoreGoal.ToString(); cards.Children.Clear();
        if (book.Active.Teams.Count == 0) { cards.Children.Add(ToolUi.Text("还没有队伍，先设置组名或从分组结果导入。")); return; }
        for (int i = 0; i < book.Active.Teams.Count; i++) {
            int index = i; var team = book.Active.Teams[i]; var body = new StackPanel();
            body.Children.Add(ToolUi.Text(team.Name, presentation ? 28 : 20));
            var score = ToolUi.Text(team.Score.ToString(), presentation ? 64 : 42); score.Foreground = team.Score >= book.Active.ScoreGoal ? Brushes.SeaGreen : Brushes.RoyalBlue; body.Children.Add(score);
            body.Children.Add(ToolUi.Text(team.Score >= book.Active.ScoreGoal ? "已达目标" : $"目标 {book.Active.ScoreGoal} 分"));
            var controls = new WrapPanel(); controls.Children.Add(ToolUi.Button("− 减分", () => book.AddScore(index, -(int)step.SelectedItem))); controls.Children.Add(ToolUi.Button("＋ 加分", () => book.AddScore(index, (int)step.SelectedItem))); body.Children.Add(controls);
            if (team.Members.Count > 0) body.Children.Add(ToolUi.Text(string.Join("、", team.Members), 12));
            cards.Children.Add(new Border { Width = presentation ? 260 : 230, Padding = new Thickness(18), Margin = new Thickness(0, 10, 12, 4), BorderBrush = Brushes.LightSteelBlue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Child = body });
        }
    }
}

internal sealed class SeatsPanel : UserControl
{
    private readonly ClassroomBook book;
    private readonly TextBox rows = ToolUi.Input("8", 60), columns = ToolUi.Input("6", 60);
    private readonly StackPanel display = new();
    private readonly TextBlock hint = ToolUi.Text("先生成座位表。点击两席交换，也可拖动换座；锁定的位置不会参与随机排座。");
    private int selected = -1;
    private string activeId = "";
    internal SeatsPanel(ClassroomBook value)
    {
        book = value; var root = new StackPanel { Margin = new Thickness(6) }; root.Children.Add(ToolUi.Text("座位编排", 26)); var bar = new WrapPanel();
        bar.Children.Add(ToolUi.Text("行")); bar.Children.Add(rows); bar.Children.Add(ToolUi.Text("列")); bar.Children.Add(columns);
        bar.Children.Add(ToolUi.Button("生成 / 重排网格", () => {
            if (book.Active.Seats.Count > 0 && !ToolUi.Confirm(Window.GetWindow(this), "重建网格会重置座位、锁定和过道，继续吗？")) return;
            book.MakeSeats(ToolUi.Number(rows.Text, 1, 30), ToolUi.Number(columns.Text, 1, 20)); selected = -1; Refresh();
        }));
        bar.Children.Add(ToolUi.Button("随机排座", book.ShuffleSeats)); bar.Children.Add(ToolUi.Button("锁定 / 解锁选中席", () => book.ToggleSeatLock(selected)));
        bar.Children.Add(ToolUi.Button("过道 / 可用席", () => book.ToggleAisle(selected)));
        bar.Children.Add(ToolUi.Button("导出 PNG…", () => {
            if (book.Active.Seats.Count == 0) throw new ArgumentException("请先生成座位表。");
            var visual = SeatImage(book.Active); ToolUi.SavePng(ToolUi.Render(visual, book.Active.SeatColumns * 160 + 40, book.Active.SeatRows * 100 + 120), Window.GetWindow(this), "班级座位表.png");
        }));
        root.Children.Add(bar); root.Children.Add(hint); root.Children.Add(display);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => { book.Changed += Refresh; Refresh(); }; Unloaded += (_, _) => book.Changed -= Refresh; Refresh();
    }
    private void Refresh()
    {
        var c = book.Active;
        if (activeId != c.Id) { selected = -1; activeId = c.Id; columns.Text = (c.SeatColumns > 0 ? c.SeatColumns : Math.Max(6, (int)Math.Ceiling(c.Members.Count / 30d))).ToString(); rows.Text = (c.SeatRows > 0 ? c.SeatRows : Math.Max(1, (int)Math.Ceiling(c.Members.Count / double.Parse(columns.Text)))).ToString(); }
        if (selected >= c.Seats.Count) selected = -1;
        display.Children.Clear(); if (c.Seats.Count == 0) return;
        display.Children.Add(ToolUi.Text(c.Name + " · 讲台在上方", 20)); var grid = new UniformGrid { Columns = c.SeatColumns, Rows = c.SeatRows, HorizontalAlignment = HorizontalAlignment.Left }; display.Children.Add(grid);
        for (int i = 0; i < c.Seats.Count; i++) {
            int index = i; var button = new Button { Content = new TextBlock { Text = SeatText(c, i), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }, Width = 120, Height = 78, Margin = new Thickness(3), Padding = new Thickness(5), AllowDrop = true, Background = selected == i ? Brushes.LightBlue : c.Aisles.Contains(i) ? Brushes.LightGray : Brushes.WhiteSmoke, Foreground = Brushes.Black };
            bool dragged = false; System.Windows.Point? down = null;
            void SelectSeat() { if (selected >= 0 && selected != index) { book.SwapSeats(selected, index); selected = -1; } else selected = selected == index ? -1 : index; Refresh(); }
            button.PreviewMouseLeftButtonDown += (_, e) => { down = e.GetPosition(grid); dragged = false; button.CaptureMouse(); e.Handled = true; };
            button.PreviewMouseMove += (_, e) => { if (down is System.Windows.Point start && e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(grid) - start).Length > 8) { dragged = true; button.Opacity = .65; } };
            button.PreviewMouseLeftButtonUp += (_, e) => {
                if (down is null) return; var position = e.GetPosition(grid); bool moved = dragged; down = null; button.ReleaseMouseCapture(); button.Opacity = 1; e.Handled = true;
                ToolUi.Run(() => {
                    if (!moved) { SelectSeat(); return; }
                    if (book.Active.Id != c.Id || position.X < 0 || position.Y < 0 || position.X >= grid.ActualWidth || position.Y >= grid.ActualHeight) return;
                    int target = (int)(position.Y / (grid.ActualHeight / c.SeatRows)) * c.SeatColumns + (int)(position.X / (grid.ActualWidth / c.SeatColumns));
                    book.SwapSeats(index, target); selected = -1; Refresh();
                });
            };
            button.LostMouseCapture += (_, _) => { down = null; button.Opacity = 1; };
            button.Click += (_, _) => ToolUi.Run(SelectSeat);
            grid.Children.Add(button);
        }
        hint.Text = selected >= 0 ? $"已选第 {selected / c.SeatColumns + 1} 行、第 {selected % c.SeatColumns + 1} 列；再点一席交换，或使用锁定 / 过道按钮。" : "点击两席交换，或拖动换座；过道和锁定席不参与随机排座。";
    }
    private static string SeatText(LocalClassProfile c, int i) => c.Aisles.Contains(i) ? "过道" : c.Seats[i].Length == 0 ? "空位" : (c.LockedSeats.Contains(i) ? "🔒 " : "") + c.Seats[i];
    internal static FrameworkElement SeatImage(LocalClassProfile c)
    {
        var panel = new StackPanel { Background = Brushes.White, Margin = new Thickness(0) }; panel.Children.Add(new TextBlock { Text = c.Name + " · 座位表\n讲台", TextAlignment = TextAlignment.Center, FontSize = 26, Height = 100, Padding = new Thickness(15), Foreground = Brushes.Black });
        var grid = new UniformGrid { Columns = c.SeatColumns, Rows = c.SeatRows, Margin = new Thickness(20, 0, 20, 20) }; panel.Children.Add(grid);
        for (int i = 0; i < c.Seats.Count; i++) grid.Children.Add(new Border { Height = 94, Margin = new Thickness(3), Background = c.Aisles.Contains(i) ? Brushes.WhiteSmoke : new SolidColorBrush(Color.FromRgb(235, 242, 253)), BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), Child = new TextBlock { Text = SeatText(c, i), FontSize = 21, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) } });
        return panel;
    }
}

internal sealed class DutiesPanel : UserControl
{
    private readonly ClassroomBook book;
    private readonly TextBox tasks = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 125, MaxLength = 1000, Padding = new Thickness(12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel results = new();
    private string activeId = "";
    internal DutiesPanel(ClassroomBook value)
    {
        book = value; var root = new StackPanel { Margin = new Thickness(6), MaxWidth = 740, HorizontalAlignment = HorizontalAlignment.Left }; root.Children.Add(ToolUi.Text("值日与任务轮值", 26));
        root.Children.Add(ToolUi.Text("每行一个任务。点击“下一轮”顺序分配并跳过本次缺席成员；轮到的位置按班级保存。修改下方人选可临时换人，不改变轮换进度。")); root.Children.Add(tasks);
        var bar = new WrapPanel(); bar.Children.Add(ToolUi.Button("生成下一轮", () => book.RotateDuties(tasks.Text))); bar.Children.Add(ToolUi.Button("复制安排", () => System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, book.Active.Duties.Select(d => d.Task + "：" + d.Person))))); root.Children.Add(bar); root.Children.Add(results); Content = ToolUi.Scroll(root);
        Loaded += (_, _) => { book.Changed += Refresh; Refresh(); }; Unloaded += (_, _) => book.Changed -= Refresh; Refresh();
    }
    private void Refresh()
    {
        if (activeId != book.Active.Id) { activeId = book.Active.Id; tasks.Text = book.Active.Duties.Count > 0 ? string.Join('\n', book.Active.Duties.Select(d => d.Task)) : "擦黑板\n整理讲台\n检查门窗"; }
        results.Children.Clear();
        if (book.Active.Duties.Count == 0) results.Children.Add(ToolUi.Text("还没有轮值安排。"));
        for (int i = 0; i < book.Active.Duties.Count; i++) {
            int index = i; var duty = book.Active.Duties[i]; var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(new TextBlock { Text = duty.Task, Width = 200, FontSize = 18, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            var people = new ComboBox { Width = 260, ItemsSource = new[] { duty.Person }.Concat(book.Present).Distinct().ToArray(), SelectedItem = duty.Person, FontSize = 17 };
            people.SelectionChanged += (_, _) => ToolUi.Run(() => { if (people.SelectedItem is string person && person != book.Active.Duties[index].Person) { try { book.ReplaceDuty(index, person); } catch { Refresh(); throw; } } }); row.Children.Add(people);
            if (book.Absent.Contains(duty.Person)) row.Children.Add(ToolUi.Text("本次缺席，请换人")); results.Children.Add(row);
        }
    }
}

internal sealed class ScreenToolsPanel : UserControl
{
    internal ScreenToolsPanel(Engine engine)
    {
        var root = new StackPanel { Margin = new Thickness(6) }; root.Children.Add(ToolUi.Text("讲课屏幕工具", 26)); root.Children.Add(ToolUi.Text("在展示设置选定的屏幕打开。手动开启后可操作，Esc 随时退出；锁屏或退出客户端会关闭所有课堂浮层。"));
        foreach (var (label, mode, description) in new[] {
            ("屏幕批注", "ink", "在课件上画笔、荧光笔、箭头；橡皮、撤销和清空。"),
            ("空白小白板", "whiteboard", "临时写题、演算，支持鼠标和触控笔。"),
            ("聚光灯", "spotlight", "鼠标指到哪里亮到哪里，滚轮调整范围。"),
            ("答案遮挡", "mask", "拖动创建多块遮挡；移动、拉角缩放，双击揭开。"),
            ("截图钉图", "capture", "主动框选后读取该区域，悬浮对照、缩放；只在点击保存时写出 PNG，不上传。")
        }) {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 4) }; var button = ToolUi.Button(label, () => engine.ScreenTools.Open(mode, engine.State.MonitorIndex, Window.GetWindow(this))); button.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(button); row.Children.Add(ToolUi.Text(description)); root.Children.Add(row);
        }
        root.Children.Add(ToolUi.Button("关闭全部课堂浮层与钉图", engine.ScreenTools.CloseAll)); Content = ToolUi.Scroll(root);
    }
}
