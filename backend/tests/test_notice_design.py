import hashlib
import json
from uuid import uuid4

from sqlalchemy.orm import Session
from test_workflows import classroom, paired, user

from backend.db import Device, Idempotency
from backend.schemas import ScreenNoticeStyle


def test_custom_notice_compatibility_validation_and_delivery(client):
    c, engine = client
    headers, owner = user(c, "notice_design")
    cid = classroom(c, headers)
    did, device_headers, _ = paired(c, headers, cid)
    style = ScreenNoticeStyle(
        fontFamily="kai",
        fontSize=76,
        surface="card",
        position="bottom",
        textColor="#f1e4c8",
        backgroundColor="#243e35",
        flowSeconds=20,
        direction="right",
    ).model_dump()
    notice = {"mode": "popup", "message": "请整理桌面", "duration": 30, "style": style}

    def send(value, revision=0, key=None):
        return c.post(
            f"/devices/{did}/commands",
            headers={**headers, "Idempotency-Key": key or str(uuid4())},
            json={"action": "notice", "expectedControlRevision": revision, "notice": value},
        )

    with Session(engine) as db:
        db.get(Device, did).state = {"clientVersion": "0.4.12"}
        db.commit()
    assert send(notice).status_code == 409
    legacy = {k: v for k, v in notice.items() if k != "style"}
    key = str(uuid4())
    sent = send(legacy, key=key)
    assert sent.status_code == 202
    # 0.4.12 的签名没有 style 字段；旧页面在服务器升级后重试仍返回原命令。
    signature_body = {
        "action": "notice",
        "expectedControlRevision": 0,
        "pageSetId": "",
        "page": 1,
        "notice": {**legacy, "tone": "blue", "overFullscreen": False},
    }
    expected = hashlib.sha256(json.dumps(signature_body, sort_keys=True, ensure_ascii=False).encode()).hexdigest()
    with Session(engine) as db:
        assert db.get(Idempotency, f"{owner}:control:{did}:{key}").request_hash == expected
        db.get(Device, did).state = {"clientVersion": "0.4.13"}
        db.commit()
    assert send(legacy, key=key).json()["id"] == sent.json()["id"]
    for invalid in (
        {"fontSize": 121},
        {"opacity": 0},
        {"backgroundColor": "url(https://example.com)"},
        {"direction": "up"},
        {"flowSeconds": 0},
        {"image": "oversized"},
    ):
        assert send({**notice, "style": {**style, **invalid}}, 1).status_code == 422
    sent = send(notice, 1)
    assert sent.status_code == 202
    commands = c.get("/device/snapshot", headers=device_headers).json()["commands"]
    delivered = next(item for item in commands if item["id"] == sent.json()["id"])
    assert delivered["notice"]["style"] == style
    assert delivered["notice"]["overFullscreen"] is False
    assert send({**notice, "mode": "barrage", "message": "一\n二\n三\n四"}, 2).status_code == 422
