"""真实 Windows 客户端联调：独立本机服务，可选缓存验证或可视化渲染，均不设置系统壁纸。"""

import argparse
import json
import os
import shutil
import subprocess
import time
from datetime import UTC, datetime, timedelta
from pathlib import Path
from urllib.parse import urlsplit
from uuid import uuid4

import httpx

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--render-preview", action="store_true", help="在隔离窗口执行渲染和全部远程控制")
parser.add_argument("--hold-open", action="store_true", help="验收后保留窗口，直到输出目录出现 stop 文件")
args = parser.parse_args()
api_url = os.environ.get("KANBAN_TEST_API_URL", "http://127.0.0.1:8016")
if urlsplit(api_url).hostname not in ("127.0.0.1", "localhost", "::1"):
    raise SystemExit("联调只允许本机独立测试服务，不能在正式服务器创建演示数据。")
login = os.environ.get("KANBAN_TEST_LOGIN")
password = os.environ.get("KANBAN_TEST_PASSWORD")
if not login or not password:
    raise SystemExit("先在隔离数据库创建管理员，再设置 KANBAN_TEST_LOGIN / KANBAN_TEST_PASSWORD。")
root = Path(__file__).resolve().parents[1]
output = root / ".qa" / f"client-{uuid4().hex[:8]}"
output.mkdir(parents=True)
sdk = root / ".tools" / "dotnet" / "dotnet.exe"
dll = root / "desktop/ClassroomBoard/bin/Release/net10.0-windows/ClassroomBoard.dll"
launcher = [str(sdk) if sdk.exists() else shutil.which("dotnet") or "dotnet", str(dll)]
client = httpx.Client(
    base_url=api_url.rstrip("/") + "/",
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
    "/auth/login",
    json={"login": login, "password": password},
)
headers = {"Authorization": f"Bearer {auth['token']}"}
classroom = request("POST", "/classes", headers=headers, json={"name": "客户端联调班（非真实数据）"})
if args.render_preview:
    # 使用老师在测试服务实际发布的设计，逐项对照网页和 Windows 渲染。
    classes = request("GET", "/classes", headers=headers)
    source = next(
        (item for item in classes if item["layout"].get("editor") == "canvas" and item["id"] != classroom["id"]), None
    )
    if source is None:
        raise SystemExit("请先通过教师设计室在测试班级发布一份自由画布。")
    request("PUT", f"/classes/{classroom['id']}/layout", headers=headers, json={**source["layout"], "revision": 1})
    request(
        "PUT",
        f"/classes/{classroom['id']}/display",
        headers=headers,
        json={
            "revision": classroom["revision"],
            "message": "保持好奇，认真思考。",
            "event": "校园阅读节",
            "eventDate": (datetime.now(UTC) + timedelta(days=12)).strftime("%Y-%m-%d"),
        },
    )
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
        "body": "\n".join(f"第{n}行 · 阅读与思考" for n in range(1, 31)),
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
    [
        *launcher,
        "--control-preview" if args.render_preview else "--integration-test",
        "--output",
        str(output),
        "--server",
        str(client.base_url),
    ],
    creationflags=subprocess.CREATE_NO_WINDOW,
)
print(f"OUTPUT={output}", flush=True)
try:
    wait_for(lambda: (output / "pairing.json").exists())
    code = json.loads((output / "pairing.json").read_text())["code"]
    device = request(
        "POST", "/device-pairings/confirm", headers=headers, json={"code": code, "classId": classroom["id"]}
    )

    def state():
        return request("GET", f"/devices/{device['id']}/control", headers=headers)

    cached = wait_for(lambda: v if (v := state())["device"]["cached"] >= published["results"][0]["version"] else None)
    if args.render_preview:
        wait_for(lambda: state()["device"]["applied"] >= published["results"][0]["version"])
    else:
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
    pause_result = "executed" if args.render_preview else "deferred"
    wait_for(lambda: any(c["id"] == pause["id"] and c["state"] == pause_result for c in state()["commands"]))
    if args.render_preview:
        assert state()["device"]["remotePaused"]
        for action in ("resume", "set_page", "auto_page"):
            current = state()["device"]
            payload = {"action": action, "expectedControlRevision": current["revision"]}
            if action in ("set_page", "auto_page"):
                assert current["pageCount"] > 1, "长内容必须产生完整分页"
                payload.update(pageSetId=current["pageSetId"], page=2)
            command = request(
                "POST",
                f"/devices/{device['id']}/commands",
                headers={**headers, "Idempotency-Key": str(uuid4())},
                json=payload,
            )
            wait_for(
                lambda command_id=command["id"]: any(
                    item["id"] == command_id and item["state"] == "executed" for item in state()["commands"]
                )
            )
            if action == "set_page":
                assert state()["device"]["page"] == 2
    result = {
        "passed": True,
        "classId": classroom["id"],
        "deviceId": device["id"],
        "cachedVersion": state()["device"]["cached"],
        "appliedVersion": state()["device"]["applied"],
        "pages": state()["device"]["pageCount"],
        "syncReceipt": "executed",
        "pauseReceipt": pause_result,
        "resumePageAndAuto": "executed" if args.render_preview else "not-tested",
        "output": "isolated-window" if args.render_preview else "cache-only",
        "wallpaperChanged": False,
    }
    (output / "result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False), flush=True)
    if args.hold_open:
        wait_for(lambda: (output / "stop").exists() or process.poll() is not None, timeout=900)
finally:
    (output / "stop").touch()
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.terminate()
    client.close()
