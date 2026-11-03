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
    public const string Revision = "2026-11-03.1";
    public static string Text => $"""
        班级看板 · Windows 客户端隐私与本机变更说明
        版本：{Revision}

        一、何时联网、发给谁
        在您同意本说明前，程序不联网、不启用启动项、不更换壁纸，也不创建本机业务数据。同意后仍需您发起配对；程序只连接您配置的服务地址，默认 https://teacher.dgplus.vip/api/。
        配对发送本机计算机名称；绑定后发送设备标识、程序版本、同步和展示状态、页码、暂停状态、命令回执及简短错误原因。服务端会接收到连接所需的 IP 地址。程序拉取班级正式内容、课表、主题和背景图，并将它们显示在桌面。

        二、不收集什么
        没有广告、第三方统计或自动上报崩溃的功能。不读取浏览器历史、键盘输入、摄像头、麦克风和个人文档，不截图、不上传当前桌面。为避免打断教学，仅检查前台是否为桌面，并读取显示器尺寸和系统显示偏好。

        三、本机保存的位置
        数据目录：{AppPaths.DataDirectory}
        设备凭证和班级快照保存在 state.bin，使用当前 Windows 用户的 DPAPI 加密。渲染后的桌面 PNG 是明文图片，可能包含教学内容，无法宣称为加密文件。临时渲染图、同意记录和有大小上限的异常日志均放在同一目录。
        运行包安装在当前用户的 LocalAppData\Programs\{AppPaths.InstallFolder}。安装器仅登记当前用户的卸载入口和开始菜单快捷方式，不安装驱动、系统服务或计划任务，不修改防火墙。

        四、需要您主动开启的功能
        壁纸展示和开机启动默认关闭。勾选“随当前用户登录启动”时，只写入 HKCU\{AppPaths.StartupKey} 中名为 {AppPaths.ProductId} 的值。勾选壁纸展示后，仅修改所选显示器，记录原壁纸路径用于恢复；不会强制切换课件或其他程序。

        五、撤回同意与卸载
        托盘“隐私与本机数据”可随时重读说明、撤回同意并卸载；Windows“已安装的应用”也提供同一卸载入口。卸载先停止同步，恢复本程序仍占用的壁纸，再删除本程序文件、启动项、快捷方式、凭证、缓存、同意记录和日志。您后来自行选择的其他壁纸不会被覆盖。
        原壁纸文件若已被移动或删除，将尝试恢复该显示器的系统纯色背景；恢复失败会停止卸载，保留记录以便重试。若目录中有您另放的文件，会保留这些文件，不递归清空整个目录。
        联网时会撤销本设备或待确认的配对。离线时可继续完成本机卸载，但请由教师在网页“大屏控制”撤销原设备授权。卸载不会删除服务器上的班级、教学内容、审计记录或备份；这些由部署该服务的学校或管理员管理。Windows 自身维护的系统日志、缩略图等由 Windows 管理，下载到其他目录的安装包也不会被擅自删除。
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
        var window = new AppWindow(requireConsent ? "欢迎使用" : "隐私与本机数据");
        var root = new DockPanel { Margin = new Thickness(32, 8, 32, 22), MaxWidth = 680 };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        var step = AppWindow.Hint(requireConsent ? "01 / 03　隐私说明" : "CLASSGLOW · 教室电脑");
        step.HorizontalAlignment = HorizontalAlignment.Center; step.SetResourceReference(TextBlock.ForegroundProperty, "GlowAccent");
        heading.Children.Add(step);
        heading.Children.Add(new TextBlock { Text = requireConsent ? "欢迎来到班级看板" : "隐私与本机数据", FontSize = 28,
            FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 8) });
        var hint = AppWindow.Hint("开始之前，先了解它会做什么。");
        hint.TextAlignment = TextAlignment.Center; heading.Children.Add(hint);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var consent = new CheckBox { Content = AppWindow.Hint("我已阅读并同意《隐私与本机数据说明》"), IsChecked = false,
            Margin = new Thickness(0, 0, 0, 16) };
        if (requireConsent) footer.Children.Add(consent);
        var buttons = new Grid();
        var accept = new Button { Content = requireConsent ? "下一步：连接班级" : "关闭", IsEnabled = !requireConsent,
            HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)System.Windows.Application.Current.FindResource("GlowPrimary") };
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
            var reject = new Button { Content = "不同意并退出", Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
                Padding = new Thickness(16, 10, 16, 10), HorizontalAlignment = HorizontalAlignment.Left };
            reject.Click += (_, _) => window.Close(); buttons.Children.Add(reject);
        }
        footer.Children.Add(buttons);
        var version = AppWindow.Hint($"ClassGlow {AppPaths.Version}" + (preview ? "  ·  外观预览，不保存同意记录" : "  ·  当前用户客户端"));
        version.HorizontalAlignment = HorizontalAlignment.Center; version.Margin = new Thickness(0, 20, 0, 0);
        footer.Children.Add(version);

        var content = new StackPanel { MaxWidth = 520 };
        if (requireConsent) {
            var summary = new StackPanel();
            summary.Children.Add(new System.Windows.Controls.Image { Source = AppWindow.BrandIcon(), Width = 58, Height = 58, Margin = new Thickness(0, 0, 0, 18) });
            foreach (var (title, detail) in new[] {
                ("只连接你的班级", "同意并绑定后，接收教学内容，发送设备名称和同步状态；没有广告与第三方统计。"),
                ("本机变更由你选择", "壁纸展示和自动启动默认关闭。不读取个人文档，不截图，不访问摄像头或麦克风。"),
                ("随时退出，也能完整卸载", "退出恢复原壁纸。卸载清理本程序的文件、凭证、缓存和启动项，保留你另放的文件。")
            }) {
                summary.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 5) });
                var detailText = AppWindow.Hint(detail); detailText.Margin = new Thickness(0, 0, 0, 12); summary.Children.Add(detailText);
            }
            content.Children.Add(new Border { Style = (Style)System.Windows.Application.Current.FindResource("GlowCard"), Child = summary });
        }
        var fullText = new StackPanel { Margin = new Thickness(12, 10, 12, 8) };
        fullText.Children.Add(new TextBlock { Text = Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 24 });
        var uninstall = new Button { Content = requireConsent ? "卸载本程序" : "撤回同意并卸载", IsEnabled = !preview,
            Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        uninstall.Click += (_, _) => { if (ClientLifecycle.StartUninstaller()) window.Close(); };
        fullText.Children.Add(uninstall);
        if (requireConsent) content.Children.Add(new Expander { Header = "阅读完整隐私与本机数据说明", Content = fullText, Margin = new Thickness(0, 16, 0, 0) });
        else content.Children.Add(fullText);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        window.SetBody(root); return window;
    }
}
