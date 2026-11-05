"""有界、进程内的请求指标；日志只记录路由模板，不能带邀请秘密、正文和口令。"""

import json
import logging
import time
from collections import deque
from threading import Lock

from fastapi import APIRouter, Depends

from .security import system_admin

router = APIRouter()
logger = logging.getLogger("uvicorn.error")


class Metrics:
    def __init__(self):
        self.lock = Lock()
        self.started = time.time()
        self.routes = {}

    def observe(self, method, route, status, elapsed):
        method = method if method in ("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS") else "OTHER"
        with self.lock:
            key = (method, route)
            if key not in self.routes and len(self.routes) >= 256:
                key = (method, "<other>")
            item = self.routes.setdefault(
                key, {"count": 0, "errors": 0, "milliseconds": 0, "recent": deque(maxlen=128)}
            )
            item["count"] += 1
            item["errors"] += int(status >= 500)
            item["milliseconds"] += elapsed
            item["recent"].append(elapsed)

    def snapshot(self):
        with self.lock:
            routes = []
            for (method, route), item in self.routes.items():
                samples = sorted(item["recent"])
                routes.append(
                    {
                        "method": method,
                        "route": route,
                        "count": item["count"],
                        "errors": item["errors"],
                        "meanMs": round(item["milliseconds"] / item["count"], 1),
                        "p95Ms": round(samples[max(0, (len(samples) * 95 + 99) // 100 - 1)], 1),
                    }
                )
            return {
                "since": self.started,
                "requests": sum(row["count"] for row in routes),
                "errors": sum(row["errors"] for row in routes),
                "sampleLimit": 128,
                "routes": sorted(routes, key=lambda row: row["p95Ms"], reverse=True),
            }


metrics = Metrics()


def log_request(request_id, method, route, status, elapsed, error_type=""):
    value = {
        "event": "request",
        "request_id": request_id,
        "method": method,
        "route": route,
        "status": status,
        "duration_ms": round(elapsed, 1),
    }
    if error_type:
        value["error_type"] = error_type
    logger.log(logging.ERROR if status >= 500 else logging.INFO, json.dumps(value, ensure_ascii=False))


@router.get("/system/metrics", dependencies=[Depends(system_admin)])
def request_metrics():
    return metrics.snapshot()
