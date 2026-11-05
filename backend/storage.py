"""管理员存储维护与明确确认后的永久删除。"""

import re
from pathlib import Path
from typing import Literal
from urllib.parse import unquote, urlsplit

from fastapi import APIRouter, Depends
from pydantic import Field
from sqlalchemy import delete, func, or_, select, update
from sqlalchemy.orm import Session

from .config import settings
from .db import (
    AccountActivation,
    Announcement,
    AnnouncementReceipt,
    AnnouncementTarget,
    Audit,
    Classroom,
    Command,
    Content,
    Device,
    DeviceDisplay,
    Draft,
    FeatureLicense,
    History,
    Idempotency,
    Invitation,
    LoginRecord,
    Membership,
    Pairing,
    PasswordRecovery,
    SystemAudit,
    Token,
    Transfer,
    User,
    get_db,
    now,
)
from .schemas import Input
from .security import fail, system_admin
from .system_service import log_system

router = APIRouter()
RETENTION_DAYS = 30
ResourceKind = Literal["users", "classes", "devices", "announcements"]


class DeleteInput(Input):
    confirmation: str = Field(min_length=1, max_length=200)


class CleanupInput(DeleteInput):
    category: Literal["records", "backups"]


def expired_records(cutoff):
    return [
        (Audit, Audit.at < cutoff, "班级操作日志"),
        (SystemAudit, SystemAudit.at < cutoff, "系统操作日志"),
        (LoginRecord, LoginRecord.at < cutoff, "登录记录"),
        (Command, Command.expires < cutoff, "过期设备指令"),
        (Token, Token.expires < cutoff, "过期登录凭据"),
        (Pairing, Pairing.expires < cutoff, "过期配对记录"),
        (PasswordRecovery, PasswordRecovery.expires < cutoff, "过期密码恢复记录"),
    ]


def count(db, model, *where):
    return db.scalar(select(func.count()).select_from(model).where(*where))


def clean_records(db, timestamp=None):
    timestamp = timestamp if timestamp is not None else now()
    cutoff = timestamp - RETENTION_DAYS * 86400
    removed = {}
    for model, condition, label in expired_records(cutoff):
        removed[label] = db.execute(delete(model).where(condition)).rowcount
    # 已关闭草稿保留小型占位，阻止旧页面以同一个 ID 再次创建和发布。
    removed["已关闭草稿正文"] = db.execute(
        update(Draft).where(Draft.closed.is_(True), Draft.updated < cutoff, Draft.data != {}).values(data={})
    ).rowcount
    # 提醒最长 1 小时；过期一天后释放内嵌图片，保留执行记录到 30 天。
    expired_canvases = db.scalars(select(Command).where(*canvas_cleanup_conditions(timestamp))).yield_per(20)
    released = 0
    for command in expired_canvases:
        command.data = {
            **command.data,
            "notice": {k: v for k, v in command.data["notice"].items() if k != "canvas"},
        }
        released += 1
        if released % 20 == 0:
            db.flush()
    db.flush()
    removed["过期提醒画布"] = released
    return removed


def canvas_cleanup_conditions(timestamp):
    return (Command.expires < timestamp - 86400, Command.data["notice"]["canvas"].as_string().is_not(None))


def old_backups(db):
    from .system_admin import backup_dir

    source = db.get_bind().url.database if db.get_bind().dialect.name == "sqlite" else None
    files = [p for p in backup_dir().glob("backup-*.db") if p.is_file() and not p.is_symlink()]
    files.sort(key=lambda p: (p.stat().st_mtime, p.name), reverse=True)
    return [
        p
        for p in files[3:]
        if p.stat().st_mtime < now() - RETENTION_DAYS * 86400 and (not source or p.resolve() != Path(source).resolve())
    ]


PACKAGE_PATTERN = re.compile(r"ClassGlow-(\d+)\.(\d+)\.(\d+)-win-x64-setup\.exe")


def download_files():
    folder = settings.downloads_dir
    if folder is None or not folder.is_dir() or folder.is_symlink():
        return []
    files = []
    for path in folder.iterdir():
        match = PACKAGE_PATTERN.fullmatch(path.name)
        if match and path.is_file() and not path.is_symlink():
            files.append((tuple(map(int, match.groups())), path))
    current = tuple(map(int, settings.client_latest_version.split(".")))
    published_name = Path(unquote(urlsplit(settings.client_download_url).path)).name
    # 当前下载地址可能由运维固定为不同版本，同时保护它及当前版的最近回退版本。
    older = [version for version, _ in files if version < current]
    previous = max(older) if older else None
    return [
        {
            "name": p.name,
            "bytes": p.stat().st_size,
            "at": p.stat().st_mtime,
            "protected": version >= current or version == previous or p.name == published_name,
        }
        for version, p in sorted(files, reverse=True)
    ]


@router.get("/system/storage")
def storage_report(user: User = Depends(system_admin), db: Session = Depends(get_db)):
    cutoff = now() - RETENTION_DAYS * 86400
    backups = old_backups(db)
    return {
        "retentionDays": RETENTION_DAYS,
        "records": [
            {"label": label, "count": count(db, model, condition)}
            for model, condition, label in expired_records(cutoff)
        ]
        + [
            {"label": "过期提醒画布", "count": count(db, Command, *canvas_cleanup_conditions(now()))},
            {
                "label": "已关闭草稿正文",
                "count": count(db, Draft, Draft.closed.is_(True), Draft.updated < cutoff, Draft.data != {}),
            },
        ],
        "backups": {"count": len(backups), "bytes": sum(p.stat().st_size for p in backups)},
        "downloads": download_files(),
        "downloadsSupported": settings.downloads_dir is not None,
    }


@router.post("/system/storage/cleanup")
def cleanup(body: CleanupInput, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    if body.confirmation != "清理过期数据":
        fail(422, "cleanup_confirmation", "请输入“清理过期数据”确认。")
    if body.category == "records":
        result = {"removed": clean_records(db)}
    else:
        files = old_backups(db)
        released = 0
        for path in files:
            size = path.stat().st_size
            path.unlink(missing_ok=True)
            released += size
        result = {"removed": {"历史备份": len(files)}, "bytes": released}
    log_system(db, user.login, "storage_cleaned", body.category, result)
    return result


@router.delete("/system/storage/downloads/{name}")
def delete_download(name: str, body: DeleteInput, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    if body.confirmation != name or not PACKAGE_PATTERN.fullmatch(name):
        fail(422, "delete_confirmation", "请核对安装包名称。")
    entry = next((item for item in download_files() if item["name"] == name), None)
    if entry is None:
        return {"ok": True}
    if entry["protected"]:
        fail(409, "download_protected", "当前发布包、待发布版本及最近一个回退版本需要保留。")
    (settings.downloads_dir / name).unlink(missing_ok=True)
    log_system(db, user.login, "download_deleted", name, {"bytes": entry["bytes"]})
    return {"ok": True}


def deletion_target(db, kind, resource_id):
    model = {"users": User, "classes": Classroom, "devices": Device, "announcements": Announcement}[kind]
    return db.scalar(select(model).where(model.id == resource_id).with_for_update())


def deletion_details(db, kind, target, actor):
    blocked = ""
    if kind == "users":
        name = target.login
        if target.id == actor.id:
            blocked = "当前登录的管理员账号不能删除。"
        elif target.active:
            blocked = "请先停用账号，再永久删除。"
        elif count(db, Classroom, Classroom.owner_id == target.id):
            blocked = "请先转移或删除该账号负责的全部班级。"
        rows = [
            ("已发布内容及其历史", count(db, Content, Content.author_id == target.id)),
            ("私人草稿", count(db, Draft, Draft.owner_id == target.id)),
            ("班级成员关系", count(db, Membership, Membership.user_id == target.id)),
        ]
    elif kind == "classes":
        name = target.name
        if not target.archived:
            blocked = "请先归档班级，再永久删除。"
        rows = [
            ("已发布内容及其历史", count(db, Content, Content.class_id == target.id)),
            ("教室设备", count(db, Device, Device.class_id == target.id)),
            ("班级成员关系", count(db, Membership, Membership.class_id == target.id)),
        ]
    elif kind == "devices":
        name = target.name
        if not target.revoked:
            blocked = "请先解绑电脑，再永久删除设备记录。"
        rows = [
            ("操作记录", count(db, Command, Command.device_id == target.id)),
            ("画面回传", count(db, DeviceDisplay, DeviceDisplay.device_id == target.id)),
        ]
    else:
        name = target.title
        if target.withdrawn_at is None:
            blocked = "请先撤回公告，再永久删除。"
        rows = [
            ("公告正文", 1),
            ("领取记录", count(db, AnnouncementReceipt, AnnouncementReceipt.announcement_id == target.id)),
        ]
    return {
        "name": name,
        "blockedReason": blocked,
        "counts": [{"label": label, "count": value} for label, value in rows],
    }


@router.get("/system/deletion/{kind}/{resource_id}")
def preview_deletion(
    kind: ResourceKind, resource_id: str, user: User = Depends(system_admin), db: Session = Depends(get_db)
):
    target = deletion_target(db, kind, resource_id)
    if target is None:
        fail(404, "delete_missing", "对象已经不存在。")
    return deletion_details(db, kind, target, user)


def remove_devices(db, device_ids):
    db.execute(delete(DeviceDisplay).where(DeviceDisplay.device_id.in_(device_ids)))
    db.execute(delete(Command).where(Command.device_id.in_(device_ids)))
    db.execute(delete(Pairing).where(Pairing.device_id.in_(device_ids)))
    db.execute(delete(Device).where(Device.id.in_(device_ids)))


def remove_contents(db, content_ids):
    db.execute(delete(History).where(History.content_id.in_(content_ids)))
    db.execute(delete(Content).where(Content.id.in_(content_ids)))


@router.delete("/system/deletion/{kind}/{resource_id}")
def permanent_delete(
    kind: ResourceKind,
    resource_id: str,
    body: DeleteInput,
    user: User = Depends(system_admin),
    db: Session = Depends(get_db),
):
    target = deletion_target(db, kind, resource_id)
    if target is None:
        return {"ok": True}
    details = deletion_details(db, kind, target, user)
    if details["blockedReason"]:
        fail(409, "delete_blocked", details["blockedReason"])
    if body.confirmation != details["name"]:
        fail(422, "delete_confirmation", "确认名称与当前对象不一致，请刷新后核对。")
    if kind == "classes":
        remove_contents(db, select(Content.id).where(Content.class_id == target.id))
        remove_devices(db, select(Device.id).where(Device.class_id == target.id))
        for model in (Membership, Invitation, Transfer, Audit):
            db.execute(delete(model).where(model.class_id == target.id))
        for draft in db.scalars(select(Draft).with_for_update()):
            classes = draft.data.get("classIds", [])
            if target.id in classes:
                remaining = [cid for cid in classes if cid != target.id]
                draft.data = {**draft.data, "classIds": remaining} if remaining else {}
                draft.closed = draft.closed or not remaining
                draft.revision += 1
        db.delete(target)
    elif kind == "users":
        affected = select(Content.class_id).where(Content.author_id == target.id).distinct()
        classes = db.scalars(
            select(Classroom).where(Classroom.id.in_(affected)).order_by(Classroom.id).with_for_update()
        ).all()
        remove_contents(db, select(Content.id).where(Content.author_id == target.id))
        db.execute(delete(History).where(History.actor_id == target.id))
        db.execute(delete(Command).where(Command.actor_id == target.id))
        for model in (
            AccountActivation,
            FeatureLicense,
            Token,
            LoginRecord,
            PasswordRecovery,
            Membership,
            AnnouncementReceipt,
            AnnouncementTarget,
        ):
            db.execute(delete(model).where(model.user_id == target.id))
        db.execute(delete(Draft).where(Draft.owner_id == target.id))
        db.execute(delete(Invitation).where(Invitation.applicant == target.id))
        db.execute(delete(Transfer).where(or_(Transfer.sender == target.id, Transfer.recipient == target.id)))
        db.execute(delete(Idempotency).where(Idempotency.key.startswith(target.id + ":", autoescape=True)))
        db.execute(delete(Audit).where(Audit.actor == target.id))
        for classroom in classes:
            classroom.version += 1
        db.delete(target)
    elif kind == "devices":
        remove_devices(db, [target.id])
    else:
        db.execute(delete(AnnouncementReceipt).where(AnnouncementReceipt.announcement_id == target.id))
        db.execute(delete(AnnouncementTarget).where(AnnouncementTarget.announcement_id == target.id))
        # 保留幂等键占位，删除正文后重试旧发布请求也不会重新创建公告。
        db.execute(
            update(Idempotency)
            .where(Idempotency.result["id"].as_string() == target.id)
            .values(result={"_deleted": True})
        )
        db.delete(target)
    log_system(db, user.login, "permanent_deleted", details["name"], {"kind": kind, "id": resource_id})
    return {"ok": True}
