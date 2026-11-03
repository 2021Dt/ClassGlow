"""首次安装与全站管理接口。系统管理员不直接读取教师私人草稿。"""

import shutil
import time
from datetime import UTC, datetime
from pathlib import Path

from fastapi import APIRouter, Depends, Query
from fastapi.responses import FileResponse
from pydantic import Field
from sqlalchemy import create_engine, delete, func, or_, select, text, update
from sqlalchemy.orm import Session

from . import schemas as S
from .config import settings
from .db import (
    Base,
    Classroom,
    Command,
    Device,
    Invitation,
    Membership,
    Meta,
    Pairing,
    SystemAudit,
    SystemConfig,
    Token,
    Transfer,
    User,
    get_db,
    now,
    uid,
)
from .devices import device_view
from .entitlements import profile
from .security import fail, passwords, system_admin
from .system_service import log_system, system_config

router = APIRouter()
started_at = time.monotonic()


class SettingsInput(S.Input):
    siteName: str = Field(min_length=1, max_length=80)
    registrationOpen: bool = False  # 兼容旧客户端字段；新账号统一由管理员创建。


class UserInput(S.Credentials):
    systemAdmin: bool = False


class UserUpdate(S.Input):
    name: str = Field(min_length=1, max_length=40)
    active: bool
    systemAdmin: bool
    canCreateClasses: bool | None = None
    expiresAt: float | None = Field(default=None, ge=0, le=4102444800)


class PasswordInput(S.Input):
    password: str = Field(min_length=10, max_length=128)


class ClassUpdate(S.Input):
    name: str = Field(min_length=1, max_length=60)
    ownerId: str
    archived: bool


class RestoreInput(S.Input):
    password: str
    confirmation: str


def user_view(user):
    return {**profile(user), "active": user.active}


@router.get("/setup/status")
def setup_status(db: Session = Depends(get_db)):
    config = system_config(db)
    return {
        "initialized": config.initialized,
        "siteName": config.site_name,
        "registrationOpen": False,
    }


@router.get("/system/overview")
def overview(user: User = Depends(system_admin), db: Session = Depends(get_db)):
    config = system_config(db)
    count = lambda model, *where: db.scalar(select(func.count()).select_from(model).where(*where))
    database = db.get_bind().url
    data_file = Path(database.database) if database.get_backend_name() == "sqlite" else None
    disk = shutil.disk_usage(settings.storage_dir)
    return {
        "siteName": config.site_name,
        "registrationOpen": False,
        "publicUrl": settings.public_url,
        "database": database.get_backend_name(),
        "databaseBytes": data_file.stat().st_size if data_file and data_file.exists() else None,
        "diskFreeBytes": disk.free,
        "uptimeSeconds": int(time.monotonic() - started_at),
        "serverTime": now(),
        "version": "0.1.0",
        "initializedAt": config.initialized_at,
        "users": count(User),
        "activeUsers": count(User, User.active.is_(True)),
        "classes": count(Classroom, Classroom.archived.is_(False)),
        "archivedClasses": count(Classroom, Classroom.archived.is_(True)),
        "devices": count(Device, Device.revoked.is_(False)),
        "onlineDevices": count(Device, Device.revoked.is_(False), Device.last_seen > now() - 90),
    }


@router.put("/system/settings")
def save_settings(body: SettingsInput, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    config = system_config(db, lock=True)
    config.site_name, config.registration_open = body.siteName.strip(), False
    log_system(db, user.login, "system_settings_changed", config.site_name)
    return {"ok": True}


@router.get("/system/users")
def list_users(
    search: str = "",
    offset: int = Query(0, ge=0),
    limit: int = Query(20, ge=1, le=100),
    user: User = Depends(system_admin),
    db: Session = Depends(get_db),
):
    where = or_(User.login.contains(search, autoescape=True), User.name.contains(search, autoescape=True))
    rows = db.scalars(select(User).where(where).order_by(User.login).offset(offset).limit(limit)).all()
    return {
        "items": [user_view(row) for row in rows],
        "total": db.scalar(select(func.count()).select_from(User).where(where)),
    }


@router.post("/system/users", status_code=201)
def create_user(body: UserInput, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    target = User(
        login=body.login.casefold(),
        name=body.name.strip(),
        password=passwords.hash(body.password),
        system_admin=body.systemAdmin,
    )
    db.add(target)
    db.flush()
    log_system(db, user.login, "user_created", target.login, {"systemAdmin": target.system_admin})
    return user_view(target)


@router.get("/system/users/{user_id}")
def get_user(user_id: str, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    target = db.get(User, user_id)
    if not target:
        fail(404, "user_missing", "账号不存在。")
    return user_view(target)


@router.put("/system/users/{user_id}")
def edit_user(user_id: str, body: UserUpdate, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    target = db.get(User, user_id)
    if not target:
        fail(404, "user_missing", "账号不存在。")
    if target.id == user.id and (not body.active or not body.systemAdmin):
        fail(409, "current_admin", "不能停用或降级当前管理员，请由其他系统管理员处理。")
    target.name, target.active, target.system_admin = body.name.strip(), body.active, body.systemAdmin
    if body.canCreateClasses is not None:
        target.can_create_classes = body.canCreateClasses
    if body.expiresAt is not None:
        if target.id == user.id and body.expiresAt:
            fail(409, "current_admin", "不能给当前管理员设置到期时间，请由其他系统管理员处理。")
        target.expires_at = body.expiresAt
    if not target.active or not target.system_admin:
        db.execute(delete(Token).where(Token.user_id == target.id))
    log_system(db, user.login, "user_updated", target.login, body.model_dump())
    return user_view(target)


@router.post("/system/users/{user_id}/reset-password")
def reset_password(
    user_id: str, body: PasswordInput, user: User = Depends(system_admin), db: Session = Depends(get_db)
):
    target = db.get(User, user_id)
    if not target:
        fail(404, "user_missing", "账号不存在。")
    target.password = passwords.hash(body.password)
    # 旧版待激活账号也可由管理员直接设置登录密码，不让老师再处理激活步骤。
    target.activated = True
    db.execute(delete(Token).where(Token.user_id == target.id))
    log_system(db, user.login, "password_reset", target.login)
    return {"ok": True}


@router.get("/system/classes")
def list_classes(
    search: str = "",
    offset: int = Query(0, ge=0),
    limit: int = Query(20, ge=1, le=100),
    user: User = Depends(system_admin),
    db: Session = Depends(get_db),
):
    where = Classroom.name.contains(search, autoescape=True)
    rows = db.scalars(
        select(Classroom).where(where).order_by(Classroom.name, Classroom.id).offset(offset).limit(limit)
    ).all()
    return {
        "items": [
            {
                "id": c.id,
                "name": c.name,
                "stage": c.stage,
                "owner": user_view(db.get(User, c.owner_id)),
                "archived": c.archived,
                "version": c.version,
                "members": db.scalar(
                    select(func.count())
                    .select_from(Membership)
                    .where(Membership.class_id == c.id, Membership.active.is_(True))
                ),
                "subjects": c.data["subjects"],
            }
            for c in rows
        ],
        "total": db.scalar(select(func.count()).select_from(Classroom).where(where)),
    }


@router.put("/system/classes/{class_id}")
def edit_class(class_id: str, body: ClassUpdate, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    c = db.scalar(select(Classroom).where(Classroom.id == class_id).with_for_update())
    owner = db.get(User, body.ownerId)
    if not c or not owner or (owner.id != c.owner_id and not owner.active):
        fail(422, "class_owner_invalid", "请选择存在的班级和有效所有者。")
    if c.owner_id != owner.id:
        previous = db.scalar(select(Membership).where(Membership.class_id == c.id, Membership.role == "owner"))
        if previous:
            previous.role = "admin"
        target = db.scalar(select(Membership).where(Membership.class_id == c.id, Membership.user_id == owner.id))
        if not target:
            target = Membership(class_id=c.id, user_id=owner.id, subjects=[])
            db.add(target)
        target.role, target.active, c.owner_id = "owner", True, owner.id
        transfer = db.get(Transfer, c.id)
        if transfer:
            transfer.state = "cancelled"
    if body.archived and not c.archived:
        devices = db.scalars(select(Device).where(Device.class_id == c.id)).all()
        for device in devices:
            revoke_device(db, device)
        db.execute(update(Invitation).where(Invitation.class_id == c.id).values(state="revoked"))
        db.execute(update(Transfer).where(Transfer.class_id == c.id).values(state="cancelled"))
    c.name, c.archived = body.name.strip(), body.archived
    c.version += 1
    c.revision += 1
    log_system(db, user.login, "class_updated", c.name, body.model_dump())
    return {"ok": True}


@router.get("/system/devices")
def list_devices(
    search: str = "",
    offset: int = Query(0, ge=0),
    limit: int = Query(20, ge=1, le=100),
    user: User = Depends(system_admin),
    db: Session = Depends(get_db),
):
    query = (
        select(Device, Classroom.name)
        .join(Classroom, Classroom.id == Device.class_id)
        .where(or_(Device.name.contains(search, autoescape=True), Classroom.name.contains(search, autoescape=True)))
    )
    rows = db.execute(query.order_by(Device.last_seen.desc()).offset(offset).limit(limit)).all()
    total = db.scalar(select(func.count()).select_from(query.subquery()))
    return {"items": [{**device_view(d), "className": name} for d, name in rows], "total": total}


def revoke_device(db, device):
    device.revoked = True
    db.execute(
        update(Command)
        .where(Command.device_id == device.id, Command.state.in_(["accepted", "deferred"]))
        .values(state="rejected")
    )


@router.post("/system/devices/{device_id}/revoke")
def revoke(device_id: str, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    device = db.get(Device, device_id)
    if not device:
        fail(404, "device_missing", "设备不存在。")
    revoke_device(db, device)
    log_system(db, user.login, "device_revoked", device.name, {"deviceId": device.id})
    return {"ok": True}


@router.get("/system/audit")
def audit(
    offset: int = Query(0, ge=0),
    limit: int = Query(20, ge=1, le=100),
    user: User = Depends(system_admin),
    db: Session = Depends(get_db),
):
    rows = db.scalars(select(SystemAudit).order_by(SystemAudit.at.desc()).offset(offset).limit(limit)).all()
    return {
        "items": [
            {"id": a.id, "actor": a.actor, "action": a.action, "target": a.target, "data": a.data, "at": a.at}
            for a in rows
        ],
        "total": db.scalar(select(func.count()).select_from(SystemAudit)),
    }


def backup_dir():
    path = settings.storage_dir / "backups"
    path.mkdir(parents=True, exist_ok=True)
    return path


def backup_path(name):
    if Path(name).name != name or not name.startswith("backup-") or not name.endswith(".db"):
        fail(404, "backup_missing", "备份不存在。")
    path = backup_dir() / name
    if not path.is_file():
        fail(404, "backup_missing", "备份不存在。")
    return path


def sqlite_source(db):
    url = db.get_bind().url
    if url.get_backend_name() != "sqlite":
        fail(409, "sqlite_only", "当前数据库请使用 PostgreSQL 运维工具备份和恢复。")
    return Path(url.database)


def make_backup(db):
    from scripts.sqlite_backup import backup

    filename = f"backup-{datetime.now(UTC):%Y%m%d-%H%M%S}-{uid()[:8]}.db"
    backup(sqlite_source(db), backup_dir() / filename)
    return filename


@router.get("/system/backups")
def backups(user: User = Depends(system_admin), db: Session = Depends(get_db)):
    return {
        "supported": db.get_bind().dialect.name == "sqlite",
        "items": [
            {"name": p.name, "bytes": p.stat().st_size, "at": p.stat().st_mtime}
            for p in sorted(backup_dir().glob("backup-*.db"), reverse=True)
        ],
    }


@router.post("/system/backups", status_code=201)
def create_backup(user: User = Depends(system_admin), db: Session = Depends(get_db)):
    name = make_backup(db)
    log_system(db, user.login, "backup_created", name)
    return {"name": name}


@router.get("/system/backups/{name}/download")
def download_backup(name: str, user: User = Depends(system_admin)):
    return FileResponse(backup_path(name), filename=name, media_type="application/octet-stream")


@router.post("/system/backups/{name}/restore")
def restore_backup(name: str, body: RestoreInput, user: User = Depends(system_admin), db: Session = Depends(get_db)):
    sqlite_source(db)
    if body.confirmation != "恢复备份" or not passwords.verify(body.password, user.password):
        fail(403, "restore_confirmation", "请填写当前管理员口令并输入“恢复备份”。")
    source_path = backup_path(name)
    from scripts.sqlite_backup import checked_source

    checked_source(source_path).close()
    source_engine = create_engine("sqlite:///" + source_path.resolve().as_posix())
    actor_name = user.login
    try:
        with source_engine.connect() as source:
            current_revision = db.scalar(text("SELECT version_num FROM alembic_version"))
            if source.scalar(text("SELECT version_num FROM alembic_version")) != current_revision:
                fail(409, "backup_version", "备份版本与服务不一致，请先使用命令行迁移该备份。")
            cfg = source.execute(select(SystemConfig.__table__)).mappings().first()
            admins = source.scalar(
                select(func.count()).select_from(User).where(User.active.is_(True), User.system_admin.is_(True))
            )
            if not cfg or not cfg["initialized"] or not admins:
                fail(409, "backup_admin_missing", "备份没有可登录的系统管理员。")
            before = make_backup(db)
            # 单次数据库事务完成替换，其他请求等待事务提交；恢复前副本留在备份列表。
            tables = Base.metadata.sorted_tables
            for table in reversed(tables):
                db.execute(delete(table))
            for table in tables:
                rows = [dict(row) for row in source.execute(select(table)).mappings()]
                if rows:
                    db.execute(table.insert(), rows)
            db.execute(update(Meta).where(Meta.key == "sync_epoch").values(value=uid()))
            db.execute(delete(Token))
            db.execute(update(Device).values(revoked=True))
            db.execute(update(Pairing).values(expires=0))
            db.execute(update(Command).where(Command.state.in_(["accepted", "deferred"])).values(state="rejected"))
            log_system(db, actor_name, "backup_restored", name, {"beforeRestore": before})
            return {
                "ok": True,
                "beforeRestore": before,
                "message": "恢复完成，请使用备份中的管理员账号重新登录；教室设备需重新绑定。",
            }
    finally:
        source_engine.dispose()
