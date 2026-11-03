from sqlalchemy import select
from sqlalchemy.orm import Session

from backend.db import Audit, Command
from backend.tests.test_workflows import classroom, command, draft, paired, publish, user


def test_uninstall_revokes_only_self_and_cancels_pending_pairing(client):
    c, engine = client
    owner, _ = user(c)
    cid = classroom(c, owner)
    publish(c, owner, draft(c, owner, [cid], "卸载不能删除的教学内容"))
    did, device, _ = paired(c, owner, cid)
    _, other, _ = paired(c, owner, cid)
    cmd = command(c, owner, did, "sync").json()
    assert c.post("/device/uninstall", headers=owner).status_code == 401
    assert c.post("/device/uninstall", headers=device).status_code == 200
    assert c.post("/device/uninstall", headers=device).status_code == 200
    assert c.get("/device/snapshot", headers=device).status_code == 401
    assert c.get("/device/snapshot", headers=other).json()["contents"][0]["body"] == "卸载不能删除的教学内容"
    with Session(engine) as db:
        assert db.get(Command, cmd["id"]).state == "rejected"
        assert len(db.scalars(select(Audit).where(Audit.action == "device_uninstalled")).all()) == 1
    pending = c.post("/device-pairings", json={"name": "未绑定即卸载"}).json()
    assert c.post("/device/uninstall", headers={"Authorization": f"Bearer {pending['secret']}"}).status_code == 200
    assert c.post("/device-pairings/confirm", headers=owner, json={"code": pending["code"], "classId": cid}).status_code == 422
