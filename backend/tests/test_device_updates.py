import asyncio

from sqlalchemy.orm import Session
from test_workflows import classroom, command, paired, user

from backend.db import Device, Membership, User
from backend.realtime import updates


def test_device_name_permissions_and_free_sync(client):
    c, engine = client
    owner, owner_id = user(c)
    teacher, teacher_id = user(c, "colleague")
    outsider, _ = user(c, "outsider")
    cid = classroom(c, owner)
    did, credential, identity = paired(c, owner, cid)
    with Session(engine) as db:
        db.add(Membership(user_id=teacher_id, class_id=cid, role="teacher", subjects=["数学"]))
        db.get(User, owner_id).premium_until = 0
        db.commit()
    before = c.get("/device/snapshot", headers=credential)
    for forbidden in [teacher, outsider]:
        assert c.patch(f"/devices/{did}", headers=forbidden, json={"name": "别人的电脑"}).status_code == 403
    assert c.patch(f"/devices/{did}", headers=owner, json={"name": "   "}).status_code == 422
    renamed = c.patch(f"/devices/{did}", headers=owner, json={"name": "  六一班讲台  "})
    assert renamed.status_code == 200 and renamed.json()["name"] == "六一班讲台"
    assert renamed.json()["bindingId"] == identity["bindingId"] and renamed.json()["revision"] == 0
    after = c.get("/device/snapshot", headers={**credential, "If-None-Match": before.headers["ETag"]})
    assert after.status_code == 200 and after.json()["deviceName"] == "六一班讲台"
    assert command(c, owner, did, "sync").status_code == 202
    assert c.get("/device/snapshot", headers=credential).json()["commands"][0]["action"] == "sync"
    assert command(c, owner, did, "pause", revision=1).status_code == 403
    assert c.get(f"/classes/{cid}/devices", headers=outsider).status_code == 403


def test_notifications_commit_scope_and_rollback(client):
    c, engine = client
    owner, _ = user(c)
    cid = classroom(c, owner)
    did, _, _ = paired(c, owner, cid)

    async def verify():
        desktop = updates.subscribe({f"device:{did}"})
        browser = updates.subscribe({f"status:{cid}"})
        unrelated = updates.subscribe({"device:unrelated", "status:unrelated"})
        try:
            response = await asyncio.to_thread(c.patch, f"/devices/{did}", headers=owner, json={"name": "讲台电脑"})
            assert response.status_code == 200
            await asyncio.wait_for(desktop.queue.get(), 1)
            await asyncio.wait_for(browser.queue.get(), 1)
            assert unrelated.queue.empty()
            with Session(engine) as db:
                d = db.get(Device, did)
                d.name = "不应发布的临时名称"
                db.flush()
                db.rollback()
            await asyncio.sleep(0)
            assert desktop.queue.empty() and browser.queue.empty()
        finally:
            for listener in [desktop, browser, unrelated]:
                updates.remove(listener)

    asyncio.run(verify())
