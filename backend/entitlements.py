"""身份决定可操作范围，许可只开放高级功能，不提升身份。"""

from sqlalchemy import select

from .db import Membership, User, now
from .security import fail


def account_available(user):
    return bool(user and user.active and user.activated and (not user.expires_at or user.expires_at > now()))


def has_premium(user):
    return account_available(user) and (user.system_admin or user.premium_until > now())


def require_premium(user):
    if not has_premium(user):
        fail(403, "premium_required", "此功能需要有效的高级许可，请向系统管理员领取许可码。基础功能仍可使用。")


def profile(user):
    return {
        "id": user.id,
        "name": user.name,
        "login": user.login,
        "systemAdmin": user.system_admin,
        "canCreateClasses": user.can_create_classes or user.system_admin,
        "activated": user.activated,
        "expiresAt": user.expires_at,
        "premiumUntil": user.premium_until,
        "premiumActive": has_premium(user),
        "serverTime": now(),
    }


def class_entitlement(db, classroom):
    # 展示绑定最后一次正式发布布局的账号许可；登录/退出不会改变归属。
    owner = db.get(User, classroom.data.get("layoutLicenseUserId", classroom.owner_id))
    active = has_premium(owner)
    if active and not owner.system_admin:
        membership = db.scalar(
            select(Membership).where(
                Membership.user_id == owner.id, Membership.class_id == classroom.id, Membership.active.is_(True)
            )
        )
        active = bool(membership and membership.role in ("owner", "admin"))
    until = 0 if owner.system_admin else owner.premium_until
    if owner.expires_at:
        until = min(until, owner.expires_at) if until else owner.expires_at
    return {"premiumActive": active, "premiumUntil": until if active else 0, "licenseOwner": owner.name}
