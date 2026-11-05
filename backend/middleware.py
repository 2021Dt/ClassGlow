from starlette.responses import JSONResponse


def body_limit(path: str, method: str) -> int:
    if method == "POST" and path == "/device/display":
        return 600000
    # 提醒画布最大 512 KiB，额外保留外层指令及 JSON 空白的空间。
    if method == "POST" and path.startswith("/devices/") and path.endswith("/commands"):
        return 600000
    # 自由画布可包含压缩背景与图片组件；其他业务接口仍保持 128 KiB。
    return 2 * 1024 * 1024 if method == "PUT" and path.startswith("/classes/") and path.endswith("/layout") else 131072


class BodyLimitMiddleware:
    """在读取请求流时限制总字节数，不能只信任可省略或伪造的 Content-Length。"""

    def __init__(self, app, maximum=131072):
        self.app, self.maximum = app, maximum

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            return await self.app(scope, receive, send)
        body = bytearray()
        while True:
            message = await receive()
            if message["type"] == "http.disconnect":
                return
            body.extend(message.get("body", b""))
            if len(body) > max(self.maximum, body_limit(scope["path"], scope["method"])):
                return await JSONResponse(
                    {"detail": {"code": "body_too_large", "message": "请求内容超过允许大小。"}}, 413
                )(scope, receive, send)
            if not message.get("more_body", False):
                break
        delivered = False

        async def limited_receive():
            nonlocal delivered
            if not delivered:
                delivered = True
                return {"type": "http.request", "body": bytes(body), "more_body": False}
            return await receive()

        await self.app(scope, limited_receive, send)
