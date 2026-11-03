from fastapi import APIRouter, Depends
from pydantic import Field
from sqlalchemy import delete, select
from sqlalchemy.orm import Session

from . import schemas as S
from .db import (
    Audit,
    Classroom,
    Command,
    Content,
    Device,
    DeviceDisplay,
    History,
    Invitation,
    Membership,
    Token,
    Transfer,
    User,
    get_db,
    now,
    uid,
)
from .security import actor, digest, fail, member, passwords, secret

router = APIRouter()


class PasswordInput(S.Input):
    oldPassword: str = Field(min_length=1, max_length=128)
    newPassword: str = Field(min_length=10, max_length=128)


@router.post("/auth/change-password")
def change_password(body: PasswordInput, user: User = Depends(actor), db: Session = Depends(get_db)):
    if not passwords.verify(body.oldPassword, user.password):
        fail(403, "password_invalid", "原口令不正确。")
    user.password = passwords.hash(body.newPassword)
    # 改密撤销所有会话，避免遗失设备仍维持管理权限。
    for token in db.scalars(select(Token).where(Token.user_id == user.id)):
        db.delete(token)
    return {"ok": True, "message": "口令已修改，请重新登录。"}


@router.get("/classes/{class_id}/members")
def members(class_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    _, viewer = member(db, user, class_id)
    rows = db.scalars(select(Membership).where(Membership.class_id == class_id, Membership.active.is_(True))).all()
    invites = db.scalars(
        select(Invitation).where(Invitation.class_id == class_id, Invitation.state.in_(["open", "pending"]))
    ).all()
    return {
        "members": [
            {
                "id": m.user_id,
                "name": db.get(User, m.user_id).name,
                "role": m.role,
                "subjects": m.subjects,
                "login": db.get(User, m.user_id).login if viewer.role in ("owner", "admin") else "",
            }
            for m in rows
        ],
        "applications": [
            {
                "id": i.id,
                "applicant": db.get(User, i.applicant).name if i.applicant else None,
                "applicantLogin": db.get(User, i.applicant).login if i.applicant else "",
                "subjects": i.subjects,
                "state": "expired" if i.expires <= now() else i.state,
            }
            for i in invites
            if viewer.role in ("owner", "admin")
        ],
    }


@router.post("/classes/{class_id}/invites", status_code=201)
def invite(class_id: str, body: S.InviteInput, user: User = Depends(actor), db: Session = Depends(get_db)):
    from .config import settings

    c, _ = member(db, user, class_id, admin=True, write=True)
    if not set(body.subjects) <= set(c.data["subjects"]):
        fail(422, "subject_unknown", "邀请学科不属于该班。")
    token = secret()
    i = Invitation(class_id=c.id, digest=digest(token), subjects=body.subjects, expires=now() + 72 * 3600)
    db.add(i)
    db.flush()
    db.add(Audit(class_id=c.id, actor=user.id, action="invitation_created", data={"invitationId": i.id}))
    return {"id": i.id, "url": f"{settings.public_url}/?invite={token}", "expiresAt": i.expires}


@router.post("/invites/{token}/apply")
def apply_invite(token: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    i = db.scalar(select(Invitation).where(Invitation.digest == digest(token)))
    if not i:
        fail(410, "invitation_invalid", "邀请已失效，请联系班级管理员。")
    # 与归档、审批统一采用“班级 → 邀请”的锁顺序，避免并发死锁。
    c = db.scalar(select(Classroom).where(Classroom.id == i.class_id).with_for_update())
    i = db.scalar(
        select(Invitation).where(Invitation.id == i.id).with_for_update().execution_options(populate_existing=True)
    )
    if not i or i.expires <= now() or i.state not in ("open", "pending"):
        fail(410, "invitation_invalid", "邀请已失效，请联系班级管理员。")
    if c.archived:
        fail(410, "invitation_invalid", "班级已归档。")
    if i.applicant and i.applicant != user.id:
        fail(409, "invitation_processing", "该邀请已有申请正在处理。")
    i.applicant = user.id
    i.state = "pending"
    return {"message": "已提交申请，等待管理员核对。", "className": c.name}


@router.get("/invites/{token}")
def invite_details(token: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    i = db.scalar(select(Invitation).where(Invitation.digest == digest(token)))
    if not i or i.expires <= now() or i.state not in ("open", "pending"):
        fail(410, "invitation_invalid", "邀请已失效，请联系班级管理员。")
    c = db.get(Classroom, i.class_id)
    if not c or c.archived:
        fail(410, "invitation_invalid", "班级不可用。")
    if i.applicant and i.applicant != user.id:
        fail(409, "invitation_processing", "该邀请已有申请正在处理。")
    return {
        "className": c.name,
        "subjects": i.subjects,
        "expiresAt": i.expires,
        "alreadyApplied": i.applicant == user.id,
    }


@router.post("/classes/{class_id}/invites/{invitation_id}/{decision}")
def decide_invite(
    class_id: str, invitation_id: str, decision: str, user: User = Depends(actor), db: Session = Depends(get_db)
):
    c, _ = member(db, user, class_id, admin=True, write=True)
    i = db.scalar(
        select(Invitation).where(Invitation.id == invitation_id, Invitation.class_id == c.id).with_for_update()
    )
    if not i or i.expires <= now() or i.state != "pending" or not i.applicant:
        fail(409, "invitation_invalid", "申请状态已改变，不能重复处理。")
    if decision not in ("approve", "reject"):
        fail(422, "decision_invalid", "审批动作无效。")
    if decision == "approve":
        m = db.scalar(select(Membership).where(Membership.class_id == c.id, Membership.user_id == i.applicant))
        if not m:
            m = Membership(class_id=c.id, user_id=i.applicant, role="teacher", subjects=[])
            db.add(m)
        m.active = True
        m.subjects = sorted(set(m.subjects) | set(i.subjects))
    i.state = "approved" if decision == "approve" else "rejected"
    db.add(Audit(class_id=c.id, actor=user.id, action=f"invitation_{i.state}", data={"invitationId": i.id}))
    return {"ok": True}


@router.put("/classes/{class_id}/members/{user_id}")
def update_member(
    class_id: str, user_id: str, body: S.MemberInput, user: User = Depends(actor), db: Session = Depends(get_db)
):
    c, current = member(db, user, class_id, admin=True, write=True)
    target = db.scalar(select(Membership).where(Membership.class_id == class_id, Membership.user_id == user_id))
    if not target or target.role == "owner":
        fail(403, "owner_protected", "不能修改或移除所有者，请使用所有权转移。")
    if not user.system_admin and current.role != "owner" and (target.role == "admin" or body.role == "admin"):
        fail(403, "owner_required", "仅所有者可以任命、撤销或移除管理员。")
    if not set(body.subjects) <= set(c.data["subjects"]):
        fail(422, "subject_unknown", "包含未配置学科。")
    target.role, target.subjects, target.active = body.role, body.subjects, body.active
    db.add(
        Audit(class_id=c.id, actor=user.id, action="membership_changed", data={"target": user_id, **body.model_dump()})
    )
    return {"ok": True}


@router.post("/classes/{class_id}/transfers")
def transfer(class_id: str, body: S.TransferInput, user: User = Depends(actor), db: Session = Depends(get_db)):
    c, current = member(db, user, class_id, write=True)
    if (current.role != "owner" and not user.system_admin) or body.recipient == c.owner_id:
        fail(403, "owner_required", "仅所有者可以向其他有效成员发起转移。")
    target = db.scalar(
        select(Membership).where(
            Membership.class_id == c.id, Membership.user_id == body.recipient, Membership.active.is_(True)
        )
    )
    if not target:
        fail(422, "member_invalid", "接收人不是有效成员。")
    t = db.get(Transfer, c.id)
    if t and t.state == "pending" and t.expires > now():
        fail(409, "transfer_pending", "已有待处理的转移请求。")
    if not t:
        t = Transfer(class_id=c.id)
        db.add(t)
    t.id, t.sender, t.recipient, t.state, t.expires = uid(), c.owner_id, target.user_id, "pending", now() + 86400
    return {"id": t.id, "expiresAt": t.expires}


@router.get("/transfers")
def incoming_transfers(user: User = Depends(actor), db: Session = Depends(get_db)):
    return [
        {"id": t.id, "classId": t.class_id, "className": db.get(Classroom, t.class_id).name, "expiresAt": t.expires}
        for t in db.scalars(
            select(Transfer).where(Transfer.recipient == user.id, Transfer.state == "pending", Transfer.expires > now())
        )
    ]


@router.post("/classes/{class_id}/transfers/{transfer_id}/{action_name}")
def finish_transfer(
    class_id: str, transfer_id: str, action_name: str, user: User = Depends(actor), db: Session = Depends(get_db)
):
    c, m = member(db, user, class_id, write=True)
    t = db.get(Transfer, class_id)
    if not t or t.id != transfer_id or t.state != "pending" or t.expires <= now() or c.owner_id != t.sender:
        fail(409, "transfer_invalid", "转移请求已失效。")
    if action_name == "cancel" and (user.id == t.sender or user.system_admin):
        t.state = "cancelled"
    elif action_name == "accept" and user.id == t.recipient:
        # 系统管理员的全班访问身份是临时授权；交接必须更新真实成员记录。
        m = db.scalar(
            select(Membership).where(
                Membership.class_id == c.id, Membership.user_id == user.id, Membership.active.is_(True)
            )
        )
        if not m:
            fail(409, "member_changed", "接收人的班级成员身份已改变。")
        old = db.scalar(
            select(Membership).where(
                Membership.class_id == c.id, Membership.user_id == t.sender, Membership.active.is_(True)
            )
        )
        if not old or old.role != "owner":
            fail(409, "owner_changed", "原所有者状态已改变。")
        old.role, m.role, c.owner_id, t.state = "admin", "owner", user.id, "accepted"
    else:
        fail(403, "transfer_forbidden", "不能执行此转移操作。")
    db.add(Audit(class_id=c.id, actor=user.id, action=f"transfer_{t.state}", data={"request": t.id}))
    return {"ok": True}


@router.post("/classes/{class_id}/{action_name}")
def lifecycle(class_id: str, action_name: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    c, m = member(db, user, class_id)
    if m.role != "owner" and not user.system_admin:
        fail(403, "owner_required", "仅所有者可以归档或恢复班级。")
    if action_name not in ("archive", "restore"):
        fail(404, "action_unknown", "操作不存在。")
    if (action_name == "archive") == c.archived:
        fail(409, "class_state_conflict", "班级状态已改变，请刷新。")
    c.archived = action_name == "archive"
    if c.archived:
        for d in db.scalars(select(Device).where(Device.class_id == c.id)):
            d.revoked = True
            db.execute(delete(DeviceDisplay).where(DeviceDisplay.device_id == d.id))
            for cmd in db.scalars(
                select(Command).where(Command.device_id == d.id, Command.state.in_(["accepted", "deferred"]))
            ):
                cmd.state = "rejected"
        for invitation in db.scalars(
            select(Invitation).where(Invitation.class_id == c.id, Invitation.state.in_(["open", "pending"]))
        ):
            invitation.state = "revoked"
        transfer = db.get(Transfer, c.id)
        if transfer and transfer.state == "pending":
            transfer.state = "cancelled"
    db.add(Audit(class_id=c.id, actor=user.id, action=action_name))
    return {"ok": True}


@router.get("/classes/{class_id}/export")
def export(class_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    from .app import content_view

    c, _ = member(db, user, class_id, admin=True)
    contents = db.scalars(select(Content).where(Content.class_id == c.id)).all()
    # 导出采取字段白名单，不序列化 ORM 对象，防止凭证或口令混入。
    return {
        "schema": 1,
        "classroom": {"name": c.name, "stage": c.stage, "archived": c.archived, "config": c.data},
        "contents": [content_view(db, item) for item in contents],
        "members": [
            {"name": db.get(User, m.user_id).name, "role": m.role, "subjects": m.subjects}
            for m in db.scalars(select(Membership).where(Membership.class_id == c.id))
        ],
        "history": [
            {
                "contentId": h.content_id,
                "revision": h.revision,
                "reason": h.reason,
                "before": h.before,
                "after": h.after,
            }
            for h in db.scalars(select(History).where(History.content_id.in_([item.id for item in contents])))
        ],
        "audit": [
            {
                "actor": db.get(User, item.actor).name if db.get(User, item.actor) else "历史成员",
                "action": item.action,
                "at": item.at,
                "data": item.data,
            }
            for item in db.scalars(select(Audit).where(Audit.class_id == c.id).order_by(Audit.at))
        ],
    }
