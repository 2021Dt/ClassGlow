"""只有系统管理员可签发许可；许可码绑定账号、一次兑换，数据库只保存摘要。"""

import secrets
from typing import Literal

from fastapi import APIRouter, Depends, Query, Request
from pydantic import Field
from sqlalchemy import func, or_, select
from sqlalchemy.orm import Session

from . import schemas as S
from .db import FeatureLicense, User, get_db, now, sync_epoch
from .entitlements import profile
from .security import actor, digest, fail, limit, system_admin
from .system_service import log_system

router = APIRouter()


class Issue(S.Input):
    userId: str
    days: int = Field(ge=1, le=3660)


class Redeem(S.Input):
    code: str = Field(min_length=12, max_length=40)


@router.post("/system/licenses", status_code=201)
def issue(body: Issue, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    target = db.get(User, body.userId)
    if not target or not target.active or target.system_admin:
        fail(422, "license_target", "请选择启用的班主任或教师账号。系统管理员无需兑换高级许可。")
    raw = "".join(secrets.choice("ABCDEFGHJKLMNPQRSTUVWXYZ23456789") for _ in range(20))
    entry = FeatureLicense(
        user_id=target.id, code_hash=digest(raw), epoch=sync_epoch(db), days=body.days, expires=now() + 30 * 86400
    )
    db.add(entry)
    db.flush()
    log_system(db, user.login, "license_issued", target.login, {"licenseId": entry.id, "days": body.days})
    return {
        "id": entry.id,
        "code": "-".join(raw[i : i + 5] for i in range(0, 20, 5)),
        "login": target.login,
        "days": entry.days,
        "expiresAt": entry.expires,
    }


@router.get("/system/licenses")
def listing(
    userId: str = "",
    search: str = "",
    status: Literal["", "pending", "used", "revoked", "expired"] = "",
    offset: int = Query(0, ge=0),
    limit: int = Query(20, ge=1, le=100),
    user: User = Depends(system_admin),
    db: Session = Depends(get_db),
):
    epoch, timestamp = sync_epoch(db), now()
    query = select(FeatureLicense, User).join(User, User.id == FeatureLicense.user_id)
    if userId:
        query = query.where(FeatureLicense.user_id == userId)
    if search:
        query = query.where(
            or_(User.name.contains(search, autoescape=True), User.login.contains(search, autoescape=True))
        )
    if status == "used":
        query = query.where(FeatureLicense.used_at > 0)
    elif status:
        query = query.where(FeatureLicense.used_at == 0)
        if status == "revoked":
            query = query.where(FeatureLicense.revoked.is_(True))
        elif status == "pending":
            query = query.where(
                FeatureLicense.revoked.is_(False), FeatureLicense.expires > timestamp, FeatureLicense.epoch == epoch
            )
        else:
            query = query.where(
                FeatureLicense.revoked.is_(False),
                or_(FeatureLicense.expires <= timestamp, FeatureLicense.epoch != epoch),
            )
    entries = db.execute(query.order_by(FeatureLicense.expires.desc(), FeatureLicense.id).offset(offset).limit(limit))
    return {
        "items": [
            {
                "id": e.id,
                "userId": target.id,
                "name": target.name,
                "login": target.login,
                "days": e.days,
                "expiresAt": e.expires,
                "usedAt": e.used_at,
                "revoked": e.revoked,
                "expired": e.expires <= timestamp or e.epoch != epoch,
            }
            for e, target in entries
        ],
        "total": db.scalar(select(func.count()).select_from(query.subquery())),
    }


@router.post("/system/licenses/{license_id}/revoke")
def revoke(license_id: str, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    entry = db.scalar(select(FeatureLicense).where(FeatureLicense.id == license_id).with_for_update())
    if not entry or entry.used_at:
        fail(409, "license_used", "许可码不存在或已经兑换；如需停用已生效许可，请在账号许可管理中终止。")
    entry.revoked = True
    log_system(db, user.login, "license_revoked", entry.user_id, {"licenseId": entry.id})
    return {"ok": True}


@router.post("/system/users/{user_id}/license/revoke")
def revoke_entitlement(user_id: str, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    target = db.scalar(select(User).where(User.id == user_id).with_for_update())
    if not target or target.system_admin:
        fail(422, "license_target", "请选择班主任或教师账号。")
    target.premium_until = 0
    for entry in db.scalars(
        select(FeatureLicense).where(FeatureLicense.user_id == target.id, FeatureLicense.used_at == 0)
    ):
        entry.revoked = True
    log_system(db, user.login, "license_ended", target.login)
    return {"ok": True}


@router.get("/account/license")
def license_status(user: User = Depends(actor)):
    return profile(user)


@router.post("/account/license/redeem")
def redeem(body: Redeem, request: Request, user: User = Depends(actor), db: Session = Depends(get_db)):
    limit(f"license:{user.id}", 10, 300)
    raw = body.code.replace("-", "").replace(" ", "").upper()
    entry = db.scalar(select(FeatureLicense).where(FeatureLicense.code_hash == digest(raw)).with_for_update())
    if (
        not entry
        or entry.user_id != user.id
        or entry.revoked
        or entry.expires <= now()
        or entry.epoch != sync_epoch(db)
    ):
        fail(400, "license_invalid", "许可码不匹配、已失效或已被撤销，请联系管理员核对。")
    # 响应丢失后可重复查询同一个已兑换码，不重复延长许可。
    if not entry.used_at:
        user.premium_until = max(now(), user.premium_until) + entry.days * 86400
        entry.used_at = now()
        log_system(
            db, user.login, "license_redeemed", user.login, {"licenseId": entry.id, "premiumUntil": user.premium_until}
        )
    return profile(user)
