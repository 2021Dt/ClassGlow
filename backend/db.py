from datetime import UTC, datetime
from uuid import uuid4

from sqlalchemy import JSON, Boolean, Float, ForeignKey, Integer, String, UniqueConstraint, create_engine, event
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


class Token(Base):
    __tablename__ = "sessions"
    digest: Mapped[str] = mapped_column(String(64), primary_key=True)
    user_id: Mapped[str] = mapped_column(ForeignKey("users.id"))
    expires: Mapped[float] = mapped_column(Float)
    epoch: Mapped[str] = mapped_column(String(36))


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
    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=uid)
    class_id: Mapped[str] = mapped_column(ForeignKey("classrooms.id"), index=True)
    author_id: Mapped[str] = mapped_column(ForeignKey("users.id"))
    revision: Mapped[int] = mapped_column(Integer, default=1)
    state: Mapped[str] = mapped_column(String(12), default="published")
    data: Mapped[dict] = mapped_column(JSON)


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

if engine.dialect.name == "sqlite":

    @event.listens_for(engine, "connect")
    def enable_sqlite_constraints(connection, _):
        # SQLite 默认不执行外键检查，显式启用，避免本地数据质量低于后续 PostgreSQL。
        cursor = connection.cursor()
        cursor.execute("PRAGMA foreign_keys=ON")
        cursor.execute("PRAGMA busy_timeout=30000")
        cursor.close()


def get_db():
    with SessionLocal() as session:
        try:
            # SQLite 无行级锁，单机部署先取事务锁；切换 PostgreSQL 后由行锁承担并发控制。
            if engine.dialect.name == "sqlite":
                session.connection().exec_driver_sql("BEGIN IMMEDIATE")
            yield session
            session.commit()
        except Exception:
            session.rollback()
            raise


def sync_epoch(db: Session) -> str:
    value = db.get(Meta, "sync_epoch")
    if value is None:
        raise RuntimeError("数据库尚未迁移或同步世代未初始化。")
    return value.value
