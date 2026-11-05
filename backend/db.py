from datetime import UTC, datetime
from uuid import uuid4

from fastapi import Request
from sqlalchemy import (
    JSON,
    Boolean,
    Float,
    ForeignKey,
    Index,
    Integer,
    LargeBinary,
    String,
    UniqueConstraint,
    create_engine,
    event,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, Session, mapped_column, sessionmaker

from .config import settings


def now() -> float:
    return datetime.now(UTC).timestamp()


def uid() -> str:
    return str(uuid4())


class Base(DeclarativeBase):
    pass


class User(Base):
    __tablename__ = "users"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    login: Mapped[str] = mapped_column(String(80), unique=True)
    name: Mapped[str] = mapped_column(String(40))
    password: Mapped[str] = mapped_column(String(300))
    active: Mapped[bool] = mapped_column(Boolean, default=True)
    system_admin: Mapped[bool] = mapped_column(Boolean, default=False)
    activated: Mapped[bool] = mapped_column(Boolean, default=True)
    # 旧账号保留原有建班能力；新开通的科任教师由管理员明确设为 False。
    can_create_classes: Mapped[bool] = mapped_column(Boolean, default=True)
    expires_at: Mapped[float] = mapped_column(Float, default=0)
    premium_until: Mapped[float] = mapped_column(Float, default=0)
    # 安全冻结独立于停用、账号期限和许可；时间到期后也不会恢复旧会话。
    frozen_until: Mapped[float] = mapped_column(Float, default=0)
    freeze_reason: Mapped[str] = mapped_column(String(200), default="")
    security_reviewed_at: Mapped[float] = mapped_column(Float, default=0)


class AccountActivation(Base):
    __tablename__ = "account_activations"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    user_id: Mapped[str] = mapped_column(ForeignKey("users.id"), index=True)
    code_hash: Mapped[str] = mapped_column(String(64), unique=True)
    epoch: Mapped[str] = mapped_column(String(36))
    expires: Mapped[float] = mapped_column(Float)
    used_at: Mapped[float] = mapped_column(Float, default=0)
    revoked: Mapped[bool] = mapped_column(Boolean, default=False)


class FeatureLicense(Base):
    __tablename__ = "feature_licenses"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    user_id: Mapped[str] = mapped_column(ForeignKey("users.id"), index=True)
    code_hash: Mapped[str] = mapped_column(String(64), unique=True)
    epoch: Mapped[str] = mapped_column(String(36))
    days: Mapped[int] = mapped_column(Integer)
    expires: Mapped[float] = mapped_column(Float)
    used_at: Mapped[float] = mapped_column(Float, default=0)
    revoked: Mapped[bool] = mapped_column(Boolean, default=False)


class Token(Base):
    __tablename__ = "sessions"
    __table_args__ = (
        Index("uq_sessions_id", "id", unique=True),
        Index("ix_sessions_user_expires", "user_id", "expires"),
    )
    digest: Mapped[str] = mapped_column(String(64), primary_key=True)
    user_id: Mapped[str] = mapped_column(ForeignKey("users.id"))
    expires: Mapped[float] = mapped_column(Float)
    epoch: Mapped[str] = mapped_column(String(36))
    revoked_reason: Mapped[str] = mapped_column(String(40), default="")
    id: Mapped[str] = mapped_column(String(36), default=uid)
    client_hash: Mapped[str] = mapped_column(String(64), default="")
    label: Mapped[str] = mapped_column(String(80), default="浏览器")
    created_at: Mapped[float] = mapped_column(Float, default=now)


class LoginRecord(Base):
    """只记录成功验证密码的登录环境，供短期风险判断，不保存密码或会话明文。"""

    __tablename__ = "login_records"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    user_id: Mapped[str] = mapped_column(ForeignKey("users.id"), index=True)
    at: Mapped[float] = mapped_column(Float, default=now, index=True)
    client_hash: Mapped[str] = mapped_column(String(64))
    ip: Mapped[str] = mapped_column(String(64))


class PasswordRecovery(Base):
    __tablename__ = "password_recoveries"
    user_id: Mapped[str] = mapped_column(ForeignKey("users.id"), primary_key=True)
    code_hash: Mapped[str] = mapped_column(String(64), unique=True)
    epoch: Mapped[str] = mapped_column(String(36))
    expires: Mapped[float] = mapped_column(Float)
    attempts: Mapped[int] = mapped_column(Integer, default=0)


class Classroom(Base):
    __tablename__ = "classrooms"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    owner_id: Mapped[str] = mapped_column(ForeignKey("users.id"))
    name: Mapped[str] = mapped_column(String(60))
    stage: Mapped[str] = mapped_column(String(20), default="小学")
    archived: Mapped[bool] = mapped_column(Boolean, default=False)
    version: Mapped[int] = mapped_column(Integer, default=1)
    revision: Mapped[int] = mapped_column(Integer, default=1)
    data: Mapped[dict] = mapped_column(JSON, default=dict)


class Membership(Base):
    __tablename__ = "memberships"
    __table_args__ = (UniqueConstraint("class_id", "user_id"),)
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    class_id: Mapped[str] = mapped_column(ForeignKey("classrooms.id"), index=True)
    user_id: Mapped[str] = mapped_column(ForeignKey("users.id"))
    role: Mapped[str] = mapped_column(String(10))
    subjects: Mapped[list] = mapped_column(JSON, default=list)
    active: Mapped[bool] = mapped_column(Boolean, default=True)


class ClassAsset(Base):
    """图片按班级和内容摘要去重，随数据库一致备份，不允许跨班读取。"""

    __tablename__ = "class_assets"
    class_id: Mapped[str] = mapped_column(ForeignKey("classrooms.id"), primary_key=True)
    digest: Mapped[str] = mapped_column(String(64), primary_key=True)
    mime: Mapped[str] = mapped_column(String(32))
    data: Mapped[bytes] = mapped_column(LargeBinary, deferred=True)


class Draft(Base):
    __tablename__ = "drafts"
    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    owner_id: Mapped[str] = mapped_column(ForeignKey("users.id"), index=True)
    revision: Mapped[int] = mapped_column(Integer, default=1)
    closed: Mapped[bool] = mapped_column(Boolean, default=False)
    data: Mapped[dict] = mapped_column(JSON)
    updated: Mapped[float] = mapped_column(Float, default=now)


class Content(Base):
    __tablename__ = "contents"
    __table_args__ = (
        Index("ix_contents_class_state_until", "class_id", "state", "display_until"),
        Index("ix_contents_class_created", "class_id", "created_at", "id"),
    )
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    class_id: Mapped[str] = mapped_column(ForeignKey("classrooms.id"), index=True)
    author_id: Mapped[str] = mapped_column(ForeignKey("users.id"))
    revision: Mapped[int] = mapped_column(Integer, default=1)
    state: Mapped[str] = mapped_column(String(12), default="published")
    data: Mapped[dict] = mapped_column(JSON)
    display_from: Mapped[float] = mapped_column(Float, default=0)
    display_until: Mapped[float] = mapped_column(Float, default=0)
    created_at: Mapped[float] = mapped_column(Float, default=0)


@event.listens_for(Content, "before_insert")
@event.listens_for(Content, "before_update")
def content_times(_, __, content):
    # 索引列始终从同一份业务数据派生，历史接口与展示过滤不能各维护一套时间。
    for field, key in (("display_from", "from"), ("display_until", "until"), ("created_at", "created")):
        value = content.data.get(key)
        setattr(content, field, datetime.fromisoformat(value).timestamp() if value else 0)


class History(Base):
    __tablename__ = "content_revisions"
    __table_args__ = (UniqueConstraint("content_id", "revision"),)
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    content_id: Mapped[str] = mapped_column(ForeignKey("contents.id"), index=True)
    revision: Mapped[int] = mapped_column(Integer)
    actor_id: Mapped[str] = mapped_column(ForeignKey("users.id"))
    reason: Mapped[str] = mapped_column(String(100), default="")
    before: Mapped[dict] = mapped_column(JSON)
    after: Mapped[dict] = mapped_column(JSON)
    at: Mapped[float] = mapped_column(Float, default=now)


class Idempotency(Base):
    __tablename__ = "idempotency"
    key: Mapped[str] = mapped_column(String(200), primary_key=True)
    request_hash: Mapped[str] = mapped_column(String(64))
    result: Mapped[dict] = mapped_column(JSON)


class Pairing(Base):
    __tablename__ = "pairings"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    code_hash: Mapped[str] = mapped_column(String(64), unique=True)
    secret_hash: Mapped[str] = mapped_column(String(64))
    name: Mapped[str] = mapped_column(String(60))
    expires: Mapped[float] = mapped_column(Float)
    consumed: Mapped[bool] = mapped_column(Boolean, default=False)
    device_id: Mapped[str | None] = mapped_column(String(36), nullable=True)
    credential: Mapped[str | None] = mapped_column(String(200), nullable=True)


class Device(Base):
    __tablename__ = "devices"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    class_id: Mapped[str] = mapped_column(ForeignKey("classrooms.id"), index=True)
    name: Mapped[str] = mapped_column(String(60))
    binding: Mapped[str] = mapped_column(String(36), default=uid)
    credential_hash: Mapped[str] = mapped_column(String(64), unique=True)
    epoch: Mapped[str] = mapped_column(String(36))
    revoked: Mapped[bool] = mapped_column(Boolean, default=False)
    last_seen: Mapped[float] = mapped_column(Float, default=now)
    control_revision: Mapped[int] = mapped_column(Integer, default=0)
    report_seq: Mapped[int] = mapped_column(Integer, default=0)
    state: Mapped[dict] = mapped_column(JSON, default=dict)


class DeviceDisplay(Base):
    """每台设备只保留最近一次回传；二进制字段可随 SQLite / PostgreSQL 迁移。"""

    __tablename__ = "device_displays"
    device_id: Mapped[str] = mapped_column(ForeignKey("devices.id"), primary_key=True)
    request_id: Mapped[str] = mapped_column(String(36), default="")
    requested_at: Mapped[float] = mapped_column(Float, default=0)
    received_at: Mapped[float] = mapped_column(Float, default=0)
    data: Mapped[dict] = mapped_column(JSON, default=dict)
    image: Mapped[bytes] = mapped_column(LargeBinary, default=b"", deferred=True)


class Command(Base):
    __tablename__ = "device_commands"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    device_id: Mapped[str] = mapped_column(ForeignKey("devices.id"), index=True)
    actor_id: Mapped[str] = mapped_column(ForeignKey("users.id"))
    binding: Mapped[str] = mapped_column(String(36))
    epoch: Mapped[str] = mapped_column(String(36))
    seq: Mapped[int] = mapped_column(Integer)
    action: Mapped[str] = mapped_column(String(16))
    group: Mapped[str] = mapped_column(String(12))
    issued: Mapped[float] = mapped_column(Float, default=now)
    expires: Mapped[float] = mapped_column(Float)
    state: Mapped[str] = mapped_column(String(20), default="accepted")
    data: Mapped[dict] = mapped_column(JSON)


class Invitation(Base):
    __tablename__ = "invitations"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    class_id: Mapped[str] = mapped_column(ForeignKey("classrooms.id"))
    digest: Mapped[str] = mapped_column(String(64), unique=True)
    subjects: Mapped[list] = mapped_column(JSON)
    applicant: Mapped[str | None] = mapped_column(String(36), nullable=True)
    state: Mapped[str] = mapped_column(String(16), default="open")
    expires: Mapped[float] = mapped_column(Float)


class Transfer(Base):
    __tablename__ = "ownership_transfers"
    # 每班保留一条当前请求，避免并发产生多个有效转移。
    class_id: Mapped[str] = mapped_column(ForeignKey("classrooms.id"), primary_key=True)
    id: Mapped[str] = mapped_column(String(36), default=uid, unique=True)
    sender: Mapped[str] = mapped_column(String(36))
    recipient: Mapped[str] = mapped_column(String(36))
    state: Mapped[str] = mapped_column(String(16), default="pending")
    expires: Mapped[float] = mapped_column(Float)


class Meta(Base):
    __tablename__ = "metadata"
    key: Mapped[str] = mapped_column(String(60), primary_key=True)
    value: Mapped[str] = mapped_column(String(200))


class SystemConfig(Base):
    __tablename__ = "system_config"
    id: Mapped[int] = mapped_column(Integer, primary_key=True, default=1)
    initialized: Mapped[bool] = mapped_column(Boolean, default=False)
    site_name: Mapped[str] = mapped_column(String(80), default="班级看板")
    registration_open: Mapped[bool] = mapped_column(Boolean, default=False)
    initialized_at: Mapped[float] = mapped_column(Float, default=0)


class SystemAudit(Base):
    __tablename__ = "system_audit"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    actor: Mapped[str] = mapped_column(String(80))
    action: Mapped[str] = mapped_column(String(60))
    target: Mapped[str] = mapped_column(String(200), default="")
    data: Mapped[dict] = mapped_column(JSON, default=dict)
    at: Mapped[float] = mapped_column(Float, default=now)


class Announcement(Base):
    __tablename__ = "announcements"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    title: Mapped[str] = mapped_column(String(120))
    markdown: Mapped[str] = mapped_column(String(16000))
    scope: Mapped[str] = mapped_column(String(10))
    published_at: Mapped[float] = mapped_column(Float, default=now, index=True)
    withdrawn_at: Mapped[float | None] = mapped_column(Float, default=None)


class AnnouncementTarget(Base):
    __tablename__ = "announcement_targets"
    announcement_id: Mapped[str] = mapped_column(ForeignKey("announcements.id"), primary_key=True)
    user_id: Mapped[str] = mapped_column(ForeignKey("users.id"), primary_key=True)


class AnnouncementReceipt(Base):
    __tablename__ = "announcement_receipts"
    __table_args__ = (UniqueConstraint("user_id", "claim_key", name="uq_announcement_receipt_claim"),)
    announcement_id: Mapped[str] = mapped_column(ForeignKey("announcements.id"), primary_key=True)
    user_id: Mapped[str] = mapped_column(ForeignKey("users.id"), primary_key=True)
    shown_at: Mapped[float] = mapped_column(Float, default=now)
    claim_key: Mapped[str | None] = mapped_column(String(36))


class Audit(Base):
    __tablename__ = "audit_log"
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    class_id: Mapped[str] = mapped_column(ForeignKey("classrooms.id"), index=True)
    actor: Mapped[str] = mapped_column(String(36))
    action: Mapped[str] = mapped_column(String(60))
    data: Mapped[dict] = mapped_column(JSON, default=dict)
    at: Mapped[float] = mapped_column(Float, default=now)


engine = create_engine(
    settings.database_url,
    hide_parameters=True,
    pool_pre_ping=True,
    **(
        {"connect_args": {"check_same_thread": False, "timeout": 30}}
        if settings.database_url.startswith("sqlite")
        else {}
    ),
)
SessionLocal = sessionmaker(engine, expire_on_commit=False)


def configure_sqlite(target):
    if target.dialect.name != "sqlite":
        return

    @event.listens_for(target, "connect")
    def enable_sqlite_constraints(connection, _):
        # SQLite 默认不执行外键检查，显式启用，避免本地数据质量低于后续 PostgreSQL。
        cursor = connection.cursor()
        cursor.execute("PRAGMA foreign_keys=ON")
        cursor.execute("PRAGMA busy_timeout=30000")
        # WAL 允许查询与写入并行；备份继续使用 SQLite 原生一致性备份 API。
        cursor.execute("PRAGMA journal_mode=WAL")
        cursor.close()


configure_sqlite(engine)


def request_is_read_only(request: Request) -> bool:
    # 快照会更新联系时间和命令结果，其余 GET 的展示状态只计算、不落库。
    path = getattr(request.scope.get("route"), "path", request.url.path)
    return request.method in ("GET", "HEAD", "OPTIONS") and path != "/device/snapshot"


def database_session(factory, *, read_only=False):
    with factory(autoflush=not read_only) as session:
        session.info["read_only"] = read_only
        try:
            # SQLite 无行级锁，单机部署先取事务锁；切换 PostgreSQL 后由行锁承担并发控制。
            if session.get_bind().dialect.name == "sqlite":
                session.connection().exec_driver_sql("BEGIN" if read_only else "BEGIN IMMEDIATE")
            yield session
            if read_only:
                session.rollback()
            else:
                session.commit()
        except Exception:
            session.rollback()
            raise


def get_db(request: Request):
    yield from database_session(SessionLocal, read_only=request_is_read_only(request))


def sync_epoch(db: Session) -> str:
    value = db.get(Meta, "sync_epoch")
    if value is None:
        raise RuntimeError("数据库尚未迁移或同步世代未初始化。")
    return value.value
