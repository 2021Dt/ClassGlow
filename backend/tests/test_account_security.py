"""只覆盖冻结、恢复和单会话的关键边界，不使用真实账号或生产数据库。"""

from uuid import uuid4

from fastapi.testclient import TestClient
from sqlalchemy import delete, select
from sqlalchemy.orm import Session

from backend.app import app
from backend.db import LoginRecord, PasswordRecovery, User, now
from backend.tests.test_access_licenses import root_account
from backend.tests.test_workflows import classroom, paired, user

PASSWORD = "CorrectPass!123"
NEW_PASSWORD = "Recovered-Only!2026"


def signin(c, login, client_id="browser-main", password=PASSWORD):
    return c.post("/auth/login", json={"login": login, "password": password, "clientId": client_id})


def test_freeze_recovery_and_classroom_continuity(client):
    c, engine = client
    root = root_account(c, engine)
    teacher, uid = user(c, "frozen_teacher")
    cid = classroom(c, teacher)
    device_id, screen, _ = paired(c, teacher, cid)
    control = c.post(
        f"/devices/{device_id}/commands",
        headers={**teacher, "Idempotency-Key": str(uuid4())},
        json={"action": "sync", "expectedControlRevision": 0},
    )
    assert control.status_code == 202
    path = f"/system/users/{uid}"
    assert c.post(f"{path}/freeze", headers=teacher, json={"reason": "异常登录"}).status_code == 403
    root_id = c.get("/bootstrap", headers=root).json()["user"]["id"]
    assert c.post(f"/system/users/{root_id}/freeze", headers=root, json={"reason": "误点本人"}).status_code == 409
    stale = c.post(f"{path}/recovery-code", headers=root).json()["code"]
    frozen = c.post(f"{path}/freeze", headers=root, json={"minutes": 30, "reason": "需核实本人登录"})
    assert frozen.status_code == 200
    assert 1790 < frozen.json()["frozenUntil"] - now() <= 1800
    assert c.get("/bootstrap", headers=teacher).json()["detail"]["code"] == "account_frozen"
    assert signin(c, "frozen_teacher").json()["detail"]["code"] == "account_frozen"
    assert signin(c, "frozen_teacher", password="Incorrect-Password").status_code == 401
    assert c.get("/system/users?status=frozen", headers=root).json()["total"] == 1
    snapshot = c.get("/device/snapshot", headers=screen)
    assert snapshot.status_code == 200 and snapshot.json()["commands"] == []
    assert (
        c.post(
            "/auth/recover-password", json={"login": "frozen_teacher", "code": stale, "password": NEW_PASSWORD}
        ).status_code
        == 400
    )
    reset = c.post(f"{path}/recovery-code", headers=root).json()
    # 发码不会直接解冻，只有持码者成功设置新密码才恢复登录。
    assert signin(c, "frozen_teacher").status_code == 403
    payload = {"login": "frozen_teacher", "code": reset["code"], "password": NEW_PASSWORD}
    assert c.post("/auth/recover-password", json=payload).status_code == 200
    assert c.post("/auth/recover-password", json=payload).status_code == 400
    assert c.get("/bootstrap", headers=teacher).status_code == 401
    assert signin(c, "frozen_teacher").status_code == 401
    assert signin(c, "frozen_teacher", password=NEW_PASSWORD).status_code == 200
    assert c.get("/system/users?status=frozen", headers=root).json()["total"] == 0


def test_single_session_unfreeze_and_time_expiry(client):
    c, engine = client
    root = root_account(c, engine)
    previous, uid = user(c, "session_teacher")
    current = signin(c, "session_teacher").json()["token"]
    active = {"Authorization": f"Bearer {current}"}
    assert c.get("/bootstrap", headers=previous).json()["detail"]["code"] == "session_replaced"
    assert c.get("/bootstrap", headers=active).status_code == 200
    path = f"/system/users/{uid}"
    c.post(f"{path}/freeze", headers=root, json={"reason": "本人确认中"})
    assert c.post(f"{path}/unfreeze", headers=root).status_code == 200
    assert c.get("/bootstrap", headers=active).status_code == 401
    current = signin(c, "session_teacher").json()["token"]
    c.post(f"{path}/freeze", headers=root, json={"reason": "到期恢复检查"})
    with Session(engine) as db:
        db.get(User, uid).frozen_until = now() - 1
        db.commit()
    assert c.get("/bootstrap", headers={"Authorization": f"Bearer {current}"}).status_code == 401
    assert signin(c, "session_teacher").status_code == 200


def test_risk_requires_successful_multi_browser_multi_network_logins(client):
    c, engine = client
    _, uid = user(c, "risk_teacher")
    peers = [TestClient(app, client=(address, 50000)) for address in ("198.51.100.10", "203.0.113.10")]
    try:
        # 同一浏览器连续换网络不能触发冻结。
        for i in range(8):
            assert signin(peers[i % 2], "risk_teacher").status_code == 200
        with Session(engine) as db:
            db.execute(delete(LoginRecord).where(LoginRecord.user_id == uid))
            db.commit()
        for i in range(3):
            assert signin(peers[i % 2], "risk_teacher", f"other-{i}", "Incorrect-Password").status_code == 401
        with Session(engine) as db:
            assert db.scalar(select(LoginRecord).where(LoginRecord.user_id == uid)) is None
        for i in range(6):
            result = signin(peers[i % 2], "risk_teacher", f"browser-{i % 3}")
            assert result.status_code == (403 if i == 5 else 200), result.text
        with Session(engine) as db:
            assert db.get(User, uid).frozen_until > now()
        assert signin(c, "risk_teacher").json()["detail"]["code"] == "account_frozen"
    finally:
        for peer in peers:
            peer.close()


def test_recovery_attempts_expiry_and_disabled_account(client):
    c, engine = client
    root = root_account(c, engine)
    _, uid = user(c, "recovery_teacher")
    path = f"/system/users/{uid}/recovery-code"
    issued = c.post(path, headers=root).json()
    payload = {"login": "recovery_teacher", "code": "INCORRECT-CODE", "password": NEW_PASSWORD}
    for _ in range(5):
        assert c.post("/auth/recover-password", json=payload).status_code == 400
    payload["code"] = issued["code"]
    assert c.post("/auth/recover-password", json=payload).status_code == 400
    payload["code"] = c.post(path, headers=root).json()["code"]
    with Session(engine) as db:
        db.get(PasswordRecovery, uid).expires = now() - 1
        db.commit()
    assert c.post("/auth/recover-password", json=payload).status_code == 400
    payload["code"] = c.post(path, headers=root).json()["code"]
    with Session(engine) as db:
        db.get(User, uid).active = False
        db.commit()
    assert c.post("/auth/recover-password", json=payload).status_code == 400
    assert c.post(path, headers=root).status_code == 409
