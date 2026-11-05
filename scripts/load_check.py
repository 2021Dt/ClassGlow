"""20 个隔离终端的短时并发检查；结果不代表教室现场或生产容量承诺。"""

import asyncio
import json
import time
from datetime import UTC, datetime, timedelta
from pathlib import Path
from uuid import uuid4

import httpx

from backend.version import RELEASE, VERSION
from scripts.verify_client import isolated_server

ROOT = Path(__file__).resolve().parents[1]
PASSWORD = "E2e-Temporary-Only!2026"


def fixtures():
    with httpx.Client(base_url="http://127.0.0.1:8019", trust_env=False, timeout=10) as client:

        def request(method, path, **kwargs):
            response = client.request(method, path, **kwargs)
            response.raise_for_status()
            return response.json()

        admin = request(
            "POST", "/auth/login", json={"login": "e2e_admin", "password": PASSWORD, "clientId": "load-root"}
        )
        root = {"Authorization": f"Bearer {admin['token']}"}
        devices = []
        for index in range(20):
            login = f"load_teacher_{index}"
            request(
                "POST",
                "/system/accounts",
                headers=root,
                json={"login": login, "password": PASSWORD, "name": "并发验收", "identity": "headteacher"},
            )
            token = request("POST", "/auth/login", json={"login": login, "password": PASSWORD})["token"]
            teacher = {"Authorization": f"Bearer {token}"}
            classroom = request("POST", "/classes", headers=teacher, json={"name": f"并发验收班 {index}"})
            start = datetime.now(UTC)
            identity = str(uuid4())
            draft = request(
                "PUT",
                f"/drafts/{identity}",
                headers=teacher,
                json={
                    "id": identity,
                    "kind": "homework",
                    "classIds": [classroom["id"]],
                    "subject": "数学",
                    "body": "并发验收作业",
                    "from": start.isoformat(),
                    "until": (start + timedelta(days=1)).isoformat(),
                    "revision": 0,
                },
            )
            published = request(
                "POST",
                "/publish-batches",
                headers={**teacher, "Idempotency-Key": str(uuid4())},
                json={"draftId": identity, "draftRevision": draft["revision"]},
            )
            pairing = request("POST", "/device-pairings", json={"name": "隔离验收终端"})
            request(
                "POST",
                "/device-pairings/confirm",
                headers=teacher,
                json={"classId": classroom["id"], "code": pairing["code"]},
            )
            headers = {
                "Authorization": f"Bearer {pairing['secret']}",
                "X-ClassGlow-Client-Version": VERSION,
                "X-ClassGlow-Assets": str(RELEASE["assetProtocol"]),
            }
            binding = request("GET", f"/device-pairings/{pairing['id']}", headers=headers)
            devices.append(
                {"headers": headers, "binding": binding, "etag": "", "version": published["results"][0]["version"]}
            )
        return root, devices


async def exercise(root, devices):
    durations, statuses = [], []
    async with httpx.AsyncClient(base_url="http://127.0.0.1:8019", trust_env=False, timeout=10) as client:

        async def request(method, path, **kwargs):
            begin = time.perf_counter()
            response = await client.request(method, path, **kwargs)
            durations.append((time.perf_counter() - begin) * 1000)
            statuses.append(response.status_code)
            if response.status_code not in (200, 304):
                response.raise_for_status()
            return response

        async def device_cycle(device, seq):
            response = await request(
                "GET", "/device/snapshot", headers={**device["headers"], "If-None-Match": device["etag"]}
            )
            device["etag"] = response.headers["ETag"]
            binding = device["binding"]
            await request(
                "POST",
                "/device/heartbeat",
                headers=device["headers"],
                json={
                    "bindingId": binding["bindingId"],
                    "syncEpoch": binding["syncEpoch"],
                    "stateReportSeq": seq,
                    "localControlEpoch": 0,
                    "localPaused": False,
                    "remotePaused": False,
                    "cachedVersion": device["version"],
                    "appliedVersion": 0,
                    "applyState": "waiting",
                    "pageSetId": "load-check",
                    "page": 1,
                    "pageCount": 1,
                    "effectiveTheme": "mica_light",
                    "effectiveMaterial": "solid",
                },
            )

        for seq in range(1, 4):
            await asyncio.gather(
                *(device_cycle(device, seq) for device in devices),
                *(request("GET", "/bootstrap", headers=root) for _ in devices),
            )
    ordered = sorted(durations)
    return {
        "passed": ordered[(len(ordered) * 95 + 99) // 100 - 1] < 8000,
        "devices": len(devices),
        "requests": len(statuses),
        "errors": sum(code >= 400 for code in statuses),
        "notModified": statuses.count(304),
        "p50Ms": round(ordered[len(ordered) // 2], 1),
        "p95Ms": round(ordered[(len(ordered) * 95 + 99) // 100 - 1], 1),
        "maximumMs": round(max(ordered), 1),
        "database": "SQLite WAL",
        "network": "loopback",
        "workers": 1,
    }


def main():
    with isolated_server():
        root, devices = fixtures()
        result = asyncio.run(exercise(root, devices))
        path = ROOT / ".qa/load-result.json"
        path.parent.mkdir(exist_ok=True)
        path.write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(json.dumps(result))
        if not result["passed"]:
            raise SystemExit("并发请求耗时超过客户端截止时间，请检查结果。")


if __name__ == "__main__":
    main()
