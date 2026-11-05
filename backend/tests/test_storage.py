"""删除的外键完整性、重试、防误删以及 30 天保留边界。"""

import os
from uuid import uuid4

import pytest
from sqlalchemy import event, select
from sqlalchemy.orm import Session

from backend.config import settings
from backend.db import (
    AccountActivation,
    Announcement,
    AnnouncementReceipt,
    AnnouncementTarget,
    Audit,
    Classroom,
    Command,
    Content,
    Device,
    DeviceDisplay,
    Draft,
    FeatureLicense,
    History,
    Idempotency,
    Invitation,
    Membership,
    Pairing,
    PasswordRecovery,
    Token,
    Transfer,
    User,
    now,
)
from backend.storage import clean_records
from backend.tests.test_access_licenses import root_account
from backend.tests.test_workflows import classroom, draft, paired, publish, user


@pytest.fixture
def storage_client(client, tmp_path, monkeypatch):
    c, engine = client

    @event.listens_for(engine, "connect")
    def enforce_foreign_keys(connection, _):
        connection.execute("PRAGMA foreign_keys=ON")

    with engine.connect() as connection:
        connection.exec_driver_sql("PRAGMA foreign_keys=ON")
    monkeypatch.setattr(settings, "storage_dir", tmp_path)
    return c, engine, root_account(c, engine)


def remove(c, root, kind, resource_id, name):
    return c.request("DELETE", f"/system/deletion/{kind}/{resource_id}", headers=root, json={"confirmation": name})


def test_class_delete_preserves_other_class_and_shared_draft(storage_client):
    c, engine, root = storage_client
    owner, uid = user(c)
    cid = classroom(c, owner, "毕业班")
    keep = classroom(c, owner, "在读班")
    did, device_headers, _ = paired(c, owner, cid)
    homework = draft(c, owner, [cid])
    published = publish(c, owner, homework).json()["results"][0]["contentId"]
    mixed = draft(c, owner, [cid, keep])
    only = draft(c, owner, [cid])
    with Session(engine) as db:
        db.add(DeviceDisplay(device_id=did, image=b"preview"))
        db.add(
            Invitation(
                class_id=cid, digest="invitation", subjects=[], state="pending", applicant=uid, expires=now() + 60
            )
        )
        db.add(Transfer(class_id=cid, sender=uid, recipient=uid, expires=now() + 60))
        db.commit()
    assert remove(c, owner, "classes", cid, "毕业班").status_code == 403
    assert remove(c, root, "classes", cid, "毕业班").status_code == 409
    assert c.post(f"/classes/{cid}/archive", headers=owner).status_code == 200
    assert remove(c, root, "classes", cid, "wrong").status_code == 422
    preview = c.get(f"/system/deletion/classes/{cid}", headers=root).json()
    assert not preview["blockedReason"] and preview["counts"][0]["count"] == 1
    deleted = remove(c, root, "classes", cid, "毕业班")
    assert deleted.status_code == 200, deleted.text
    assert remove(c, root, "classes", cid, "毕业班").status_code == 200
    with Session(engine) as db:
        assert db.get(Classroom, cid) is None and db.get(Classroom, keep)
        assert db.get(Content, published) is None and db.get(Device, did) is None
        assert db.get(Draft, mixed["id"]).data["classIds"] == [keep]
        assert not db.get(Draft, mixed["id"]).closed
        assert db.get(Draft, only["id"]).closed and db.get(Draft, only["id"]).data == {}
        assert db.scalar(select(Pairing).where(Pairing.device_id == did)) is None
        assert db.connection().exec_driver_sql("PRAGMA foreign_key_check").all() == []
    assert c.get("/device/snapshot", headers=device_headers).status_code == 401
    assert publish(c, owner, only).status_code == 409


def test_disabled_user_delete_removes_references_and_updates_display(storage_client):
    c, engine, root = storage_client
    owner, owner_id = user(c, "owner")
    teacher, teacher_id = user(c, "departed")
    cid = classroom(c, owner)
    did, _, _ = paired(c, owner, cid)
    own = publish(c, owner, draft(c, owner, [cid])).json()["results"][0]["contentId"]
    with Session(engine) as db:
        db.add(Membership(class_id=cid, user_id=teacher_id, role="teacher", subjects=["数学"]))
        db.commit()
    published = publish(c, teacher, draft(c, teacher, [cid])).json()["results"][0]["contentId"]
    with Session(engine) as db:
        db.add(History(content_id=own, revision=2, actor_id=teacher_id, before={}, after={}))
        db.add(AccountActivation(user_id=teacher_id, code_hash=uuid4().hex, epoch="test-epoch", expires=now() + 60))
        db.add(
            FeatureLicense(user_id=teacher_id, code_hash=uuid4().hex, epoch="test-epoch", days=30, expires=now() + 60)
        )
        db.add(PasswordRecovery(user_id=teacher_id, code_hash=uuid4().hex, epoch="test-epoch", expires=now() + 60))
        db.add(
            Command(
                device_id=did,
                actor_id=teacher_id,
                binding="binding",
                epoch="test-epoch",
                seq=1,
                action="sync",
                group="sync",
                expires=now() + 60,
                data={},
            )
        )
        notice = Announcement(title="公告", markdown="正文", scope="selected")
        db.add(notice)
        db.flush()
        db.add(AnnouncementTarget(announcement_id=notice.id, user_id=teacher_id))
        db.add(AnnouncementReceipt(announcement_id=notice.id, user_id=teacher_id))
        before = db.get(Classroom, cid).version
        db.commit()
    assert remove(c, root, "users", teacher_id, "departed").status_code == 409
    with Session(engine) as db:
        db.get(User, teacher_id).active = False
        db.commit()
    response = remove(c, root, "users", teacher_id, "departed")
    assert response.status_code == 200, response.text
    assert remove(c, root, "users", teacher_id, "departed").status_code == 200
    with Session(engine) as db:
        assert db.get(User, teacher_id) is None
        assert db.get(User, owner_id) and db.get(Content, own)
        assert db.get(Content, published) is None
        assert db.get(Classroom, cid).version == before + 1
        assert db.connection().exec_driver_sql("PRAGMA foreign_key_check").all() == []
        db.get(User, owner_id).active = False
        db.commit()
    assert remove(c, root, "users", owner_id, "owner").status_code == 409
    assert c.get("/bootstrap", headers=teacher).status_code == 401
    current_id = c.get("/bootstrap", headers=root).json()["user"]["id"]
    assert remove(c, root, "users", current_id, "anything").status_code == 409


def test_announcement_and_revoked_device_delete(storage_client):
    c, engine, root = storage_client
    owner, _ = user(c)
    cid = classroom(c, owner)
    did, _, _ = paired(c, owner, cid)
    assert remove(c, root, "devices", did, "测试设备").status_code == 409
    assert c.post(f"/system/devices/{did}/revoke", headers=root).status_code == 200
    assert remove(c, root, "devices", did, "测试设备").status_code == 200
    payload = {"title": "过期公告", "markdown": "正文", "scope": "all"}
    headers = {**root, "Idempotency-Key": str(uuid4())}
    notice = c.post("/system/announcements", headers=headers, json=payload).json()
    assert remove(c, root, "announcements", notice["id"], notice["title"]).status_code == 409
    c.post(f"/system/announcements/{notice['id']}/withdraw", headers=root)
    assert remove(c, root, "announcements", notice["id"], notice["title"]).status_code == 200
    assert c.post("/system/announcements", headers=headers, json=payload).status_code == 410
    with Session(engine) as db:
        assert db.scalar(select(Idempotency).where(Idempotency.result["_deleted"].as_boolean().is_(True)))
        assert db.connection().exec_driver_sql("PRAGMA foreign_key_check").all() == []


def test_retention_boundary_and_backup_safety(storage_client, tmp_path):
    c, engine, root = storage_client
    owner, uid = user(c)
    cid = classroom(c, owner)
    closed = draft(c, owner, [cid])
    opened = draft(c, owner, [cid])
    timestamp = now()
    cutoff = timestamp - 30 * 86400
    with Session(engine) as db:
        db.add_all(
            [
                Audit(class_id=cid, actor=uid, action="old", at=cutoff - 1),
                Audit(class_id=cid, actor=uid, action="boundary", at=cutoff),
            ]
        )
        db.get(Draft, closed["id"]).closed = True
        db.get(Draft, closed["id"]).updated = cutoff - 1
        db.get(Draft, opened["id"]).updated = cutoff - 1
        db.add(Token(digest="old-token", user_id=uid, expires=cutoff - 1, epoch="test-epoch"))
        db.commit()
        result = clean_records(db, timestamp)
        db.commit()
        assert result["班级操作日志"] == 1 and result["已关闭草稿正文"] == 1
        assert db.scalar(select(Audit).where(Audit.action == "boundary"))
        assert db.get(Token, "old-token") is None
        assert db.get(Draft, closed["id"]).data == {}
        assert db.get(Draft, opened["id"]).data["body"]
    assert c.get("/system/storage", headers=owner).status_code == 403
    assert c.get("/bootstrap", headers=owner).status_code == 200
    folder = tmp_path / "backups"
    folder.mkdir()
    for i in range(5):
        path = folder / f"backup-old-{i}.db"
        path.write_bytes(b"backup")
        os.utime(path, (cutoff - 100 + i, cutoff - 100 + i))
    report = c.get("/system/storage", headers=root).json()
    assert report["backups"] == {"count": 2, "bytes": 12}
    assert (
        c.post(
            "/system/storage/cleanup", headers=root, json={"category": "backups", "confirmation": "wrong"}
        ).status_code
        == 422
    )
    assert len(list(folder.iterdir())) == 5
    assert (
        c.post(
            "/system/storage/cleanup", headers=root, json={"category": "backups", "confirmation": "清理过期数据"}
        ).status_code
        == 200
    )
    assert len(list(folder.iterdir())) == 3


def test_download_deletion_protects_current_future_and_rollback(storage_client, tmp_path, monkeypatch):
    c, _engine, root = storage_client
    monkeypatch.setattr(settings, "downloads_dir", tmp_path)
    monkeypatch.setattr(settings, "client_latest_version", "0.4.12")
    monkeypatch.setattr(
        settings, "client_download_url", "https://example.test/downloads/ClassGlow-0.4.9-win-x64-setup.exe"
    )
    for version in (8, 9, 10, 11, 12, 13):
        (tmp_path / f"ClassGlow-0.4.{version}-win-x64-setup.exe").write_bytes(b"package")
    for version in (9, 11, 12, 13):
        name = f"ClassGlow-0.4.{version}-win-x64-setup.exe"
        response = c.request("DELETE", f"/system/storage/downloads/{name}", headers=root, json={"confirmation": name})
        assert response.status_code == 409 and (tmp_path / name).exists()
    name = "ClassGlow-0.4.8-win-x64-setup.exe"
    response = c.request("DELETE", f"/system/storage/downloads/{name}", headers=root, json={"confirmation": name})
    assert response.status_code == 200 and not (tmp_path / name).exists()
    assert (
        c.request("DELETE", f"/system/storage/downloads/{name}", headers=root, json={"confirmation": name}).status_code
        == 200
    )
