"""管理员预设账号权限，老师只用账号、密码和一次性激活码完成首次使用。"""

import secrets
from typing import Literal

from fastapi import APIRouter, Depends, Request
from pydantic import Field
from sqlalchemy import select, update
from sqlalchemy.orm import Session

from . import schemas as S
from .db import AccountActivation, Classroom, Membership, User, get_db, now, sync_epoch
from .security import digest, fail, limit, passwords, secret, system_admin
from .system_service import log_system, system_config

router = APIRouter()


class ClassGrant(S.Input):
    classId: str
    subjects: list[str] = Field(default_factory=list, max_length=20)


class OpenAccount(S.Input):
    login: str = Field(min_length=3, max_length=80, pattern=r"^[a-zA-Z0-9_@.\-]+$")
    name: str = Field(min_length=1, max_length=40)
    identity: Literal["teacher", "headteacher", "system_admin"]
    expiresAt: float = Field(default=0, ge=0, le=4102444800)
    classes: list[ClassGrant] = Field(default_factory=list, max_length=20)


class Activate(S.Credentials):
    activationCode: str = Field(min_length=12, max_length=40)


def issue_code(db, target):
    # 明文仅在生成时返回；重发立即作废旧码，恢复数据库后旧码随同步世代失效。
    db.execute(update(AccountActivation).where(AccountActivation.user_id == target.id).values(revoked=True))
    raw = "".join(secrets.choice("ABCDEFGHJKLMNPQRSTUVWXYZ23456789") for _ in range(16))
    expires = now() + 7 * 86400
    db.add(AccountActivation(user_id=target.id, code_hash=digest(raw), epoch=sync_epoch(db), expires=expires))
    return {
        "login": target.login,
        "name": target.name,
        "activationCode": "-".join(raw[i : i + 4] for i in range(0, 16, 4)),
        "expiresAt": expires,
    }


@router.post("/system/account-invitations", status_code=201)
def open_account(body: OpenAccount, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    if body.expiresAt and body.expiresAt <= now():
        fail(422, "expiry_invalid", "账号有效期请选择未来日期，或选择长期有效。")
    if not body.name.strip():
        fail(422, "name_required", "请填写老师姓名。")
    if db.scalar(select(User).where(User.login == body.login.casefold())):
        fail(409, "login_taken", "该账号已存在，请使用其他账号，或在账号列表中重发激活码。")
    if len({g.classId for g in body.classes}) != len(body.classes):
        fail(422, "duplicate_class", "同一个班级只需选择一次。")
    if body.identity == "teacher" and not body.classes:
        fail(422, "class_required", "请为科任教师分配至少一个任教班级和学科。")
    grants = []
    for grant in body.classes:
        c = db.scalar(select(Classroom).where(Classroom.id == grant.classId).with_for_update())
        if not c or c.archived:
            fail(422, "class_unavailable", "请选择使用中的班级。")
        if body.identity == "teacher" and (not grant.subjects or not set(grant.subjects) <= set(c.data["subjects"])):
            fail(422, "subjects_invalid", "请从班级已有学科中选择任教学科。")
        grants.append((c, grant))
    target = User(
        login=body.login.casefold(),
        name=body.name.strip(),
        password=passwords.hash(secret()),
        activated=False,
        system_admin=body.identity == "system_admin",
        can_create_classes=body.identity != "teacher",
        expires_at=body.expiresAt,
    )
    db.add(target)
    db.flush()
    for c, grant in grants:
        db.add(
            Membership(
                class_id=c.id,
                user_id=target.id,
                role="teacher" if body.identity == "teacher" else "admin",
                subjects=list(dict.fromkeys(grant.subjects)) if body.identity == "teacher" else c.data["subjects"],
            )
        )
    result = issue_code(db, target)
    log_system(
        db,
        user.login,
        "account_invited",
        target.login,
        {"identity": body.identity, "classIds": [g.classId for g in body.classes]},
    )
    return result


@router.post("/system/users/{user_id}/activation")
def reissue(user_id: str, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    target = db.scalar(select(User).where(User.id == user_id).with_for_update())
    if not target or not target.active or target.activated:
        fail(409, "activation_unavailable", "仅可为启用且尚未激活的账号重发激活码。已激活账号请使用重置密码。")
    result = issue_code(db, target)
    log_system(db, user.login, "activation_reissued", target.login)
    return result


@router.post("/auth/activate")
def activate(body: Activate, request: Request, db: Session = Depends(get_db)):
    limit(f"activation:{request.client.host}", 10, 300)
    if not system_config(db).initialized:
        fail(409, "school_pending", "学校尚未开通，请联系管理员。")
    user = db.scalar(select(User).where(User.login == body.login.casefold()).with_for_update())
    code_hash = digest(body.activationCode.replace("-", "").replace(" ", "").upper())
    code = db.scalar(select(AccountActivation).where(AccountActivation.code_hash == code_hash).with_for_update())
    if (
        not user
        or not user.active
        or user.activated
        or (user.expires_at and user.expires_at <= now())
        or not code
        or code.user_id != user.id
        or code.revoked
        or code.used_at
        or code.expires <= now()
        or code.epoch != sync_epoch(db)
    ):
        fail(
            400, "activation_invalid", "账号或激活码不匹配，或激活码已过期/使用。已激活请直接登录，否则联系管理员重发。"
        )
    user.password = passwords.hash(body.password)
    user.activated, code.used_at = True, now()
    log_system(db, user.login, "account_activated", user.login)
    from .app import session_result

    return session_result(db, user)
