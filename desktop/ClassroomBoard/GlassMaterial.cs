using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace ClassroomBoard;

/// <summary>用随安装包携带的原版 Liquid Glass 生成静态底图；日常刷新只复用图片。</summary>
public sealed class GlassMaterial(string directory)
{
    private const string Host = "classglow-render.invalid";
    private string memoryKey = "";
    private BitmapSource? bitmap;
    private DateTime retryAfter;
    public string Reason { get; private set; } = "";
    public int Generations { get; private set; }
    private string Cache => Path.Combine(directory, "glass-material.png");
    private string KeyFile => Path.Combine(directory, "glass-material.key");
    public const string ProfileFolder = "material-webview";

    public async Task<BitmapSource?> GetAsync(ClientState state, DateTimeOffset clock, int width, int height, CancellationToken cancellation)
    {
        var layout = Renderer.EffectiveLayout(state, clock);
        Reason = "";
        if (layout.Str("editor") != "canvas" || layout.Str("material") == "solid" ||
            state.ReduceEffects || SystemParameters.HighContrast || !SystemParameters.ClientAreaAnimation || !Renderer.TransparencyEnabled()) {
            bitmap = null; memoryKey = ""; return null;
        }
        // 限制临时渲染表面的像素总量；超出 4K 仍以原生兼容材质完整展示。
        if (width > 4096 || height > 4096 || (long)width * height > 8847360) { Reason = "超大分辨率使用兼容材质"; return null; }
        // 玻璃底图只含图片与材质，最高按 1080p 生成；4K 正文仍由原生渲染器按屏幕尺寸绘制。
        // 相同比例的显示器复用同一份缓存，避免首次 4K 折射占用过多临时内存。
        double resolutionScale = Math.Min(1, Math.Min(1920d / width, 1080d / height));
        width = (int)Math.Round(width * resolutionScale); height = (int)Math.Round(height * resolutionScale);
        var appearance = layout.DeepClone().AsObject();
        foreach (var item in appearance["canvas"]!.AsArray()) {
            // 材质层不处理教学正文，文字变化也不应重新唤起浏览器进程。
            foreach (string field in new[] { "text", "title", "event", "eventDate", "subjectFilter" }) item![field] = "";
        }
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{AppPaths.Version}:{width}x{height}:" + appearance.ToJsonString())));
        if (key == memoryKey && bitmap is not null) return bitmap;
        try {
            if (File.Exists(Cache) && File.Exists(KeyFile) && File.ReadAllText(KeyFile) == key) {
                try { bitmap = ReadImage(Cache, width, height); memoryKey = key; return bitmap; }
                catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException) { /* 中断写入或旧尺寸缓存视为未命中，重新生成。 */ }
            }
            if (DateTime.UtcNow < retryAfter) { Reason = "暂用兼容材质"; return null; }
            await CaptureAsync(appearance, width, height, cancellation);
            bitmap = ReadImage(Cache, width, height); memoryKey = key;
            File.WriteAllText(KeyFile, key); Generations++;
            return bitmap;
        } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is COMException or WebView2RuntimeNotFoundException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or ArgumentException or NotSupportedException) {
            // 某些教室没有 WebView2 或图形驱动不兼容，正文和同步继续使用既有原生绘制。
            // 不自动安装运行时、不反复启动失败的浏览器，也不缓存失败画面。
            retryAfter = DateTime.UtcNow.AddMinutes(10);
            Reason = "玻璃材质暂不可用，已使用兼容材质";
            return null;
        }
    }

    private static BitmapSource ReadImage(string path, int width, int height)
    {
        using var input = File.OpenRead(path);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        // 使用流读取固定缓存路径，避免 WPF 的 URI 缓存复用已被替换的旧图。
        image.StreamSource = input; image.EndInit(); image.Freeze();
        if (image.PixelWidth != width || image.PixelHeight != height) throw new InvalidDataException("材质缓存尺寸不符。");
        return image;
    }

    private async Task CaptureAsync(JsonObject layout, int width, int height, CancellationToken cancellation)
    {
        string assets = Path.Combine(AppContext.BaseDirectory, "Renderer");
        if (!File.Exists(Path.Combine(assets, "desktop.html"))) throw new FileNotFoundException("离线材质资源不存在。");
        Directory.CreateDirectory(directory);
        string profile = Path.Combine(directory, ProfileFolder);
        // 本目录完全归 WebView2 使用，清理入口只接受这个固定的子目录，并拒绝目录联接。
        if (Directory.Exists(profile) && (File.GetAttributes(profile) & FileAttributes.ReparsePoint) != 0) throw new IOException("渲染缓存是目录联接。");
        var options = new CoreWebView2EnvironmentOptions("--disable-gpu --disable-background-networking --disable-component-update --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows") {
            AllowSingleSignOnUsingOSPrimaryAccount = false, IsCustomCrashReportingEnabled = true
        };
        var environment = await CoreWebView2Environment.CreateAsync(null, profile, options);
        cancellation.ThrowIfCancellationRequested();
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // 退出事件只通知当前绘制任务，避免旧事件延迟到达时删掉下一轮正在使用的缓存。
        environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
        using var host = new HwndSource(new HwndSourceParameters("ClassGlow material renderer") {
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
            PositionX = -32000, PositionY = -32000, Width = width, Height = height
        });
        CoreWebView2Controller? controller = null;
        try {
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions(); controllerOptions.IsInPrivateModeEnabled = true;
            controller = await environment.CreateCoreWebView2ControllerAsync(host.Handle, controllerOptions);
            cancellation.ThrowIfCancellationRequested();
            controller.ShouldDetectMonitorScaleChanges = false; controller.RasterizationScale = 1;
            controller.Bounds = new System.Drawing.Rectangle(0, 0, width, height); controller.ZoomFactor = 1; controller.IsVisible = true;
            var web = controller.CoreWebView2;
            web.Settings.AreDevToolsEnabled = false; web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.IsStatusBarEnabled = false; web.Settings.IsGeneralAutofillEnabled = false; web.Settings.IsPasswordAutosaveEnabled = false;
            web.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            web.NewWindowRequested += (_, e) => e.Handled = true;
            web.DownloadStarting += (_, e) => e.Cancel = true;
            web.NavigationStarting += (_, e) => e.Cancel = !e.Uri.StartsWith($"https://{Host}/", StringComparison.Ordinal);
            web.SetVirtualHostNameToFolderMapping(Host, assets, CoreWebView2HostResourceAccessKind.DenyCors);
            web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            web.WebResourceRequested += (_, e) => {
                if (!e.Request.Uri.StartsWith($"https://{Host}/", StringComparison.Ordinal) && !e.Request.Uri.StartsWith("data:", StringComparison.Ordinal))
                    e.Response = environment.CreateWebResourceResponse(Stream.Null, 403, "Local rendering only", "");
            };
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            string id = Guid.NewGuid().ToString("N");
            web.WebMessageReceived += (_, e) => {
                if (!e.Source.StartsWith($"https://{Host}/", StringComparison.Ordinal)) return;
                var response = JsonNode.Parse(e.WebMessageAsJson)!;
                if (response.Flag("loaded")) loaded.TrySetResult();
                if (response.Str("id") == id) {
                    if (response.Flag("ready")) ready.TrySetResult();
                    else ready.TrySetException(new InvalidOperationException("材质图片未完成加载。"));
                }
            };
            web.Navigate($"https://{Host}/desktop.html");
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
            web.PostWebMessageAsJson(new JsonObject { ["id"] = id, ["layout"] = layout.DeepClone(), ["width"] = width, ["height"] = height }.ToJsonString());
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
            await using (var output = File.Create(Cache + ".tmp"))
                await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output).WaitAsync(TimeSpan.FromSeconds(10), cancellation);
            ReadImage(Cache + ".tmp", width, height);
            File.Move(Cache + ".tmp", Cache, true);
        } finally {
            controller?.Close();
            // 生成后释放浏览器、渲染和 GPU 子进程。下次正文刷新直接使用 PNG。
            if (controller is not null) await Task.WhenAny(exited.Task, Task.Delay(3000));
            try { CleanupProfile(directory); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* 运行时仍在收尾时，退出或卸载再次清理。 */ }
        }
    }

    public static void CleanupProfile(string directory)
    {
        string path = Path.Combine(Path.GetFullPath(directory), ProfileFolder);
        if (!Directory.Exists(path)) return;
        // 不跟随任何层级的联接，防止误删用户目录；只删除固定的程序渲染缓存。
        void Remove(string folder) {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("渲染缓存含目录联接。");
            foreach (string child in Directory.EnumerateDirectories(folder)) Remove(child);
            foreach (string file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
        Remove(path);
    }
}
