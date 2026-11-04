import { session } from "./api";

/** 使用 Authorization 请求更新通知，不把凭证放在 URL；失败仍由页面的定时核对兜底。 */
export function watchUpdates(path: string, changed: () => void) {
  const stop = new AbortController();
  const token = session.token;
  let request: AbortController | undefined;
  let retry: ReturnType<typeof setTimeout> | undefined;
  async function connect() {
    if (stop.signal.aborted || token !== session.token) return;
    request = new AbortController();
    let deadline = setTimeout(() => request?.abort(), 35000);
    try {
      const response = await fetch(`/api${path}`, {
        headers: { Authorization: `Bearer ${token}` },
        signal: request.signal,
      });
      if (response.status === 401 || response.status === 403) {
        changed();
        return;
      }
      if (response.status === 404) return;
      if (
        !response.ok ||
        !response.headers.get("Content-Type")?.includes("text/event-stream") ||
        !response.body
      )
        throw new Error("通知连接未就绪");
      const reader = response.body
        .pipeThrough(new TextDecoderStream())
        .getReader();
      let buffer = "";
      try {
        while (!stop.signal.aborted) {
          const { value, done } = await reader.read();
          if (done) break;
          clearTimeout(deadline);
          deadline = setTimeout(() => request?.abort(), 35000);
          buffer += value;
          let end: number;
          while ((end = buffer.indexOf("\n\n")) >= 0) {
            const event = buffer.slice(0, end);
            buffer = buffer.slice(end + 2);
            if (/^event: (ready|changed|revoked)$/m.test(event)) changed();
            if (event.includes("event: revoked")) return;
          }
        }
      } finally {
        await reader.cancel().catch(() => {});
      }
    } catch {
      /* 后台重连，不用通知通道错误覆盖正常内容。 */
    } finally {
      clearTimeout(deadline);
    }
    if (!stop.signal.aborted) retry = setTimeout(connect, 3000);
  }
  void connect();
  return () => {
    stop.abort();
    request?.abort();
    clearTimeout(retry);
  };
}
