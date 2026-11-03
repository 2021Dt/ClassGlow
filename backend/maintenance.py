"""班级配置与课表维护，所有写入使用同一班级锁和配置修订。"""

from datetime import datetime
from zoneinfo import ZoneInfo

from fastapi import APIRouter, Depends
from sqlalchemy import select
from sqlalchemy.orm import Session

from . import schemas as S
from .db import Audit, Invitation, Membership, Transfer, User, get_db, now
from .security import actor, fail, member

router = APIRouter()


@router.get("/classes/{class_id}/audit")
def audit_history(class_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    member(db, user, class_id, admin=True)
    return [
        {
            "id": item.id,
            "actor": "教室设备"
            if item.action == "device_uninstalled"
            else db.get(User, item.actor).name
            if db.get(User, item.actor)
            else "历史成员",
            "action": item.action,
            "at": item.at,
        }
        for item in db.scalars(select(Audit).where(Audit.class_id == class_id).order_by(Audit.at.desc()).limit(100))
    ]


class ClassSettings(S.ClassInput):
    revision: int


@router.put("/classes/{class_id}/settings")
def settings(class_id: str, body: ClassSettings, user: User = Depends(actor), db: Session = Depends(get_db)):
    c, _ = member(db, user, class_id, admin=True, write=True)
    if body.revision != c.revision:
        fail(409, "configuration_conflict", "班级配置已改变，请比较最新设置。")
    c.name, c.stage = body.name, body.stage
    c.data = {**c.data, "subjects": body.subjects}
    for m in db.scalars(select(Membership).where(Membership.class_id == c.id)):
        m.subjects = [s for s in m.subjects if s in body.subjects]
    c.version += 1
    c.revision += 1
    db.add(Audit(class_id=c.id, actor=user.id, action="class_settings_updated", data={"subjects": body.subjects}))
    return {"revision": c.revision}


@router.post("/classes/{class_id}/invites/{invitation_id}/revoke")
def revoke_invite(class_id: str, invitation_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    c, _ = member(db, user, class_id, admin=True, write=True)
    i = db.get(Invitation, invitation_id)
    if not i or i.class_id != c.id or i.state not in ("open", "pending"):
        fail(409, "invitation_invalid", "邀请已失效，不能重复撤销。")
    i.state = "revoked"
    db.add(Audit(class_id=c.id, actor=user.id, action="invitation_revoked", data={"invitationId": i.id}))
    return {"ok": True}


@router.get("/classes/{class_id}/transfer")
def transfer_state(class_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    c, m = member(db, user, class_id)
    if m.role != "owner":
        fail(403, "owner_required", "仅所有者可查看待发起交接。")
    t = db.get(Transfer, c.id)
    if not t:
        return None
    return {
        "id": t.id,
        "recipient": t.recipient,
        "state": "expired" if t.state == "pending" and t.expires <= now() else t.state,
        "expiresAt": t.expires,
    }


@router.delete("/classes/{class_id}/overrides/{date}")
def remove_override(
    class_id: str, date: str, body: S.RevisionInput, user: User = Depends(actor), db: Session = Depends(get_db)
):
    c, _ = member(db, user, class_id, admin=True, write=True)
    if c.revision != body.revision:
        fail(409, "configuration_conflict", "课表已改变，请重新核对。")
    overrides = dict(c.data.get("overrides", {}))
    if date not in overrides:
        fail(404, "override_missing", "该日期没有临时调整。")
    previous = overrides.pop(date)
    c.data = {**c.data, "overrides": overrides}
    c.revision += 1
    c.version += 1
    db.add(Audit(class_id=c.id, actor=user.id, action="override_removed", data={"date": date, "before": previous}))
    return {"revision": c.revision}


@router.delete("/classes/{class_id}/timetable/{start}")
def cancel_future_template(
    class_id: str, start: str, body: S.RevisionInput, user: User = Depends(actor), db: Session = Depends(get_db)
):
    c, _ = member(db, user, class_id, admin=True, write=True)
    if body.revision != c.revision:
        fail(409, "configuration_conflict", "课表已改变，请重新核对。")
    today = datetime.now(ZoneInfo("Asia/Shanghai")).date().isoformat()
    if start <= today:
        fail(422, "history_protected", "已生效课表保留历史，请发布未来生效的调整。")
    templates = c.data.get("templates", [])
    if not any(t["effectiveFrom"] == start for t in templates):
        fail(404, "template_missing", "模板不存在。")
    c.data = {**c.data, "templates": [t for t in templates if t["effectiveFrom"] != start]}
    c.revision += 1
    c.version += 1
    db.add(Audit(class_id=c.id, actor=user.id, action="future_template_cancelled", data={"effectiveFrom": start}))
    return {"revision": c.revision}
