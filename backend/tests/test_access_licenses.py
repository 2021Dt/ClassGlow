from uuid import uuid4

from sqlalchemy.orm import Session

from backend.db import AccountActivation, FeatureLicense, SystemConfig, User, now
from backend.schemas import Credentials
from backend.tests.test_workflows import classroom, draft, paired, publish, user
from scripts.admin_account import initialize

PASSWORD = "For-QA-only!2026"


def test_console_admin_and_direct_teacher_login(client):
    c, engine = client
    with Session(engine) as db:
        db.get(SystemConfig, 1).initialized = False
        initialize(db, Credentials(login="console_admin", password=PASSWORD, name="验收管理员"))
        db.commit()
    assert c.post("/setup/initialize", json={}).status_code == 404
    assert c.post("/auth/register", json={"login": "public_signup", "password": PASSWORD}).status_code == 403
    signed = c.post("/auth/login", json={"login": "console_admin", "password": PASSWORD})
    root = {"Authorization": f"Bearer {signed.json()['token']}"}
    head = c.post(
        "/system/accounts",
        headers=root,
        json={
            "login": "direct_head",
            "name": "班主任",
            "password": PASSWORD,
            "identity": "headteacher",
        },
    )
    assert head.status_code == 201 and "activationCode" not in head.json()
    signed = c.post("/auth/login", json={"login": "direct_head", "password": PASSWORD})
    assert signed.status_code == 200 and signed.json()["user"]["canCreateClasses"]
    head_headers = {"Authorization": f"Bearer {signed.json()['token']}"}
    cid = classroom(c, head_headers)
    teacher = {
        "login": "direct_teacher",
        "name": "科任老师",
        "password": PASSWORD,
        "identity": "teacher",
        "classes": [{"classId": cid, "subjects": ["数学"]}],
    }
    assert c.post("/system/accounts", headers=head_headers, json=teacher).status_code == 403
    assert c.post("/system/accounts", headers=root, json=teacher).status_code == 201
    signed = c.post("/auth/login", json={"login": "direct_teacher", "password": PASSWORD})
    assert signed.status_code == 200
    assert signed.json()["user"]["activated"] and not signed.json()["user"]["canCreateClasses"]


def root_account(c, engine):
    headers, identity = user(c, "school_admin")
    with Session(engine) as db:
        db.get(User, identity).system_admin = True
        db.commit()
    return headers


def invite(c, root, login, identity="headteacher", classes=None):
    r = c.post(
        "/system/account-invitations",
        headers=root,
        json={
            "login": login,
            "name": login,
            "identity": identity,
            "classes": classes or [],
        },
    )
    assert r.status_code == 201, r.text
    return r.json()


def activate(c, entry):
    r = c.post(
        "/auth/activate",
        json={"login": entry["login"], "password": PASSWORD, "activationCode": entry["activationCode"]},
    )
    assert r.status_code == 200, r.text
    return {"Authorization": f"Bearer {r.json()['token']}"}, r.json()["user"]["id"]


def license_code(c, root, user_id):
    r = c.post("/system/licenses", headers=root, json={"userId": user_id, "days": 30})
    assert r.status_code == 201, r.text
    return r.json()


def test_activation_hierarchy_license_binding_and_renewal(client):
    c, engine = client
    root = root_account(c, engine)
    head, hid = activate(c, invite(c, root, "headteacher"))
    cid = classroom(c, head)
    second = classroom(c, head, "另一个班")
    invitation = invite(
        c,
        root,
        "math_teacher",
        "teacher",
        [
            {"classId": cid, "subjects": ["数学"]},
            {"classId": second, "subjects": ["数学"]},
        ],
    )
    pending = c.get("/system/users?search=math_teacher", headers=root).json()["items"][0]
    assert not pending["activated"]
    replacement = c.post(f"/system/users/{pending['id']}/activation", headers=root).json()
    assert (
        c.post(
            "/auth/activate",
            json={"login": invitation["login"], "password": PASSWORD, "activationCode": invitation["activationCode"]},
        ).status_code
        == 400
    )
    teacher, tid = activate(c, replacement)
    assert (
        c.post(
            "/auth/activate",
            json={"login": replacement["login"], "password": PASSWORD, "activationCode": replacement["activationCode"]},
        ).status_code
        == 400
    )
    data = c.get("/bootstrap", headers=teacher).json()
    assert not data["user"]["canCreateClasses"] and not data["user"]["premiumActive"]
    assert {entry["role"] for entry in data["classes"]} == {"teacher"}
    assert data["classes"][0]["authorizedSubjects"] == ["数学"]
    assert c.get("/system/users", headers=head).status_code == 403
    assert c.post("/system/licenses", headers=head, json={"userId": tid, "days": 365}).status_code == 403
    assert c.post("/classes", headers=teacher, json={"name": "越权建班"}).status_code == 403
    batch = draft(c, teacher, [cid, second])
    assert publish(c, teacher, batch).status_code == 403
    code = license_code(c, root, tid)
    assert c.post("/account/license/redeem", headers=head, json={"code": code["code"]}).status_code == 400
    first = c.post("/account/license/redeem", headers=teacher, json={"code": code["code"]}).json()
    repeat = c.post("/account/license/redeem", headers=teacher, json={"code": code["code"]}).json()
    assert first["premiumActive"] and first["premiumUntil"] == repeat["premiumUntil"]
    assert publish(c, teacher, batch).status_code == 200
    layout = c.get("/bootstrap", headers=teacher).json()["classes"][0]["layout"]
    assert c.put(f"/classes/{cid}/layout", headers=teacher, json=layout).status_code == 403
    # 最高管理员无需加入成员表即可管理班级；许可不会把普通教师升级成管理者。
    assert len(c.get("/bootstrap", headers=root).json()["classes"]) == 2
    assert c.get(f"/classes/{cid}/members", headers=root).status_code == 200
    renewal = license_code(c, root, tid)
    renewed = c.post("/account/license/redeem", headers=teacher, json={"code": renewal["code"]}).json()
    assert renewed["premiumUntil"] == first["premiumUntil"] + 30 * 86400
    cancelled = license_code(c, root, hid)
    assert c.post(f"/system/licenses/{cancelled['id']}/revoke", headers=root).status_code == 200
    assert c.post("/account/license/redeem", headers=head, json={"code": cancelled["code"]}).status_code == 400
    with Session(engine) as db:
        assert not hasattr(db.get(FeatureLicense, code["id"]), "code")
        assert (
            db.query(AccountActivation).filter(AccountActivation.user_id == tid, AccountActivation.used_at > 0).count()
            == 1
        )


def test_expiry_preserves_free_features_and_downgrades_display(client):
    c, engine = client
    root = root_account(c, engine)
    head, hid = activate(c, invite(c, root, "class_owner"))
    cid = classroom(c, head)
    did, device, _ = paired(c, head, cid)
    layout = c.get("/bootstrap", headers=head).json()["classes"][0]["layout"]
    layout.update(backgroundMode="color", backgroundColor="#334455")
    assert c.put(f"/classes/{cid}/layout", headers=head, json=layout).status_code == 403
    code = license_code(c, root, hid)
    assert c.post("/account/license/redeem", headers=head, json={"code": code["code"]}).status_code == 200
    assert c.put(f"/classes/{cid}/layout", headers=head, json=layout).status_code == 200
    old = c.get("/device/snapshot", headers=device)
    assert old.json()["classroom"]["layout"]["backgroundMode"] == "color"
    assert old.json()["classroom"]["premiumUntil"] > now()
    command = c.post(
        f"/devices/{did}/commands",
        headers={**head, "Idempotency-Key": str(uuid4())},
        json={"action": "pause", "expectedControlRevision": 0},
    )
    assert command.status_code == 202
    with Session(engine) as db:
        db.get(User, hid).premium_until = now() - 1
        db.commit()
    changed = c.get("/device/snapshot", headers={**device, "If-None-Match": old.headers["ETag"]})
    assert changed.status_code == 200 and changed.headers["ETag"] != old.headers["ETag"]
    assert changed.json()["classroom"]["layout"]["backgroundMode"] == "mica"
    assert changed.json()["commands"] == []
    stored = c.get("/bootstrap", headers=head).json()["classes"][0]
    assert stored["layout"]["backgroundColor"] == "#334455" and not stored["premiumActive"]
    assert publish(c, head, draft(c, head, [cid])).status_code == 200
    assert (
        c.post(
            f"/devices/{did}/commands",
            headers={**head, "Idempotency-Key": str(uuid4())},
            json={"action": "sync", "expectedControlRevision": 1},
        ).status_code
        == 403
    )
    with Session(engine) as db:
        db.get(User, hid).expires_at = now() - 1
        db.commit()
    assert c.get("/bootstrap", headers=head).status_code == 401
    assert c.post("/auth/login", json={"login": "class_owner", "password": PASSWORD}).status_code == 403
    assert c.get("/device/snapshot", headers=device).status_code == 200
    assert (
        c.put(
            f"/system/users/{hid}",
            headers=root,
            json={"name": "class_owner", "active": True, "systemAdmin": False, "expiresAt": 0},
        ).status_code
        == 200
    )
    assert c.post("/auth/login", json={"login": "class_owner", "password": PASSWORD}).status_code == 200
