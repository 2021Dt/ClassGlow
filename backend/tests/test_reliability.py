from concurrent.futures import ThreadPoolExecutor
from datetime import UTC, datetime, timedelta
from threading import Barrier
from uuid import uuid4

from sqlalchemy import event, func, select
from sqlalchemy.exc import OperationalError
from sqlalchemy.orm import Session

from backend.config import settings
from backend.db import Content, User
from backend.tests.test_account_security import signin
from backend.tests.test_workflows import classroom, draft, paired, user


def test_old_history_does_not_inflate_bootstrap_or_snapshot_and_future_is_cached(client):
    c, engine = client
    headers, uid = user(c)
    cid = classroom(c, headers)
    _, device, _ = paired(c, headers, cid)
    start = datetime.now(UTC)
    with Session(engine) as db:

        def row(begin, end, body):
            return Content(
                class_id=cid,
                author_id=uid,
                state="published",
                data={
                    "kind": "homework",
                    "subject": "数学",
                    "body": body,
                    "created": begin.isoformat(),
                    "from": begin.isoformat(),
                    "until": end.isoformat(),
                    "due": "",
                    "batchId": "history",
                },
            )

        db.add_all(row(start - timedelta(days=50), start - timedelta(days=40), "历史" * 250) for _ in range(3000))
        # 当前内容不能通过简单的“前 N 条”截断；未来内容必须支持断网后到点展示。
        db.add_all(row(start - timedelta(minutes=1), start + timedelta(days=1), f"今日{i}") for i in range(120))
        db.add(row(start + timedelta(days=20), start + timedelta(days=21), "未来离线展示"))
        db.commit()
    for path, auth in (("/bootstrap", headers), ("/device/snapshot", device)):
        response = c.get(path, headers=auth)
        assert response.status_code == 200
        assert len(response.json()["contents"]) == 121
        assert any(item["body"] == "未来离线展示" for item in response.json()["contents"])
        assert len(response.content) < 100000
    history = c.get(f"/contents?classId={cid}&state=ended&limit=20", headers=headers).json()
    assert history["total"] == 3000 and len(history["items"]) == 20
    with Session(engine) as db:
        assert db.scalar(select(func.count()).select_from(Content)) == 3121


def test_read_requests_do_not_take_write_lock_and_work_during_uncommitted_write(client):
    c, engine = client
    headers, uid = user(c)
    cid = classroom(c, headers)
    statements = []

    def capture(_, __, statement, ___, ____, _____):
        statements.append(statement.upper())

    event.listen(engine, "before_cursor_execute", capture)
    try:
        with Session(engine) as writer:
            writer.connection().exec_driver_sql("BEGIN IMMEDIATE")
            writer.get(User, uid).name = "尚未提交的名字"
            writer.flush()
            statements.clear()
            assert c.get("/bootstrap", headers=headers).json()["user"]["name"] == "teacher"
            assert c.get(f"/classes/{cid}/devices", headers=headers).status_code == 200
            assert c.get("/health").status_code == 200
            assert "BEGIN IMMEDIATE" not in statements
            assert any(item == "BEGIN" for item in statements)
            writer.rollback()
    finally:
        event.remove(engine, "before_cursor_execute", capture)


def test_concurrent_publish_retry_remains_atomic(client):
    c, engine = client
    headers, _ = user(c)
    cid = classroom(c, headers)
    saved = draft(c, headers, [cid])
    key, barrier = str(uuid4()), Barrier(2)

    def publish():
        barrier.wait(timeout=5)
        return c.post(
            "/publish-batches",
            headers={**headers, "Idempotency-Key": key},
            json={"draftId": saved["id"], "draftRevision": saved["revision"]},
        )

    with ThreadPoolExecutor(max_workers=2) as pool:
        responses = list(pool.map(lambda _: publish(), range(2)))
    assert all(r.status_code == 200 for r in responses)
    assert responses[0].json() == responses[1].json()
    with Session(engine) as db:
        assert db.scalar(select(func.count()).select_from(Content)) == 1


def test_session_limits_revoke_others_and_account_isolation(client, monkeypatch):
    c, _ = client
    monkeypatch.setattr(settings, "session_limit", 3)
    headers, _ = user(c, "devices_teacher")
    tokens = [signin(c, "devices_teacher", f"browser-{i}").json()["token"] for i in range(4)]
    auth = lambda token: {"Authorization": f"Bearer {token}"}
    assert c.get("/bootstrap", headers=headers).status_code == 401
    assert c.get("/bootstrap", headers=auth(tokens[0])).status_code == 401
    assert c.get("/bootstrap", headers=auth(tokens[1])).status_code == 200
    current = auth(tokens[-1])
    listed = c.get("/account/sessions", headers=current).json()
    assert listed["limit"] == 3 and len(listed["items"]) == 3
    assert sum(item["current"] for item in listed["items"]) == 1
    assert "digest" not in str(listed) and tokens[-1] not in str(listed)
    other, _ = user(c, "different_teacher")
    target = next(item for item in listed["items"] if not item["current"])
    assert c.delete(f"/account/sessions/{target['id']}", headers=other).status_code == 404
    assert c.delete(f"/account/sessions/{target['id']}", headers=current).status_code == 200
    assert c.post("/account/sessions/logout-others", headers=current).status_code == 200
    assert len(c.get("/account/sessions", headers=current).json()["items"]) == 1
    assert c.get("/bootstrap", headers=auth(tokens[2])).status_code == 401
    assert c.get("/bootstrap", headers=current).status_code == 200


def test_commit_failure_does_not_report_success(client):
    c, engine = client
    headers, _ = user(c)
    cid = classroom(c, headers)

    def fail_commit(_):
        raise OperationalError("commit", {}, RuntimeError("unavailable"))

    event.listen(engine, "commit", fail_commit)
    try:
        start = datetime.now(UTC)
        response = c.put(
            f"/drafts/{(identity := str(uuid4()))}",
            headers=headers,
            json={
                "id": identity,
                "kind": "homework",
                "classIds": [cid],
                "subject": "数学",
                "body": "保留草稿",
                "from": start.isoformat(),
                "until": (start + timedelta(days=1)).isoformat(),
                "revision": 0,
            },
        )
    finally:
        event.remove(engine, "commit", fail_commit)
    assert response.status_code == 503
