using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Button = Wpf.Ui.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Size = System.Windows.Size;

namespace ClassroomBoard;

internal static class ToolUi
{
    public static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 4, 8, 4), Padding = new Thickness(12, 8, 12, 8), FontSize = 13 };
        button.Click += (_, _) => Run(action); return button;
    }
    public static TextBlock Text(string text, int size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 10, 7), VerticalAlignment = VerticalAlignment.Center };
    public static TextBox Input(string text, int width = 100) => new() { Text = text, Width = width, Padding = new Thickness(10), Margin = new Thickness(0, 4, 10, 4), VerticalContentAlignment = VerticalAlignment.Center };
    public static ScrollViewer Scroll(object content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 12) };
    public static int Number(string text, int min, int max) => int.TryParse(text.Trim(), out int n) && n >= min && n <= max ? n : throw new ArgumentException($"请输入 {min}–{max} 的整数。");
    public static void Run(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Xml.XmlException or FormatException or OverflowException or System.Runtime.InteropServices.ExternalException or NotSupportedException) {
            string message = error is ArgumentException or InvalidOperationException ? error.Message : "操作未完成，请检查文件格式、存储权限或剪贴板状态。";
            System.Windows.MessageBox.Show(message, "课堂工具", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    public static string? Prompt(Window owner, string title, string value, bool multiline = false)
    {
        var window = new AppWindow(title, 540, multiline ? 470 : 290) { Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ReduceMaterial = true };
        var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(Text(title, 20));
        var input = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, Height = multiline ? 230 : 44, MaxLength = multiline ? 2100 : 40, Padding = new Thickness(10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; panel.Children.Add(input);
        panel.Children.Add(Button("确定", () => window.DialogResult = true)); window.SetBody(panel); window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text : null;
    }
    public static bool Confirm(Window owner, string message) => System.Windows.MessageBox.Show(owner, message, "课堂工具", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public static void SavePng(BitmapSource bitmap, Window owner, string name)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png", FileName = name, DefaultExt = ".png", AddExtension = true };
        if (dialog.ShowDialog(owner) != true) return;
        using var stream = File.Create(dialog.FileName); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
    }
    public static BitmapSource Render(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element); bitmap.Freeze(); return bitmap;
    }
}

internal sealed class RosterImportWindow : AppWindow
{
    private RosterSheet[] sheets = [];
    private readonly ComboBox sheetChoice = new() { MinWidth = 230, Margin = new Thickness(0, 4, 8, 4) };
    private readonly ComboBox nameChoice = new() { MinWidth = 180, Margin = new Thickness(0, 4, 8, 4) };
    private readonly ComboBox idChoice = new() { MinWidth = 180, Margin = new Thickness(0, 4, 8, 4) };
    private readonly System.Windows.Controls.CheckBox header = new() { Content = "第一行是表头", Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBox preview = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 220, Padding = new Thickness(14), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock status = ToolUi.Text("选择文件后预览，不会直接覆盖现有名单。");
    private readonly Button accept;
    private bool updating;
    public string[]? Imported { get; private set; }
    internal RosterImportWindow(Window owner) : base("导入班级名单", 760, 690)
    {
        Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; ReduceMaterial = true;
        accept = ToolUi.Button("导入到名单编辑框", () => { Imported = Selection(); DialogResult = true; }); accept.IsEnabled = false;
        var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(ToolUi.Text("导入名单", 24));
        var browse = ToolUi.Button("选择 Excel / CSV / TXT…", () => { }); panel.Children.Add(browse);
        browse.Click += async (_, _) => {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "名单文件|*.xlsx;*.csv;*.txt", Multiselect = false };
            if (dialog.ShowDialog(this) != true) return; browse.IsEnabled = false; accept.IsEnabled = false; status.Text = "正在读取文件…";
            try {
                var loaded = await Task.Run(() => RosterImport.Read(dialog.FileName)); if (!IsVisible) return;
                sheets = loaded; sheetChoice.ItemsSource = loaded.Select(s => s.Name).ToArray(); sheetChoice.SelectedIndex = 0; SetColumns();
            } catch (Exception error) when (error is IOException or ArgumentException or System.Xml.XmlException or UnauthorizedAccessException or OverflowException or FormatException) { sheets = []; status.Text = "读取失败：请检查格式、文件大小及是否为完整的名单表。"; preview.Text = error is ArgumentException ? error.Message : "支持未加密的 .xlsx、.csv、.txt；旧 .xls 需另存。"; }
            finally { browse.IsEnabled = true; }
        };
        var sheetRow = new WrapPanel(); sheetRow.Children.Add(ToolUi.Text("工作表")); sheetRow.Children.Add(sheetChoice); panel.Children.Add(sheetRow);
        var columns = new WrapPanel(); columns.Children.Add(ToolUi.Text("姓名列")); columns.Children.Add(nameChoice); columns.Children.Add(ToolUi.Text("学号列")); columns.Children.Add(idChoice); panel.Children.Add(columns);
        panel.Children.Add(header); panel.Children.Add(preview); panel.Children.Add(status);
        panel.Children.Add(ToolUi.Text("最多 500 人。学号会放在姓名前；公式只读取文件已保存的结果。导入后仍需点击“保存并使用”。", 12));
        panel.Children.Add(accept); SetBody(ToolUi.Scroll(panel));
        sheetChoice.SelectionChanged += (_, _) => SetColumns(); nameChoice.SelectionChanged += (_, _) => Preview(); idChoice.SelectionChanged += (_, _) => Preview(); header.Checked += (_, _) => Preview(); header.Unchecked += (_, _) => Preview();
    }
    private RosterSheet? Sheet => sheetChoice.SelectedIndex >= 0 && sheetChoice.SelectedIndex < sheets.Length ? sheets[sheetChoice.SelectedIndex] : null;
    private void SetColumns()
    {
        if (Sheet is not RosterSheet sheet) return; updating = true;
        var labels = Enumerable.Range(0, sheet.Columns).Select(i => $"第 {i + 1} 列 · {(sheet.Rows.Length > 0 && i < sheet.Rows[0].Length ? sheet.Rows[0][i] : "")}").ToArray();
        nameChoice.ItemsSource = labels; idChoice.ItemsSource = new[] { "不使用学号列" }.Concat(labels).ToArray();
        var first = sheet.Rows.FirstOrDefault() ?? []; int nameIndex = Array.FindIndex(first, s => s.Trim() is "姓名" or "学生姓名" or "名字" or "name" or "Name"); int idIndex = Array.FindIndex(first, s => s.Trim() is "学号" or "编号" or "序号" or "studentId");
        nameChoice.SelectedIndex = sheet.Columns > 0 ? Math.Max(0, nameIndex) : -1; idChoice.SelectedIndex = idIndex >= 0 && idIndex != nameChoice.SelectedIndex ? idIndex + 1 : 0; header.IsChecked = nameIndex >= 0 || idIndex >= 0;
        updating = false; Preview();
    }
    private string[] Selection() => Sheet?.Select(nameChoice.SelectedIndex, idChoice.SelectedIndex - 1, header.IsChecked == true) ?? throw new ArgumentException("请先选择工作表。");
    private void Preview()
    {
        if (updating) return;
        try { var names = Selection(); preview.Text = string.Join(Environment.NewLine, names.Take(12)); if (names.Length > 12) preview.Text += "\n…"; status.Text = $"将导入 {names.Length} 人，请核对首行和学号。"; accept.IsEnabled = true; }
        catch (ArgumentException e) { preview.Text = Sheet is RosterSheet sheet ? string.Join('\n', sheet.Rows.Take(5).Select(r => string.Join(" ｜ ", r))) : ""; status.Text = e.Message; accept.IsEnabled = false; }
    }
}

internal sealed class AbsenceWindow : AppWindow
{
    internal AbsenceWindow(Window owner, ClassroomBook book) : base("本次缺席排除", 570, 660)
    {
        Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; ReduceMaterial = true;
        var root = new DockPanel { Margin = new Thickness(24) }; var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(ToolUi.Text(book.Active.Name + " · 勾选缺席成员", 22)); top.Children.Add(ToolUi.Text("点名、分组和下一轮值日会跳过；座位表保留完整名单。只在本次运行中记住缺席情况。"));
        var search = ToolUi.Input("", 450); top.Children.Add(search); var choices = book.Active.Members.Select(name => new System.Windows.Controls.CheckBox { Content = name, IsChecked = book.Absent.Contains(name), Margin = new Thickness(8), FontSize = 16 }).ToArray();
        var list = new StackPanel(); foreach (var choice in choices) list.Children.Add(choice);
        search.TextChanged += (_, _) => { foreach (var choice in choices) choice.Visibility = choice.Content.ToString()!.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed; };
        var bottom = new WrapPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        bottom.Children.Add(ToolUi.Button("全部到场", () => { foreach (var choice in choices) choice.IsChecked = false; }));
        bottom.Children.Add(ToolUi.Button("应用缺席排除", () => { book.SetAbsent(choices.Where(c => c.IsChecked == true).Select(c => c.Content.ToString()!)); DialogResult = true; })); root.Children.Add(ToolUi.Scroll(list)); SetBody(root);
    }
}
