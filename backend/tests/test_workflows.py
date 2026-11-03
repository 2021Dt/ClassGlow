from datetime import UTC, datetime, timedelta
from uuid import uuid4

from sqlalchemy.orm import Session

from backend.db import Device, Membership


def user(c, name="teacher"):
    r = c.post("/auth/register", json={"login": name, "name": name, "password": "CorrectPass!123"})
    assert r.status_code == 201, r.text
    data = r.json()
    return {"Authorization": f"Bearer {data['token']}"}, data["user"]["id"]


def classroom(c, h, name="五年级二班"):
    r = c.post("/classes", headers=h, json={"name": name})
    assert r.status_code == 201, r.text
    return r.json()["id"]


def draft(c, h, ids, text="最后一句也要完整保存", **extra):
    start = datetime.now(UTC)
    body = {
        "id": str(uuid4()),
        "kind": "homework",
        "classIds": ids,
        "subject": "数学",
        "body": text,
        "from": start.isoformat(),
        "until": (start + timedelta(days=1)).isoformat(),
        "revision": 0,
        **extra,
    }
    r = c.put(f"/drafts/{body['id']}", headers=h, json=body)
    assert r.status_code == 200, r.text
    return r.json()


def publish(c, h, d, key=None):
    return c.post(
        "/publish-batches",
        headers={**h, "Idempotency-Key": key or str(uuid4())},
        json={"draftId": d["id"], "draftRevision": d["revision"]},
    )


def paired(c, h, class_id):
    p = c.post("/device-pairings", json={"name": "测试设备"}).json()
    r = c.post("/device-pairings/confirm", headers=h, json={"code": p["code"], "classId": class_id})
    assert r.status_code == 200, r.text
    dh = {"Authorization": f"Bearer {p['secret']}"}
    identity = c.get(f"/device-pairings/{p['id']}", headers=dh).json()
    return r.json()["id"], dh, identity


def report(identity, **extra):
    return {
        "bindingId": identity["bindingId"],
        "syncEpoch": identity["syncEpoch"],
        "stateReportSeq": 1,
        "localControlEpoch": 0,
        "localPaused": False,
        "remotePaused": False,
        "cachedVersion": 1,
        "appliedVersion": 0,
        "applyState": "waiting",
        "pageSetId": "page-set",
        "page": 1,
        "pageCount": 3,
        "effectiveTheme": "mica_light",
        "effectiveMaterial": "solid",
        **extra,
    }


def command(c, h, did, action, revision=0, **extra):
    return c.post(
        f"/devices/{did}/commands",
        headers={**h, "Idempotency-Key": str(uuid4())},
        json={"action": action, "expectedControlRevision": revision, **extra},
    )


def test_cloud_draft_private_and_device_snapshot_excludes_input(client):
    c, _ = client
    h, _ = user(c)
    other, _ = user(c, "second")
    cid = classroom(c, h)
    d = draft(c, h, [cid])
    _, dh, _ = paired(c, h, cid)
    assert c.get("/drafts", headers=other).json() == []
    assert c.get("/device/snapshot", headers=dh).json()["contents"] == []
    assert c.put(f"/drafts/{d['id']}", headers=other, json=d).status_code == 404
    assert c.post("/classes", headers=dh, json={"name": "恶意写入"}).status_code == 401


def test_publish_final_input_and_idempotency(client):
    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    d = draft(c, h, [cid], "第一句")
    old_revision = d["revision"]
    d["body"] += "\n最后一句"
    d = c.put(f"/drafts/{d['id']}", headers=h, json=d).json()
    assert publish(c, h, {**d, "revision": old_revision}).status_code == 409
    key = str(uuid4())
    first = publish(c, h, d, key)
    assert first.status_code == 200, first.text
    assert publish(c, h, d, key).json() == first.json()
    data = c.get("/bootstrap", headers=h).json()
    assert len(data["contents"]) == 1 and data["contents"][0]["body"] == "第一句\n最后一句"
    assert c.get(f"/publish-batches/{key}", headers=h).json() == first.json()


def test_multiclass_atomic_and_independent(client):
    c, engine = client
    owner, _ = user(c)
    teacher, teacher_id = user(c, "second")
    a, b = classroom(c, owner), classroom(c, owner, "五年级三班")
    with Session(engine) as db:
        db.add(Membership(class_id=a, user_id=teacher_id, role="teacher", subjects=["数学"]))
        db.commit()
    d = draft(c, teacher, [a, b])
    assert publish(c, teacher, d).status_code == 403
    assert c.get("/bootstrap", headers=owner).json()["contents"] == []
    both = draft(c, owner, [a, b])
    r = publish(c, owner, both)
    assert r.status_code == 200 and len(r.json()["results"]) == 2
    record_id = r.json()["results"][0]["contentId"]
    assert c.post(f"/contents/{record_id}/withdraw", headers=owner, json={"revision": 1}).status_code == 200
    assert len([v for v in c.get("/bootstrap", headers=owner).json()["contents"] if v["state"] == "published"]) == 1


def test_draft_conflict_and_official_conflict_preserve_input(client):
    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    d = draft(c, h, [cid])
    stale = dict(d)
    d["body"] = "新草稿"
    saved = c.put(f"/drafts/{d['id']}", headers=h, json=d)
    assert saved.status_code == 200
    assert c.put(f"/drafts/{d['id']}", headers=h, json=stale).status_code == 409
    content_id = publish(c, h, saved.json()).json()["results"][0]["contentId"]
    edit = draft(c, h, [cid], "保留这份修改", sourceId=content_id, baseRevision=1)
    c.post(f"/contents/{content_id}/withdraw", headers=h, json={"revision": 1})
    assert publish(c, h, edit).status_code == 409
    assert c.get("/drafts", headers=h).json()[0]["body"] == "保留这份修改"


def test_control_etag_and_local_pause(client):
    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    did, dh, identity = paired(c, h, cid)
    assert c.post("/device/heartbeat", headers=dh, json=report(identity, localPaused=True)).status_code == 200
    snap = c.get("/device/snapshot", headers=dh)
    etag = snap.headers["etag"]
    assert c.get("/device/snapshot", headers={**dh, "If-None-Match": etag}).status_code == 304
    assert command(c, h, did, "resume").status_code == 409
    assert command(c, h, did, "set_page", pageSetId="page-set", page=2).status_code == 409
    sync = command(c, h, did, "sync")
    assert sync.status_code == 202
    snap = c.get("/device/snapshot", headers={**dh, "If-None-Match": etag})
    assert snap.status_code == 200 and snap.json()["commands"][0]["action"] == "sync"
    assert c.get(f"/devices/{did}/control", headers=h).json()["device"]["localPaused"] is True


def test_stale_report_cannot_rollback_state(client):
    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    did, dh, identity = paired(c, h, cid)
    c.post(
        "/device/heartbeat", headers=dh, json=report(identity, stateReportSeq=10, localControlEpoch=2, localPaused=True)
    )
    stale = c.post("/device/heartbeat", headers=dh, json=report(identity, stateReportSeq=9))
    assert stale.status_code == 200 and stale.json()["accepted"] is False
    assert c.get(f"/devices/{did}/control", headers=h).json()["device"]["localPaused"] is True


def test_control_groups_do_not_supersede_each_other(client):
    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    did, dh, identity = paired(c, h, cid)
    c.post("/device/heartbeat", headers=dh, json=report(identity))
    pause = command(c, h, did, "pause")
    assert pause.status_code == 202
    assert command(c, h, did, "sync", 1).status_code == 202
    snapshot = c.get("/device/snapshot", headers=dh).json()
    assert {v["action"] for v in snapshot["commands"]} == {"pause", "sync"}
    assert command(c, h, did, "resume", 2).status_code == 202
    assert {v["action"] for v in c.get("/device/snapshot", headers=dh).json()["commands"]} == {"resume", "sync"}


def test_archive_restore_does_not_revive_credentials(client):
    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    _, dh, _ = paired(c, h, cid)
    assert c.post(f"/classes/{cid}/archive", headers=h).status_code == 200
    assert c.get("/device/snapshot", headers=dh).status_code == 401
    assert c.post(f"/classes/{cid}/restore", headers=h).status_code == 200
    assert c.get("/device/snapshot", headers=dh).status_code == 401


def test_layout_revision_and_timetable_validation(client):
    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    layout = c.get("/classes", headers=h).json()[0]["layout"]
    assert c.put(f"/classes/{cid}/layout", headers=h, json=layout).status_code == 200
    assert c.put(f"/classes/{cid}/layout", headers=h, json=layout).status_code == 409
    overlap = {
        "revision": 1,
        "effectiveFrom": "2026-11-01",
        "effectiveUntil": "2027-02-01",
        "weekdays": {
            "1": [
                {"start": "08:00", "end": "09:00", "subject": "数学"},
                {"start": "08:30", "end": "09:30", "subject": "语文"},
            ]
        },
    }
    assert c.put(f"/classes/{cid}/timetable", headers=h, json=overlap).status_code == 422


def test_invite_approval_consumes_once_and_export_no_credentials(client):
    c, _ = client
    h, _ = user(c)
    other, _ = user(c, "second")
    cid = classroom(c, h)
    invitation = c.post(f"/classes/{cid}/invites", headers=h, json={"subjects": ["数学"]}).json()
    token = invitation["url"].split("invite=")[1]
    assert c.post(f"/invites/{token}/apply", headers=other).status_code == 200
    path = f"/classes/{cid}/invites/{invitation['id']}/approve"
    assert c.post(path, headers=h).status_code == 200
    assert c.post(path, headers=h).status_code == 409
    assert c.get(f"/classes/{cid}/export", headers=other).status_code == 403
    text = c.get(f"/classes/{cid}/export", headers=h).text
    assert "credential" not in text and "password" not in text and "token" not in text


def test_offline_immediate_control_rejected(client):
    c, engine = client
    h, _ = user(c)
    cid = classroom(c, h)
    did, _, _ = paired(c, h, cid)
    with Session(engine) as db:
        d = db.get(Device, did)
        d.last_seen = 0
        db.commit()
    assert command(c, h, did, "pause").status_code == 409
    assert publish(c, h, draft(c, h, [cid])).status_code == 200


def test_invalid_inputs_do_not_echo_password(client):
    c, _ = client
    r = c.post("/auth/register", json={"login": "bad", "password": "secret"})
    assert r.status_code == 422 and "secret" not in r.text


def test_teacher_cannot_edit_same_subject_other_author_and_admin_reason_required(client):
    c, engine = client
    h, _ = user(c)
    teacher, tid = user(c, "second")
    cid = classroom(c, h)
    with Session(engine) as db:
        db.add(Membership(class_id=cid, user_id=tid, role="teacher", subjects=["数学"]))
        db.commit()
    own = publish(c, h, draft(c, h, [cid])).json()["results"][0]["contentId"]
    edit = draft(c, teacher, [cid], sourceId=own, baseRevision=1)
    assert publish(c, teacher, edit).status_code == 403
    second_content = publish(c, teacher, draft(c, teacher, [cid])).json()["results"][0]["contentId"]
    admin_edit = draft(c, h, [cid], sourceId=second_content, baseRevision=1)
    assert publish(c, h, admin_edit).status_code == 403
    admin_edit["reason"] = "修正页码"
    admin_edit = c.put(f"/drafts/{admin_edit['id']}", headers=h, json=admin_edit).json()
    assert publish(c, h, admin_edit).status_code == 200
    assert c.get(f"/contents/{second_content}/history", headers=h).json()[0]["reason"] == "修正页码"


def test_pending_command_expires_and_late_receipt_cannot_rollback_latest_state(client):
    from backend.db import Command, now

    c, engine = client
    h, _ = user(c)
    cid = classroom(c, h)
    did, dh, identity = paired(c, h, cid)
    first = command(c, h, did, "sync").json()
    with Session(engine) as db:
        cmd = db.get(Command, first["id"])
        cmd.expires = now() - 1
        db.commit()
    assert c.get("/device/snapshot", headers=dh).json()["commands"] == []
    assert c.get(f"/devices/{did}/control", headers=h).json()["commands"][0]["state"] == "expired"
    c.post(
        "/device/heartbeat", headers=dh, json=report(identity, stateReportSeq=5, localControlEpoch=2, localPaused=True)
    )
    stale_ack = {**report(identity), "commandId": first["id"], "result": "rejected"}
    assert c.post("/device/command-acks", headers=dh, json=stale_ack).status_code == 200
    assert c.get(f"/devices/{did}/control", headers=h).json()["device"]["localPaused"]


def test_restore_expired_todo_requires_new_end(client):
    c, _ = client
    h, _ = user(c)
    cid = classroom(c, h)
    start = datetime.now(UTC) - timedelta(days=2)
    d = draft(
        c,
        h,
        [cid],
        "已过展示期的待办",
        kind="todo",
        subject="公共提醒",
        **{"from": start.isoformat(), "until": (start + timedelta(days=1)).isoformat()},
    )
    content_id = publish(c, h, d).json()["results"][0]["contentId"]
    assert c.post(f"/contents/{content_id}/complete", headers=h, json={"revision": 1}).status_code == 200
    assert c.post(f"/contents/{content_id}/restore", headers=h, json={"revision": 2}).status_code == 422
    assert (
        c.post(
            f"/contents/{content_id}/restore",
            headers=h,
            json={"revision": 2, "until": (datetime.now(UTC) + timedelta(days=1)).isoformat()},
        ).status_code
        == 200
    )


def test_ownership_transfer_accept_once(client):
    c, engine = client
    owner, _ = user(c)
    other, tid = user(c, "second")
    cid = classroom(c, owner)
    with Session(engine) as db:
        db.add(Membership(class_id=cid, user_id=tid, role="teacher", subjects=["数学"]))
        db.commit()
    request = c.post(f"/classes/{cid}/transfers", headers=owner, json={"recipient": tid}).json()
    path = f"/classes/{cid}/transfers/{request['id']}/accept"
    assert c.post(path, headers=other).status_code == 200
    assert c.post(path, headers=other).status_code == 409
    assert c.get("/classes", headers=owner).json()[0]["role"] == "admin"
    assert c.get("/classes", headers=other).json()[0]["role"] == "owner"


def test_oversized_stream_rejected_before_auth(client):
    c, _ = client
    response = c.post(
        "/auth/register", content=(b"x" * 70000 for _ in range(2)), headers={"Content-Type": "application/json"}
    )
    assert response.status_code == 413
