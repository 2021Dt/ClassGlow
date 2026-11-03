"""只读查询按当前班级权限过滤，列表采用有界分页。"""

from datetime import UTC, datetime

from fastapi import APIRouter, Depends, Query
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from .db import Command, Content, Device, Draft, Idempotency, User, get_db, now
from .security import actor, fail, member

router = APIRouter()


@router.get("/devices/{device_id}/commands/by-key/{request_key}")
def command_result(device_id: str, request_key: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    from .devices import command_view

    device = db.get(Device, device_id)
    if not device:
        fail(404, "device_not_found", "设备不存在。")
    member(db, user, device.class_id)
    record = db.get(Idempotency, f"{user.id}:control:{device_id}:{request_key}")
    if not record:
        fail(404, "result_not_found", "暂未找到原控制请求，请使用原标识安全重试。")
    command = db.get(Command, record.result["id"])
    if not command:
        fail(404, "result_not_found", "控制历史不可用，请联系管理员核对。")
    return command_view(command)


@router.get("/drafts/{draft_id}")
def draft_detail(draft_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    draft = db.get(Draft, draft_id)
    if not draft or draft.owner_id != user.id or draft.closed:
        fail(404, "draft_not_found", "草稿不存在或已关闭。")
    return {**draft.data, "revision": draft.revision, "updated": datetime.fromtimestamp(draft.updated, UTC).isoformat()}


@router.get("/contents/{content_id}")
def content_detail(content_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    from .app import content_view

    content = db.get(Content, content_id)
    if not content:
        fail(404, "content_not_found", "内容不存在。")
    member(db, user, content.class_id)
    return content_view(db, content, user.id)


@router.get("/contents")
def contents(
    classId: str,
    kind: str = "",
    state: str = "",
    subject: str = "",
    search: str = Query(default="", max_length=100),
    date: str = "",
    offset: int = Query(default=0, ge=0),
    limit: int = Query(default=20, ge=1, le=100),
    user: User = Depends(actor),
    db: Session = Depends(get_db),
):
    from .app import content_view

    member(db, user, classId)
    conditions = [Content.class_id == classId]
    if kind:
        if kind not in ("homework", "todo"):
            fail(422, "kind_invalid", "内容类型无效。")
        conditions.append(Content.data["kind"].as_string() == kind)
    if subject:
        conditions.append(Content.data["subject"].as_string() == subject)
    if search:
        conditions.append(Content.data["body"].as_string().contains(search, autoescape=True))
    timestamp = datetime.fromtimestamp(now(), UTC).isoformat()
    if state in ("published", "completed", "withdrawn"):
        conditions.append(Content.state == state)
    elif state in ("active", "scheduled", "ended"):
        conditions.append(Content.state == "published")
        if state == "scheduled":
            conditions.append(Content.data["from"].as_string() > timestamp)
        elif state == "ended":
            conditions.append(Content.data["until"].as_string() <= timestamp)
        else:
            conditions.extend(
                [Content.data["from"].as_string() <= timestamp, Content.data["until"].as_string() > timestamp]
            )
    elif state:
        fail(422, "state_invalid", "状态筛选无效。")
    if date:
        try:
            from zoneinfo import ZoneInfo

            start = datetime.strptime(date, "%Y-%m-%d").replace(tzinfo=ZoneInfo("Asia/Shanghai"))
        except ValueError:
            fail(422, "date_invalid", "日期无效。")
        from datetime import timedelta

        conditions.extend(
            [
                Content.data["from"].as_string() < (start + timedelta(days=1)).astimezone(UTC).isoformat(),
                Content.data["until"].as_string() > start.astimezone(UTC).isoformat(),
            ]
        )
    total = db.scalar(select(func.count()).select_from(Content).where(*conditions))
    rows = db.scalars(
        select(Content)
        .where(*conditions)
        .order_by(Content.data["created"].as_string().desc(), Content.id)
        .offset(offset)
        .limit(limit)
    ).all()
    return {
        "items": [content_view(db, item, user.id) for item in rows],
        "total": total,
        "offset": offset,
        "limit": limit,
    }
