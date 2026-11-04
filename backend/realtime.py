"""提交后发送轻量更新通知；内容仍经原有鉴权接口读取，断线由定期同步兜底。"""

import asyncio
from dataclasses import dataclass, field
from threading import Lock

from fastapi import APIRouter, Header, HTTPException
from fastapi.responses import StreamingResponse
from sqlalchemy import event, inspect, select
from sqlalchemy.orm import Session
from starlette.concurrency import run_in_threadpool

from .db import Classroom, Command, Device, DeviceDisplay, Meta, SessionLocal, now
from .security import actor, device_actor, member

router = APIRouter()


@dataclass(eq=False)
class Subscription:
    keys: set[str]
    loop: asyncio.AbstractEventLoop
    queue: asyncio.Queue = field(default_factory=lambda: asyncio.Queue(maxsize=1))
    closed: bool = False

    def wake(self):
        if not self.closed and self.queue.empty():
            self.queue.put_nowait(True)


class Updates:
    def __init__(self):
        self.lock = Lock()
        self.listeners: set[Subscription] = set()

    def subscribe(self, keys):
        listener = Subscription(set(keys) | {"epoch"}, asyncio.get_running_loop())
        with self.lock:
            self.listeners.add(listener)
        return listener

    def remove(self, listener):
        listener.closed = True
        with self.lock:
            self.listeners.discard(listener)

    def publish(self, keys):
        with self.lock:
            listeners = [item for item in self.listeners if item.keys.intersection(keys)]
        for item in listeners:
            try:
                item.loop.call_soon_threadsafe(item.wake)
            except RuntimeError:
                self.remove(item)


updates = Updates()


@event.listens_for(Session, "after_flush")
def collect_updates(db, _):
    keys = db.info.setdefault("realtime_keys", set())
    for obj in list(db.new) + list(db.dirty):
        changed = {attr.key for attr in inspect(obj).attrs if attr.history.has_changes()}
        if isinstance(obj, Classroom):
            keys.update((f"content:{obj.id}", f"status:{obj.id}"))
        elif isinstance(obj, Device):
            if obj in db.new or changed.intersection({"name", "state", "revoked"}):
                keys.add(f"status:{obj.class_id}")
            previous = inspect(obj).attrs.last_seen.history.deleted
            if previous and previous[0] < now() - 90:
                keys.add(f"status:{obj.class_id}")
            if changed.intersection({"control_revision", "revoked", "name"}):
                keys.add(f"device:{obj.id}")
        elif isinstance(obj, (Command, DeviceDisplay)):
            class_id = db.scalar(select(Device.class_id).where(Device.id == obj.device_id))
            keys.add(f"status:{class_id}")
            if (
                isinstance(obj, Command)
                and obj in db.new
                or isinstance(obj, DeviceDisplay)
                and changed.intersection({"request_id", "requested_at"})
            ):
                keys.add(f"device:{obj.device_id}")
        elif isinstance(obj, Meta) and obj.key == "sync_epoch":
            keys.add("epoch")


@event.listens_for(Session, "after_commit")
def committed(db):
    # 不能在 flush 时推送：客户端可能读到尚未提交的旧内容，甚至读到之后回滚的操作。
    updates.publish(db.info.pop("realtime_keys", set()))


@event.listens_for(Session, "after_rollback")
def rolled_back(db):
    db.info.pop("realtime_keys", None)


def device_access(authorization):
    # 长连接不使用 get_db，也不持有 BEGIN IMMEDIATE、连接或行锁等待事件。
    with SessionLocal(autoflush=False) as db:
        d = device_actor(authorization, db)
        return {f"content:{d.class_id}", f"device:{d.id}"}


def class_access(class_id, authorization):
    with SessionLocal(autoflush=False) as db:
        user = actor(authorization, db)
        member(db, user, class_id)
        return {f"status:{class_id}", f"content:{class_id}"}


def event_stream(check, keys):
    async def stream():
        listener = updates.subscribe(keys)
        try:
            # 订阅建立后重新读取，可覆盖首次查询与建立连接之间的发布，事件不携带教学正文。
            yield "event: ready\ndata: {}\n\n"
            while True:
                changed = False
                try:
                    await asyncio.wait_for(listener.queue.get(), timeout=20)
                    changed = True
                except TimeoutError:
                    pass
                try:
                    await run_in_threadpool(check)
                except HTTPException:
                    yield "event: revoked\ndata: {}\n\n"
                    break
                yield "event: changed\ndata: {}\n\n" if changed else ": keepalive\n\n"
        finally:
            updates.remove(listener)

    return StreamingResponse(
        stream(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache, no-transform",
            "X-Accel-Buffering": "no",
        },
    )


@router.get("/device/events")
async def device_events(authorization: str | None = Header(default=None)):
    check = lambda: device_access(authorization)
    return event_stream(check, await run_in_threadpool(check))


@router.get("/classes/{class_id}/device-events")
async def class_events(class_id: str, authorization: str | None = Header(default=None)):
    check = lambda: class_access(class_id, authorization)
    return event_stream(check, await run_in_threadpool(check))
