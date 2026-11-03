from starlette.responses import JSONResponse


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
            if len(body) > self.maximum:
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
