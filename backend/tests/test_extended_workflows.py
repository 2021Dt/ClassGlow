import sqlite3
from datetime import datetime, timedelta
from pathlib import Path

from sqlalchemy.orm import Session

from backend.db import Membership, Meta
from backend.tests.test_workflows import classroom, command, draft, paired, publish, report, user
from scripts.sqlite_backup import backup, restore


def test_todo_priority_limit_and_teacher_authority(client):
    c, engine = client
    h, _ = user(c)
    teacher, tid = user(c, "second")
    cid = classroom(c, h)
    with Session(engine) as db:
        db.add(Membership(class_id=cid, user_id=tid, role="teacher", subjects=["数学"]))
        db.commit()
    denied = draft(c, teacher, [cid], kind="todo", priority=True)
    assert publish(c, teacher, denied).status_code == 403
    for _ in range(3):
        assert publish(c, h, draft(c, h, [cid], kind="todo", subject="公共提醒", priority=True)).status_code == 200
    fourth = draft(c, h, [cid], kind="todo", subject="公共提醒", priority=True)
    assert publish(c, h, fourth).status_code == 422


def test_grid_overlap_and_bounds_refused_without_changing_version(client):
    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    layout = c.get("/classes", headers=h).json()[0]["layout"]
    layout["grid"]["todo"]["x"] = 0
    assert c.put(f"/classes/{cid}/layout", headers=h, json=layout).status_code == 422
    assert c.get("/classes", headers=h).json()[0]["version"] == 1
    layout["grid"]["todo"]["x"] = 7
    layout["grid"]["todo"]["w"] = 6
    assert c.put(f"/classes/{cid}/layout", headers=h, json=layout).status_code == 422


def test_future_template_preserves_previous_interval_and_custom_override(client):
    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    from zoneinfo import ZoneInfo

    today = datetime.now(ZoneInfo("Asia/Shanghai")).date()
    a = {
        "revision": 1,
        "effectiveFrom": today.isoformat(),
        "effectiveUntil": (today + timedelta(days=120)).isoformat(),
        "weekdays": {"1": [{"start": "08:00", "end": "08:40", "subject": "数学"}]},
    }
    assert c.put(f"/classes/{cid}/timetable", headers=h, json=a).status_code == 200
    b = {**a, "revision": 2, "effectiveFrom": (today + timedelta(days=1)).isoformat()}
    assert c.put(f"/classes/{cid}/timetable", headers=h, json=b).status_code == 200
    cls = c.get("/classes", headers=h).json()[0]
    assert len(cls["templates"]) == 2 and cls["templates"][0]["effectiveUntil"] == today.isoformat()
    r = c.put(
        f"/classes/{cid}/overrides",
        headers=h,
        json={
            "revision": 3,
            "date": today.isoformat(),
            "mode": "custom",
            "periods": [{"start": "09:00", "end": "09:40", "subject": "语文"}],
        },
    )
    assert r.status_code == 200
    assert (
        c.request(
            "DELETE", f"/classes/{cid}/overrides/{today.isoformat()}", headers=h, json={"revision": 4}
        ).status_code
        == 200
    )


def test_records_filters_and_pagination(client):
    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    for i in range(3):
        publish(c, h, draft(c, h, [cid], f"数学记录{i}"))
    result = c.get("/contents", headers=h, params={"classId": cid, "limit": 2, "search": "数学"}).json()
    assert result["total"] == 3 and len(result["items"]) == 2
    second = c.get("/contents", headers=h, params={"classId": cid, "limit": 2, "offset": 2}).json()
    assert len(second["items"]) == 1
    assert c.get("/contents", headers=h, params={"classId": cid, "limit": 101}).status_code == 422


def test_sqlite_isolated_restore_rotates_epoch_and_preserves_business(client, tmp_path):
    c, engine = client
    h, _ = user(c)
    cid = classroom(c, h)
    publish(c, h, draft(c, h, [cid]))
    with Session(engine) as db:
        old_epoch = db.get(Meta, "sync_epoch").value
    source = Path(engine.url.database)
    saved = tmp_path / "backup.db"
    target = tmp_path / "restored.db"
    backup(source, saved)
    restore(saved, target)
    with sqlite3.connect(target) as db:
        assert db.execute("SELECT count(*) FROM contents").fetchone()[0] == 1
        assert db.execute("SELECT value FROM metadata WHERE key='sync_epoch'").fetchone()[0] != old_epoch
        assert db.execute("SELECT count(*) FROM sessions").fetchone()[0] == 0


def test_late_deferred_receipt_does_not_revive_superseded_control(client):
    from uuid import uuid4

    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    did, dh, identity = paired(c, h, cid)
    key = str(uuid4())
    first = c.post(
        f"/devices/{did}/commands",
        headers={**h, "Idempotency-Key": key},
        json={"action": "pause", "expectedControlRevision": 0},
    ).json()
    assert command(c, h, did, "resume", 1).status_code == 202
    late = {**report(identity), "commandId": first["id"], "result": "deferred"}
    assert c.post("/device/command-acks", headers=dh, json=late).status_code == 200
    previous = c.get(f"/devices/{did}/commands/by-key/{key}", headers=h)
    assert previous.status_code == 200 and previous.json()["state"] == "superseded"
    assert {v["action"] for v in c.get("/device/snapshot", headers=dh).json()["commands"]} == {"resume"}


def test_class_subjects_and_teacher_authority_are_distinct(client):
    c, engine = client
    owner, _ = user(c)
    teacher, tid = user(c, "second")
    cid = classroom(c, owner)
    with Session(engine) as db:
        db.add(Membership(class_id=cid, user_id=tid, role="teacher", subjects=["数学"]))
        db.commit()
    data = c.get("/classes", headers=teacher).json()[0]
    assert "语文" in data["subjects"] and data["authorizedSubjects"] == ["数学"]
    assert publish(c, teacher, draft(c, teacher, [cid], subject="语文")).status_code == 403
