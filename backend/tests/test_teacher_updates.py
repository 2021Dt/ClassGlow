from uuid import uuid4

from sqlalchemy import select
from sqlalchemy.orm import Session
from test_workflows import draft, paired, publish, user

from backend.db import Device, Token, User, now


def test_subject_session_and_notice_lifecycle(client):
    c, engine = client
    headers, owner = user(c, "highschool")
    with Session(engine) as db:
        assert 29.9 * 86400 < db.scalar(select(Token)).expires - now() <= 30 * 86400
    created = c.post("/classes", headers=headers, json={"name": "高一二班", "stage": "高中"})
    assert created.status_code == 201
    cid = created.json()["id"]
    classroom = c.get("/bootstrap", headers=headers).json()["classes"][0]
    assert {"生物", "地理", "政治", "化学"} <= set(classroom["subjects"])
    changed = c.put(
        f"/classes/{cid}/settings",
        headers=headers,
        json={
            "name": "高一二班",
            "stage": "高中",
            "subjects": [*classroom["subjects"], "日语"],
            "revision": classroom["revision"],
        },
    )
    assert changed.status_code == 200
    assert publish(c, headers, draft(c, headers, [cid], subject="日语")).status_code == 200
    did, device_headers, _identity = paired(c, headers, cid)
    second, _, _ = paired(c, headers, cid)

    def control(action, revision, notice=None, key=None):
        return c.post(
            f"/devices/{did}/commands",
            headers={**headers, "Idempotency-Key": key or str(uuid4())},
            json={"action": action, "expectedControlRevision": revision, **({"notice": notice} if notice else {})},
        )

    notice = {"mode": "countdown", "message": "课堂练习", "duration": 60}
    assert control("notice", 0, notice).status_code == 409  # 旧客户端不接受会误报成功的新指令。
    with Session(engine) as db:
        db.get(Device, did).state = {"clientVersion": "0.4.12"}
        db.commit()
    key = str(uuid4())
    sent = control("notice", 0, notice, key)
    assert sent.status_code == 202
    assert control("notice", 0, notice, key).json()["id"] == sent.json()["id"]
    commands = c.get("/device/snapshot", headers=device_headers).json()["commands"]
    assert commands[0]["notice"]["endsAt"] > now() + 55
    assert not c.get(f"/devices/{second}/control", headers=headers).json()["commands"]
    with Session(engine) as db:
        db.get(User, owner).premium_until = 0
        db.commit()
    assert control("stop_notice", 1).status_code == 202  # 停止播放不被许可到期阻挡。
    with Session(engine) as db:
        db.get(User, owner).premium_until = now() + 3600
        db.get(Device, did).state = {"clientVersion": "0.4.12", "localPaused": True}
        db.commit()
    assert control("notice", 2, notice).status_code == 409
