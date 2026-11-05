"""系统公告按账号领取，弹出记录跨浏览器和登录会话保留。"""

from typing import Annotated, Literal

from fastapi import APIRouter, Depends, Header, Query, Response
from pydantic import Field, field_validator, model_validator
from sqlalchemy import func, or_, select
from sqlalchemy.orm import Session

from .db import Announcement, AnnouncementReceipt, AnnouncementTarget, Idempotency, User, get_db, now
from .schemas import Input
from .security import actor, fail, system_admin
from .system_service import log_system

router = APIRouter()


class AnnouncementInput(Input):
    title: str = Field(min_length=1, max_length=120)
    markdown: str = Field(min_length=1, max_length=16000)
    scope: Literal["all", "selected"]
    accountIds: list[Annotated[str, Field(min_length=1, max_length=36)]] = Field(default_factory=list, max_length=500)

    @field_validator("title", mode="before")
    @classmethod
    def trim_text(cls, value):
        return value.strip() if isinstance(value, str) else value

    @model_validator(mode="after")
    def verify_scope(self):
        if not self.markdown.strip():
            raise ValueError("请填写公告正文。")
        if self.scope == "selected" and not self.accountIds:
            raise ValueError("请至少选择一个接收账号。")
        if self.scope == "all" and self.accountIds:
            raise ValueError("全体账号无需指定接收人。")
        self.accountIds = sorted(set(self.accountIds))
        return self


def notice_view(notice: Announcement):
    return {
        "id": notice.id,
        "title": notice.title,
        "markdown": notice.markdown,
        "publishedAt": notice.published_at,
    }


@router.get("/system/announcements")
def list_announcements(
    offset: int = Query(0, ge=0),
    limit: int = Query(20, ge=1, le=100),
    user: User = Depends(system_admin),
    db: Session = Depends(get_db, scope="function"),
):
    target_count = (
        select(func.count())
        .select_from(AnnouncementTarget)
        .where(AnnouncementTarget.announcement_id == Announcement.id)
        .scalar_subquery()
    )
    shown_count = (
        select(func.count())
        .select_from(AnnouncementReceipt)
        .where(AnnouncementReceipt.announcement_id == Announcement.id)
        .scalar_subquery()
    )
    rows = db.execute(
        select(Announcement, target_count, shown_count)
        .order_by(Announcement.published_at.desc(), Announcement.id)
        .offset(offset)
        .limit(limit)
    )
    return {
        "items": [
            {
                **notice_view(notice),
                "scope": notice.scope,
                "withdrawnAt": notice.withdrawn_at,
                "targetCount": count if notice.scope == "selected" else None,
                "shownCount": shown,
            }
            for notice, count, shown in rows
        ],
        "total": db.scalar(select(func.count()).select_from(Announcement)),
    }


@router.post("/system/announcements", status_code=201)
def publish_announcement(
    body: AnnouncementInput,
    idempotency_key: str | None = Header(default=None),
    user: User = Depends(system_admin),
    db: Session = Depends(get_db, scope="function"),
):
    from .app import idempotent

    identity, signature, prior = idempotent(db, user, idempotency_key, "announcement", body.model_dump())
    if prior is not None:
        return prior
    if body.accountIds:
        accounts = set(db.scalars(select(User.id).where(User.id.in_(body.accountIds))))
        if accounts != set(body.accountIds):
            fail(422, "announcement_accounts_invalid", "部分接收账号不存在，请重新选择。")
    notice = Announcement(title=body.title, markdown=body.markdown, scope=body.scope)
    db.add(notice)
    db.flush()
    db.add_all([AnnouncementTarget(announcement_id=notice.id, user_id=account_id) for account_id in body.accountIds])
    log_system(
        db,
        user.login,
        "announcement_published",
        notice.title,
        {"id": notice.id, "scope": body.scope, "targetCount": len(body.accountIds)},
    )
    result = notice_view(notice)
    db.add(Idempotency(key=identity, request_hash=signature, result=result))
    return result


@router.post("/system/announcements/{notice_id}/withdraw")
def withdraw_announcement(
    notice_id: str, user: User = Depends(system_admin), db: Session = Depends(get_db, scope="function")
):
    notice = db.scalar(select(Announcement).where(Announcement.id == notice_id).with_for_update())
    if notice is None:
        fail(404, "announcement_not_found", "公告不存在。")
    if notice.withdrawn_at is None:
        notice.withdrawn_at = now()
        log_system(db, user.login, "announcement_withdrawn", notice.title, {"id": notice.id})
    return {"ok": True}


@router.post("/announcements/next")
def next_announcement(
    response: Response,
    idempotency_key: str | None = Header(default=None, min_length=8, max_length=36),
    user: User = Depends(actor),
    db: Session = Depends(get_db, scope="function"),
):
    response.headers["Cache-Control"] = "no-store"
    if idempotency_key:
        # 请求超时后允许同一页面取回本次领取结果，避免网络波动吞掉公告。
        prior = db.scalar(
            select(Announcement)
            .join(AnnouncementReceipt)
            .where(AnnouncementReceipt.user_id == user.id, AnnouncementReceipt.claim_key == idempotency_key)
        )
        if prior is not None:
            return {"notice": notice_view(prior) if prior.withdrawn_at is None else None}

    targeted = (
        select(AnnouncementTarget.user_id)
        .where(AnnouncementTarget.announcement_id == Announcement.id, AnnouncementTarget.user_id == user.id)
        .exists()
    )
    shown = (
        select(AnnouncementReceipt.user_id)
        .where(AnnouncementReceipt.announcement_id == Announcement.id, AnnouncementReceipt.user_id == user.id)
        .exists()
    )
    # actor 已锁定当前账号，领取和写入记录在同一事务完成。多个页面也只会领取一次。
    notice = db.scalar(
        select(Announcement)
        .where(Announcement.withdrawn_at.is_(None), or_(Announcement.scope == "all", targeted), ~shown)
        .order_by(Announcement.published_at, Announcement.id)
        .limit(1)
    )
    if notice is None:
        return {"notice": None}
    db.add(AnnouncementReceipt(announcement_id=notice.id, user_id=user.id, claim_key=idempotency_key))
    db.flush()
    return {"notice": notice_view(notice)}
