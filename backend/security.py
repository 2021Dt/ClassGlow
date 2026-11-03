import hashlib
import secrets
import threading
from collections import defaultdict, deque

from fastapi import Depends, Header, HTTPException
from pwdlib import PasswordHash
from sqlalchemy import select
from sqlalchemy.orm import Session

from .db import Classroom, Device, Membership, Token, User, get_db, now, sync_epoch

passwords = PasswordHash.recommended()
_buckets: dict[str, deque] = defaultdict(deque)
_bucket_lock = threading.Lock()


def fail(status: int, code: str, message: str):
    raise HTTPException(status, {"code": code, "message": message})


def digest(value: str) -> str:
    return hashlib.sha256(value.encode()).hexdigest()


def secret() -> str:
    return secrets.token_urlsafe(36)


def limit(key: str, maximum: int, seconds: int):
    """单进程本地限流；多进程生产部署仍须在反向代理配置全局速率限制。"""
    with _bucket_lock:
        if key not in _buckets and len(_buckets) >= 10000:
            # 为攻击者构造的无限新账号名设置内存上限，生产仍需代理侧全局限流。
            expired = [k for k, v in _buckets.items() if not v or v[-1] <= now() - 3600]
            for old_key in expired:
                del _buckets[old_key]
            if len(_buckets) >= 10000:
                fail(429, "rate_limited", "服务正忙，请稍后重试。")
        bucket = _buckets[key]
        while bucket and bucket[0] <= now() - seconds:
            bucket.popleft()
        if len(bucket) >= maximum:
            fail(429, "rate_limited", "尝试过于频繁，请稍后重试。")
        bucket.append(now())


def bearer(authorization: str | None) -> str:
    if not authorization or not authorization.startswith("Bearer "):
        fail(401, "authentication_required", "请先登录。")
    return authorization[7:]


def actor(authorization: str | None = Header(default=None), db: Session = Depends(get_db)) -> User:
    session = db.get(Token, digest(bearer(authorization)))
    if not session or session.expires <= now() or session.epoch != sync_epoch(db):
        fail(401, "session_expired", "登录已失效，请重新登录。")
    # 同一账号写入串行化，也使相同幂等键的并发重试取得同一结果。
    user = db.scalar(select(User).where(User.id == session.user_id).with_for_update())
    if not user or not user.active:
        fail(401, "session_expired", "账号不可用。")
    return user


def system_admin(user: User = Depends(actor)) -> User:
    if not user.system_admin:
        fail(403, "system_admin_required", "此操作需要系统管理员权限。")
    return user


def member(db: Session, user: User, class_id: str, admin=False, write=False) -> tuple[Classroom, Membership]:
    # 所有班级写入先锁班级，权限变更和发布使用同一锁约定。
    c = db.scalar(select(Classroom).where(Classroom.id == class_id).with_for_update())
    m = db.scalar(
        select(Membership).where(
            Membership.class_id == class_id, Membership.user_id == user.id, Membership.active.is_(True)
        )
    )
    if not c or not m:
        fail(403, "class_forbidden", "无权访问该班级。")
    if admin and m.role not in ("owner", "admin"):
        fail(403, "admin_required", "此操作需要班级管理权限。")
    if write and c.archived:
        fail(409, "class_archived", "班级已归档，只能查看或导出历史。")
    return c, m


def device_actor(authorization: str | None = Header(default=None), db: Session = Depends(get_db)) -> Device:
    d = db.scalar(select(Device).where(Device.credential_hash == digest(bearer(authorization))).with_for_update())
    if not d or d.revoked or d.epoch != sync_epoch(db):
        fail(401, "device_revoked", "设备授权已失效，请清除缓存并重新绑定。")
    c = db.get(Classroom, d.class_id)
    if not c or c.archived:
        fail(401, "device_revoked", "班级不可用，请清除缓存。")
    d.last_seen = now()
    return d
