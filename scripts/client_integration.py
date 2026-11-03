"""真实 C# 客户端安全联调：只绑定、缓存和回执，不设置壁纸。需先运行本地后端。"""

import json
import os
import shutil
import subprocess
import time
from datetime import UTC, datetime, timedelta
from pathlib import Path
from uuid import uuid4

import httpx

root = Path(__file__).resolve().parents[1]
output = root / ".qa" / f"client-{uuid4().hex[:8]}"
output.mkdir(parents=True)
sdk = root / ".tools" / "dotnet" / "dotnet.exe"
dll = root / "desktop/ClassroomBoard/bin/Debug/net10.0-windows/ClassroomBoard.dll"
packaged = root / "dist/windows/ClassroomBoard.exe"
launcher = (
    [str(packaged)]
    if packaged.exists()
    else [str(sdk) if sdk.exists() else shutil.which("dotnet") or "dotnet", str(dll)]
)
client = httpx.Client(
    base_url=os.environ.get("KANBAN_TEST_API_URL", "http://127.0.0.1:8000").rstrip("/") + "/",
    timeout=10,
    trust_env=False,
)


def request(method, path, **kwargs):
    # 相对路径保留生产反向代理的 /api/ 前缀，不能意外请求 SPA 首页。
    response = client.request(method, path.lstrip("/"), **kwargs)
    response.raise_for_status()
    return response.json()


def wait_for(callback, timeout=35):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = callback()
        if value:
            return value
        time.sleep(0.5)
    raise TimeoutError("未在规定时间取得真实客户端回执")


auth = request(
    "POST",
    "/auth/register",
    json={"login": f"integration_{uuid4().hex[:10]}", "name": "客户端联调测试", "password": uuid4().hex},
)
headers = {"Authorization": f"Bearer {auth['token']}"}
classroom = request("POST", "/classes", headers=headers, json={"name": "客户端联调班（非真实数据）"})
start = datetime.now(UTC)
draft_id = str(uuid4())
draft = request(
    "PUT",
    f"/drafts/{draft_id}",
    headers=headers,
    json={
        "id": draft_id,
        "kind": "homework",
        "classIds": [classroom["id"]],
        "subject": "数学",
        "body": "真实客户端联调内容，不应用壁纸。",
        "from": start.isoformat(),
        "until": (start + timedelta(days=1)).isoformat(),
        "revision": 0,
    },
)
published = request(
    "POST",
    "/publish-batches",
    headers={**headers, "Idempotency-Key": str(uuid4())},
    json={"draftId": draft_id, "draftRevision": draft["revision"]},
)
process = subprocess.Popen(
    [*launcher, "--integration-test", "--output", str(output), "--server", str(client.base_url)],
    creationflags=subprocess.CREATE_NO_WINDOW,
)
try:
    wait_for(lambda: (output / "pairing.json").exists())
    code = json.loads((output / "pairing.json").read_text())["code"]
    device = request(
        "POST", "/device-pairings/confirm", headers=headers, json={"code": code, "classId": classroom["id"]}
    )

    def state():
        return request("GET", f"/devices/{device['id']}/control", headers=headers)

    cached = wait_for(lambda: v if (v := state())["device"]["cached"] >= published["results"][0]["version"] else None)
    assert cached["device"]["applied"] == 0, "未启用壁纸时不能伪报已应用"
    sync = request(
        "POST",
        f"/devices/{device['id']}/commands",
        headers={**headers, "Idempotency-Key": str(uuid4())},
        json={"action": "sync", "expectedControlRevision": cached["device"]["revision"]},
    )
    wait_for(lambda: any(c["id"] == sync["id"] and c["state"] == "executed" for c in state()["commands"]))
    current = state()
    pause = request(
        "POST",
        f"/devices/{device['id']}/commands",
        headers={**headers, "Idempotency-Key": str(uuid4())},
        json={"action": "pause", "expectedControlRevision": current["device"]["revision"]},
    )
    wait_for(lambda: any(c["id"] == pause["id"] and c["state"] == "deferred" for c in state()["commands"]))
    result = {
        "passed": True,
        "cachedVersion": state()["device"]["cached"],
        "appliedVersion": 0,
        "syncReceipt": "executed",
        "pauseReceipt": "deferred",
        "wallpaperChanged": False,
    }
    (output / "result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))
finally:
    (output / "stop").touch()
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.terminate()
    client.close()
