using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Button = Wpf.Ui.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ClassroomBoard;

public static class PrivacyNotice
{
    public const string Revision = "2026-11-04.2";
    public static string Text => $"""
        班级看板 · Windows 客户端隐私与本机变更说明
        版本：{Revision}

        一、何时联网、发给谁
        在您同意本说明前，程序不联网、不启用启动项、不更换壁纸，也不创建本机业务数据。同意后仍需您发起配对；程序只连接您配置的服务地址，默认 https://teacher.dgplus.vip/api/。
        配对发送本机计算机名称；绑定后发送设备标识、程序版本、同步和展示状态、页码、暂停状态、命令回执及简短错误原因。服务端会接收到连接所需的 IP 地址。程序拉取班级正式内容、课表、主题和背景图，并将它们显示在桌面。班级成员点击“刷新画面”时，程序会回传已成功设置的看板缩略图、生成时间和版本，供本班成员与系统管理员查看；每台设备只保留最近一次回传，替换旧图，不保存回传历史。备份可能包含备份时刻的缩略图。

        二、不收集什么
        没有广告、第三方统计或自动上报崩溃的功能。不读取浏览器历史、键盘输入、摄像头、麦克风和个人文档，不截取整个桌面，不采集其他窗口或用户原壁纸；回传仅来自本程序自己生成的班级看板图片。为避免打断教学，仅检查前台是否为桌面，并读取显示器尺寸和系统显示偏好。

        三、本机保存的位置
        玻璃材质由本机已有的 Microsoft Edge WebView2 运行时处理随安装包提供的离线网页。该网页不访问互联网、不接收设备凭证或作业正文，只处理布局与背景图片；未安装运行时的电脑自动使用兼容材质，不自动安装其他软件。生成后的材质 PNG、校验信息与临时 material-webview 缓存位于下述本程序数据目录，生成完成会关闭渲染进程，卸载时清理。系统共享的 WebView2 运行时由 Windows 或 Microsoft Edge 管理，不随本程序删除。
        数据目录：{AppPaths.DataDirectory}
        设备凭证和班级快照保存在 state.bin，使用当前 Windows 用户的 DPAPI 加密。渲染后的桌面 PNG 是明文图片，可能包含教学内容，无法宣称为加密文件。临时渲染图、同意记录和有大小上限的异常日志均放在同一目录。
        即使首次界面未能正常打开，程序也可能在本机记录启动诊断，包含时间、应用/系统/运行时版本、绘制方式、异常类型和应用方法名，不包含异常正文、本机文件路径、账号或凭证；不会自动上传。
        运行包安装在您选择的专用目录，默认位置为当前用户的 LocalAppData\Programs\{AppPaths.InstallFolder}。安装器登记当前用户的卸载入口和开始菜单快捷方式，可选创建桌面快捷方式，不安装驱动、系统服务或计划任务，不修改防火墙。

        四、需要您主动开启的功能
        壁纸展示和开机启动默认关闭。勾选“随当前用户登录启动”时，只写入 HKCU\{AppPaths.StartupKey} 中名为 {AppPaths.ProductId} 的值。勾选壁纸展示后，仅修改所选显示器，记录原壁纸路径用于恢复；不会强制切换课件或其他程序。

        五、撤回同意与卸载
        托盘“隐私与本机数据”可随时重读说明、撤回同意并卸载；Windows“已安装的应用”也提供同一卸载入口。卸载先停止同步，恢复本程序仍占用的壁纸，再删除本程序文件、启动项、快捷方式、凭证、缓存、同意记录和日志。您后来自行选择的其他壁纸不会被覆盖。
        原壁纸文件若已被移动或删除，将尝试恢复该显示器的系统纯色背景；恢复失败会停止卸载，保留记录以便重试。若目录中有您另放的文件，会保留这些文件，不递归清空整个目录。
        联网时会撤销本设备或待确认的配对，并删除服务器当前保留的回传画面。离线时可继续完成本机卸载，但请由教师在网页“大屏控制”撤销原设备授权。卸载不会删除服务器上的班级、教学内容、审计记录或备份；这些由部署该服务的学校或管理员管理。Windows 自身维护的系统日志、缩略图等由 Windows 管理，下载到其他目录的安装包也不会被擅自删除。
        """;

    private sealed record Consent(string Revision, DateTimeOffset AcceptedAt);
    public static bool Accepted()
    {
        string path = Path.Combine(AppPaths.DataDirectory, "privacy.json");
        if (!File.Exists(path)) return false;
        try { return JsonSerializer.Deserialize<Consent>(File.ReadAllText(path))?.Revision == Revision; }
        catch (JsonException) { return false; }
    }

    public static bool Show(bool requireConsent)
    {
        var window = Create(requireConsent);
        window.ShowDialog(); return window.Tag is true;
    }

    public static AppWindow Create(bool requireConsent, bool preview = false)
    {
        var window = new AppWindow((requireConsent ? "欢迎使用" : "隐私与本机数据") + (preview ? " · 外观预览" : ""));
        var root = new Grid { Margin = new Thickness(32, 0, 32, 24), MaxWidth = 560 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        Grid.SetRow(footer, 1); root.Children.Add(footer);
        var consent = new CheckBox { Content = AppWindow.Hint("我已阅读并同意《隐私与本机数据说明》"), IsChecked = false,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) };
        consent.Resources["ControlCornerRadius"] = new CornerRadius(4);
        if (requireConsent) footer.Children.Add(consent);
        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        var accept = new Button { Content = requireConsent ? "继续" : "关闭", IsEnabled = !requireConsent, MinWidth = 108,
            HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)System.Windows.Application.Current.FindResource("GlowPrimary") };
        Grid.SetColumn(accept, 2);
        consent.Checked += (_, _) => accept.IsEnabled = true;
        consent.Unchecked += (_, _) => accept.IsEnabled = false;
        accept.Click += (_, _) => {
            // 外观预览可完整走完页面，但不能代替用户真正同意或留下业务数据。
            if (requireConsent && !preview) {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                File.WriteAllText(Path.Combine(AppPaths.DataDirectory, "privacy.json"), JsonSerializer.Serialize(new Consent(Revision, DateTimeOffset.UtcNow)));
            }
            window.Tag = true; window.Close();
        };
        buttons.Children.Add(accept);
        if (requireConsent) {
            var reject = new Button { Content = "退出", Style = (Style)System.Windows.Application.Current.FindResource("GlowQuiet"),
                HorizontalAlignment = HorizontalAlignment.Left };
            reject.Click += (_, _) => window.Close(); buttons.Children.Add(reject);
            var progress = AppWindow.Progress(1); Grid.SetColumn(progress, 1); buttons.Children.Add(progress);
        }
        footer.Children.Add(buttons);

        var content = new StackPanel { MaxWidth = 480, Margin = new Thickness(16, 12, 16, 12) };
        content.Children.Add(AppWindow.Artwork("GlowPrivacyArt", 68));
        content.Children.Add(new TextBlock { Text = "隐私与本机数据",
            Style = (Style)System.Windows.Application.Current.FindResource("GlowPageTitle"), Margin = new Thickness(0, 12, 0, 18) });
        if (requireConsent) {
            var summary = new StackPanel();
            foreach (var (title, detail, icon) in new[] {
                ("联网信息", "同意并绑定后，接收教学内容、发送同步状态；本班老师刷新时，可查看本程序的看板缩略图。", "M2,7 Q10,-1 18,7 M5,10 Q10,5 15,10 M8,13 Q10,11 12,13 M10,16 L10,16.2"),
                ("本机权限", "壁纸展示和自动启动默认关闭。不读取个人文档，不截取其他窗口，不访问摄像头或麦克风。", "M10,1 L18,4 V9 Q18,15 10,19 Q2,15 2,9 V4 Z M6,9 L9,12 L14,7"),
                ("卸载清理", "退出恢复原壁纸。卸载清理本程序的文件、凭证、缓存和启动项，保留你另放的文件。", "M3,5 H17 M7,5 V2 H13 V5 M5,5 L6,18 H14 L15,5 M8,8 V15 M12,8 V15")
            }) {
                var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var glyph = new System.Windows.Shapes.Path { Data = System.Windows.Media.Geometry.Parse(icon), Width = 18, Height = 18,
                    Stretch = System.Windows.Media.Stretch.Uniform, StrokeThickness = 1.3,
                    StrokeStartLineCap = System.Windows.Media.PenLineCap.Round, StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                    StrokeLineJoin = System.Windows.Media.PenLineJoin.Round };
                glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "GlowAccent");
                var iconBox = new Border { Style = (Style)System.Windows.Application.Current.FindResource("GlowRowIcon"), Child = glyph,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 14, 0) };
                row.Children.Add(iconBox);
                var labels = new StackPanel();
                labels.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
                var detailText = AppWindow.Hint(detail); detailText.LineHeight = 19; labels.Children.Add(detailText);
                Grid.SetColumn(labels, 1); row.Children.Add(labels); summary.Children.Add(row);
            }
            content.Children.Add(new Border { Style = (Style)System.Windows.Application.Current.FindResource("GlowCard"),
                Padding = new Thickness(20, 4, 20, 4), Child = summary });
        }
        var fullText = new StackPanel();
        fullText.Children.Add(new TextBlock { Text = Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 24 });
        var uninstall = new Button { Content = requireConsent ? "卸载本程序" : "撤回同意并卸载", IsEnabled = !preview,
            Style = (Style)System.Windows.Application.Current.FindResource("GlowSecondary"),
            Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        uninstall.Click += (_, _) => { if (ClientLifecycle.StartUninstaller()) window.Close(); };
        fullText.Children.Add(uninstall);
        if (requireConsent) content.Children.Add(new Expander { Header = "完整隐私说明", Content = fullText,
            Style = (Style)System.Windows.Application.Current.FindResource("GlowDisclosure"), Margin = new Thickness(8, 10, 8, 0) });
        else content.Children.Add(fullText);
        root.Children.Add(new ScrollViewer { Content = content, VerticalContentAlignment = VerticalAlignment.Center,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        window.SetBody(root); return window;
    }
}
