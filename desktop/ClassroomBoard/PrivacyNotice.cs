using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

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
        bool accepted = false;
        var window = new Window { Title = AppPaths.DisplayName + " · 隐私与本机数据", Width = 720, Height = 760, MinWidth = 520, MinHeight = 500, FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"), FontSize = 14, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var root = new DockPanel { Margin = new Thickness(24) };
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var consent = new CheckBox { Content = "我已阅读并同意上述本机数据与联网说明", IsChecked = false, Margin = new Thickness(0, 0, 0, 14) };
        if (requireConsent) footer.Children.Add(consent);
        var buttons = new WrapPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var accept = new Button { Content = requireConsent ? "同意并继续" : "关闭", IsEnabled = !requireConsent, Padding = new Thickness(16, 8, 16, 8) };
        consent.Checked += (_, _) => accept.IsEnabled = true;
        consent.Unchecked += (_, _) => accept.IsEnabled = false;
        accept.Click += (_, _) => {
            if (requireConsent) {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                File.WriteAllText(Path.Combine(AppPaths.DataDirectory, "privacy.json"), JsonSerializer.Serialize(new Consent(Revision, DateTimeOffset.UtcNow)));
            }
            accepted = true; window.Close();
        };
        buttons.Children.Add(accept);
        if (requireConsent) {
            var reject = new Button { Content = "不同意并退出", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(12, 0, 0, 0) };
            reject.Click += (_, _) => window.Close(); buttons.Children.Add(reject);
        }
        var uninstall = new Button { Content = requireConsent ? "卸载本程序" : "撤回同意并卸载", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(12, 0, 0, 0) };
        uninstall.Click += (_, _) => { if (ClientLifecycle.StartUninstaller()) window.Close(); };
        buttons.Children.Add(uninstall); footer.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = new TextBlock { Text = Text, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 24 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = root; window.ShowDialog(); return accepted;
    }
}
