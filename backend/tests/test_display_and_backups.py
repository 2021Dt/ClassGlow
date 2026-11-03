"""只覆盖本次新增的授权边界、回传时序和文件删除，使用隔离数据库。"""

import base64
from io import BytesIO

from PIL import Image
from sqlalchemy.orm import Session

from backend.config import settings
from backend.db import Device, DeviceDisplay, Membership, User
from backend.tests.test_workflows import classroom, paired, report, user


def test_backup_details_and_delete_are_admin_only(client, tmp_path, monkeypatch):
    c, engine = client
    monkeypatch.setattr(settings, "storage_dir", tmp_path)
    admin, aid = user(c, "administrator")
    teacher, _ = user(c, "teacher")
    with Session(engine) as db:
        db.get(User, aid).system_admin = True
        db.commit()
    classroom(c, admin)
    created = c.post("/system/backups", headers=admin)
    assert created.status_code == 201, created.text
    name = created.json()["name"]
    assert "manual" in name
    details = c.get(f"/system/backups/{name}", headers=admin).json()
    assert {item["label"]: item["count"] for item in details["counts"]}["账号"] == 2
    assert c.delete(f"/system/backups/{name}", headers=teacher).status_code == 403
    assert c.delete(f"/system/backups/{name}", headers=admin).status_code == 200
    assert not (tmp_path / "backups" / name).exists()
    assert c.get("/bootstrap", headers=teacher).status_code == 200
    assert c.get(f"/system/backups/{name}/download", headers=admin).status_code == 404
    assert c.delete("/system/backups/backup-missing.db", headers=admin).status_code == 404


def test_display_is_shared_in_class_but_rejects_stale_reports(client):
    c, engine = client
    owner, oid = user(c)
    colleague, uid = user(c, "colleague")
    outsider, _ = user(c, "outsider")
    cid = classroom(c, owner)
    did, dh, identity = paired(c, owner, cid)
    with Session(engine) as db:
        db.add(Membership(class_id=cid, user_id=uid, role="teacher", subjects=["数学"]))
        db.get(User, oid).premium_until = 0
        db.get(User, uid).premium_until = 0
        db.commit()
    payload = report(identity, clientVersion="0.4.6", appliedVersion=1, applyState="applied")
    assert c.post("/device/heartbeat", headers=dh, json=payload).status_code == 200
    snapshot = c.get("/device/snapshot", headers=dh)
    request = c.post(f"/devices/{did}/display-request", headers=owner).json()
    assert request["pending"]
    assert c.post(f"/devices/{did}/display-request", headers=colleague).json()["requestId"] == request["requestId"]
    assert c.get(f"/devices/{did}/display", headers=outsider).status_code == 403
    assert c.post(f"/devices/{did}/display-request", headers=outsider).status_code == 403
    pending = c.get("/device/snapshot", headers={**dh, "If-None-Match": snapshot.headers["ETag"]})
    assert pending.status_code == 200 and pending.json()["displayRequest"] == request["requestId"]
    stream = BytesIO()
    Image.new("RGB", (320, 180), "white").save(stream, format="JPEG")
    upload = {
        **payload,
        "requestId": request["requestId"],
        "availability": "displayed",
        "imageBase64": base64.b64encode(stream.getvalue()).decode(),
    }
    assert not c.post("/device/display", headers=dh, json=upload).json()["accepted"]
    upload["stateReportSeq"] = 2
    assert c.post("/device/display", headers=dh, json={**upload, "imageBase64": "invalid"}).status_code == 422
    assert c.post("/device/display", headers=dh, json=upload).json()["accepted"]
    result = c.get(f"/devices/{did}/display", headers=colleague).json()
    assert not result["pending"] and result["image"].startswith("data:image/jpeg;base64,")
    assert result["report"]["appliedVersion"] == result["classVersion"]
    assert "image" not in c.get(f"/devices/{did}/display?after={result['receivedAt']}", headers=owner).json()
    with Session(engine) as db:
        assert "imageBase64" not in db.get(Device, did).state
    assert c.post(f"/devices/{did}/revoke", headers=owner).status_code == 200
    assert c.get(f"/devices/{did}/display", headers=colleague).status_code == 409
    with Session(engine) as db:
        assert db.get(DeviceDisplay, did) is None
