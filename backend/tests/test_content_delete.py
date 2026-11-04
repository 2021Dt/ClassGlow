from sqlalchemy import func, select
from sqlalchemy.orm import Session

from backend.db import Classroom, Content, History, Membership
from backend.tests.test_workflows import classroom, draft, paired, publish, user


def test_delete_published_history_permissions_and_snapshot(client):
    """删除必须遵守所有权和版本检查；正文、修订与设备快照同步消失。"""
    c, engine = client
    owner, _ = user(c)
    other, other_id = user(c, "other")
    class_id = classroom(c, owner)
    with Session(engine) as db:
        db.add(Membership(class_id=class_id, user_id=other_id, role="teacher", subjects=["数学"]))
        db.commit()
    _, device, _ = paired(c, owner, class_id)
    content_id = publish(c, owner, draft(c, owner, [class_id])).json()["results"][0]["contentId"]
    path = f"/contents/{content_id}"
    assert c.request("DELETE", path, headers=other, json={"revision": 1}).status_code == 403
    assert c.request("DELETE", path, headers=owner, json={"revision": 2}).status_code == 409
    assert c.post(path + "/withdraw", headers=owner, json={"revision": 1}).status_code == 200
    assert c.get(path + "/history", headers=owner).json()
    with Session(engine) as db:
        version = db.get(Classroom, class_id).version
    assert c.request("DELETE", path, headers=owner, json={"revision": 2}).status_code == 200
    assert c.request("DELETE", path, headers=owner, json={"revision": 2}).status_code == 200
    assert c.get("/device/snapshot", headers=device).json()["contents"] == []
    assert c.get("/contents", headers=owner, params={"classId": class_id}).json()["total"] == 0
    with Session(engine) as db:
        assert db.get(Content, content_id) is None
        assert db.scalar(select(func.count()).select_from(History).where(History.content_id == content_id)) == 0
        assert db.get(Classroom, class_id).version == version + 1
