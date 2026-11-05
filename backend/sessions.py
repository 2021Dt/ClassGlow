"""同一浏览器替换旧登录，手机和电脑可并存；冻结与改密仍撤销全部会话。"""

from fastapi import APIRouter, Depends, Header
from sqlalchemy import select, update
from sqlalchemy.orm import Session

from .config import settings
from .db import Token, User, get_db, now, sync_epoch
from .security import actor, bearer, digest, fail

router = APIRouter()


def create_session(db, user, token, client_id, user_agent):
    timestamp = now()
    client_hash = digest(f"{user.id}:{client_id or user_agent[:300]}")
    active = list(
        db.scalars(
            select(Token)
            .where(Token.user_id == user.id, Token.expires > timestamp, Token.epoch == sync_epoch(db))
            .order_by(Token.created_at, Token.id)
        )
    )
    for item in active:
        if item.client_hash == client_hash:
            item.expires, item.revoked_reason = timestamp, "session_replaced"
    remaining = [item for item in active if item.expires > timestamp]
    for item in remaining[: max(0, len(remaining) - settings.session_limit + 1)]:
        item.expires, item.revoked_reason = timestamp, "session_replaced"
    label = (
        "手机浏览器" if any(word in user_agent.lower() for word in ("mobile", "android", "iphone")) else "电脑浏览器"
    )
    db.add(
        Token(
            digest=digest(token),
            user_id=user.id,
            expires=timestamp + settings.session_hours * 3600,
            epoch=sync_epoch(db),
            client_hash=client_hash,
            label=label,
            created_at=timestamp,
        )
    )


@router.get("/account/sessions")
def sessions(
    authorization: str = Header(), user: User = Depends(actor), db: Session = Depends(get_db, scope="function")
):
    current = digest(bearer(authorization))
    return {
        "limit": settings.session_limit,
        "items": [
            {
                "id": item.id,
                "label": item.label,
                "createdAt": item.created_at,
                "expiresAt": item.expires,
                "current": item.digest == current,
            }
            for item in db.scalars(
                select(Token)
                .where(Token.user_id == user.id, Token.expires > now(), Token.epoch == sync_epoch(db))
                .order_by(Token.created_at.desc(), Token.id)
            )
        ],
    }


@router.post("/account/sessions/logout-others")
def logout_others(
    authorization: str = Header(), user: User = Depends(actor), db: Session = Depends(get_db, scope="function")
):
    db.execute(
        update(Token)
        .where(Token.user_id == user.id, Token.digest != digest(bearer(authorization)), Token.expires > now())
        .values(expires=now(), revoked_reason="session_revoked")
    )
    return {"ok": True}


@router.delete("/account/sessions/{session_id}")
def revoke(session_id: str, user: User = Depends(actor), db: Session = Depends(get_db, scope="function")):
    item = db.scalar(select(Token).where(Token.id == session_id, Token.user_id == user.id))
    if not item:
        fail(404, "session_not_found", "登录设备不存在。")
    item.expires, item.revoked_reason = now(), "session_revoked"
    return {"ok": True}
