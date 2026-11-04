using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ClassroomBoard;

/// <summary>服务器只通知“有更新”，仍通过原快照接口验证并获取内容；断线不影响轮询兜底。</summary>
public sealed class DeviceSignals : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    public Task Completion { get; }
    private bool disposed;

    public DeviceSignals(Uri address, string credential, Action changed) => Completion = Listen(address, credential, changed);

    private async Task Listen(Uri address, string credential, Action changed)
    {
        while (!stop.IsCancellationRequested) {
            try {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(8));
                using var request = new HttpRequestMessage(HttpMethod.Get, address);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { changed(); return; }
                if (response.StatusCode == HttpStatusCode.NotFound) return; // 旧服务器继续使用原有轮询。
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") return;
                using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var reader = new StreamReader(stream);
                while (!stop.IsCancellationRequested) {
                    // 服务端每 20 秒保活；断网或代理卡住时主动重连，不永久等待半开连接。
                    deadline.CancelAfter(TimeSpan.FromSeconds(35));
                    string? line = await reader.ReadLineAsync(deadline.Token);
                    if (line is null) break;
                    if (line is "event: ready" or "event: changed" or "event: revoked") changed();
                    if (line == "event: revoked") return;
                }
            } catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException) {
                // 常规同步继续运行，不因通知通道失败清空班级、弹窗或切换壁纸。
            }
            try { await Task.Delay(TimeSpan.FromSeconds(3), stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }
    public void Dispose() {
        if (disposed) return;
        disposed = true; stop.Cancel(); http.Dispose();
        _ = Completion.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
