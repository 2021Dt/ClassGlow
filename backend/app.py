import hashlib
import json
from contextlib import asynccontextmanager
from datetime import UTC, datetime, timedelta
from pathlib import Path
from uuid import uuid4
from zoneinfo import ZoneInfo

from alembic import command
from alembic.config import Config
from fastapi import Depends, FastAPI, Header, Request
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from sqlalchemy import inspect, select
from sqlalchemy.exc import IntegrityError, SQLAlchemyError
from sqlalchemy.orm import Session

from . import schemas as S
from .account_security import LoginInput, record_login, revoke_sessions
from .config import settings
from .db import (
    Audit,
    Base,
    Classroom,
    Content,
    Draft,
    History,
    Idempotency,
    Membership,
    Meta,
    Token,
    User,
    engine,
    get_db,
    now,
    sync_epoch,
    uid,
)
from .entitlements import class_entitlement, profile, require_premium
from .middleware import BodyLimitMiddleware, body_limit
from .security import actor, bearer, check_frozen, digest, fail, frozen_detail, limit, member, passwords, secret
from .system_service import system_config


def iso(value: float) -> str:
    return datetime.fromtimestamp(value, UTC).isoformat()


def layout_default():
    return S.LayoutInput.model_validate(
        {
            "theme": "mica_light",
            "material": "subtle",
            "font": "medium",
            "iconMargin": "left",
            "interval": 20,
            "modules": dict.fromkeys(["homework", "todo", "timetable", "countdown", "message"], True),
            "reverse": False,
            "revision": 1,
        }
    ).model_dump()


@asynccontextmanager
async def lifespan(_):
    # 生产只允许运维显式迁移，防止多个进程在启动时竞争建表。
    if settings.environment == "development":
        migration = Config(str(Path(__file__).resolve().parents[1] / "alembic.ini"))
        migration.set_main_option("script_location", str(Path(__file__).resolve().parent / "migrations"))
        inspector = inspect(engine)
        if inspector.has_table("users") and not inspector.has_table("alembic_version"):
            # 兼容本轮早期 create_all 创建的开发数据库。只接受完整已知结构，不对未知库盲目打标。
            for table in Base.metadata.sorted_tables:
                actual = {column["name"] for column in inspector.get_columns(table.name)}
                if actual != set(table.columns.keys()):
                    raise RuntimeError("旧开发数据库结构不兼容，请先备份并人工迁移。")
            command.stamp(migration, "3fc3d970f5fe")
        command.upgrade(migration, "head")
        with Session(engine) as db:
            if not db.get(Meta, "sync_epoch"):
                db.add(Meta(key="sync_epoch", value=uid()))
                db.commit()
    yield


app = FastAPI(title="班级看板服务", version="0.1.0", lifespan=lifespan)
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.allowed_origins,
    allow_methods=["GET", "POST", "PUT", "PATCH", "DELETE"],
    allow_headers=["Authorization", "Content-Type", "Idempotency-Key", "If-None-Match"],
    expose_headers=["ETag", "X-Request-ID"],
)
app.add_middleware(BodyLimitMiddleware)


@app.middleware("http")
async def request_context(request: Request, call_next):
    request.state.request_id = str(uuid4())
    try:
        length = int(request.headers.get("content-length", "0"))
    except ValueError:
        return JSONResponse({"message": "请求长度无效"}, 400)
    if length > body_limit(request.url.path, request.method):
        return JSONResponse({"message": "请求过大"}, 413)
    response = await call_next(request)
    response.headers["X-Request-ID"] = request.state.request_id
    response.headers["Cache-Control"] = "no-store"
    response.headers["X-Content-Type-Options"] = "nosniff"
    return response


@app.exception_handler(RequestValidationError)
async def validation_error(request: Request, exc: RequestValidationError):
    # 默认错误包含原始输入，口令和作业正文不能被反射到诊断信息中。
    return JSONResponse(
        {
            "detail": {
                "code": "validation_failed",
                "message": "字段校验失败，请检查输入。",
                "fields": [{"field": list(e["loc"]), "message": e["msg"]} for e in exc.errors()],
                "request_id": request.state.request_id,
            }
        },
        422,
    )


@app.exception_handler(IntegrityError)
async def concurrent_write(request: Request, _):
    return JSONResponse(
        {
            "detail": {
                "code": "concurrent_write",
                "message": "记录已改变，请查询原请求结果或刷新后重试。",
                "request_id": request.state.request_id,
            }
        },
        409,
    )


@app.exception_handler(SQLAlchemyError)
async def database_unavailable(request: Request, _):
    # 不把 SQL、正文、口令哈希或连接信息放进响应和默认异常日志。
    return JSONResponse(
        {
            "detail": {
                "code": "database_unavailable",
                "message": "数据保存暂不可用，请保留输入后重试。",
                "request_id": request.state.request_id,
            }
        },
        503,
    )


def audit(db, c, user, action, data=None):
    db.add(Audit(class_id=c.id, actor=user.id, action=action, data=data or {}))


def idempotent(db: Session, user: User, key: str | None, scope: str, payload: dict):
    if not key or not 8 <= len(key) <= 100:
        fail(422, "idempotency_required", "请提供 8 至 100 字符的幂等键。")
    identity = f"{user.id}:{scope}:{key}"
    signature = hashlib.sha256(json.dumps(payload, sort_keys=True, ensure_ascii=False).encode()).hexdigest()
    existing = db.get(Idempotency, identity)
    if existing and existing.request_hash != signature:
        fail(409, "idempotency_mismatch", "同一个请求标识不能用于不同参数。")
    return identity, signature, existing.result if existing else None


def session_result(db, user):
    check_frozen(user)
    revoke_sessions(db, user, "session_replaced")
    token = secret()
    db.add(
        Token(
            digest=digest(token), user_id=user.id, expires=now() + settings.session_hours * 3600, epoch=sync_epoch(db)
        )
    )
    return {
        "token": token,
        "user": profile(user),
    }


@app.get("/health")
def health(db: Session = Depends(get_db)):
    return {"status": "ok", "syncEpoch": sync_epoch(db), "serverTime": now()}


@app.get("/client/version")
def client_version():
    # 只提供人工升级信息，不向教室端发送自动下载或重启指令。
    return {
        "latestVersion": settings.client_latest_version,
        "minimumSchema": 1,
        "downloadUrl": settings.client_download_url,
    }


@app.post("/auth/register", status_code=201)
def register(body: S.Credentials, request: Request, db: Session = Depends(get_db)):
    fail(403, "registration_closed", "账号由管理员统一创建，请联系管理员领取账号和密码。")


@app.post("/auth/login")
def login(body: LoginInput, request: Request, db: Session = Depends(get_db)):
    if not system_config(db).initialized:
        fail(409, "setup_required", "学校尚未开通，请联系管理员。")
    limit(f"login-ip:{request.client.host}", 30, 300)
    limit(f"login:{request.client.host}:{body.login.casefold()}", 10, 300)
    user = db.scalar(select(User).where(User.login == body.login.casefold()).with_for_update())
    if not user or not user.activated or not passwords.verify(body.password, user.password):
        fail(401, "invalid_credentials", "账号或口令不正确。")
    if not user.active:
        fail(403, "account_disabled", "账号已停用，请联系系统管理员。")
    if user.expires_at and user.expires_at <= now():
        fail(403, "account_expired", "账号使用期限已到，请联系系统管理员续期。数据会保留。")
    check_frozen(user)
    if record_login(db, user, request, body.clientId):
        # 拒绝登录时仍提交冻结和审计，不能用异常触发事务回滚。
        return JSONResponse({"detail": frozen_detail(user)}, 403)
    return session_result(db, user)


@app.post("/auth/logout")
def logout(authorization: str = Header(), user: User = Depends(actor), db: Session = Depends(get_db)):
    token = db.get(Token, digest(bearer(authorization)))
    if token:
        db.delete(token)
    return {"ok": True}


def class_view(c, m, db):
    return {
        "id": c.id,
        "name": c.name,
        "stage": c.stage,
        "role": m.role,
        **class_entitlement(db, c),
        "subjects": c.data["subjects"],
        "authorizedSubjects": c.data["subjects"] if m.role != "teacher" else m.subjects,
        "version": c.version,
        "revision": c.revision,
        "archived": c.archived,
        **{k: v for k, v in c.data.items() if k != "subjects"},
        "layout": S.LayoutInput.model_validate(c.data["layout"]).model_dump(),
    }


def content_view(db, content, user_id=None):
    author = db.get(User, content.author_id)
    return {
        **content.data,
        "id": content.id,
        "classId": content.class_id,
        "revision": content.revision,
        "state": content.state,
        "authorId": content.author_id,
        "author": "我" if user_id == content.author_id else author.name if author else "已离开成员",
    }


@app.get("/bootstrap")
def bootstrap(user: User = Depends(actor), db: Session = Depends(get_db)):
    memberships = db.scalars(select(Membership).where(Membership.user_id == user.id, Membership.active.is_(True))).all()
    if user.system_admin:
        memberships = [
            Membership(class_id=c.id, user_id=user.id, role="admin", subjects=c.data["subjects"])
            for c in db.scalars(select(Classroom))
        ]
    ids = [m.class_id for m in memberships]
    from .db import Command, Device
    from .devices import command_view, device_view

    devices = db.scalars(select(Device).where(Device.class_id.in_(ids), Device.revoked.is_(False))).all()
    return {
        "schema": 1,
        "user": profile(user),
        "classes": [class_view(db.get(Classroom, m.class_id), m, db) for m in memberships],
        "contents": [
            content_view(db, c, user.id)
            for c in db.scalars(select(Content).where(Content.class_id.in_(ids)).order_by(Content.id))
        ],
        "drafts": [
            {**d.data, "revision": d.revision, "updated": iso(d.updated)}
            for d in db.scalars(select(Draft).where(Draft.owner_id == user.id, Draft.closed.is_(False)))
        ],
        "devices": [device_view(d) for d in devices],
        "commands": [
            command_view(c)
            for c in db.scalars(
                select(Command)
                .where(Command.device_id.in_([d.id for d in devices]))
                .order_by(Command.issued.desc())
                .limit(100)
            )
        ],
    }


@app.get("/classes")
def list_classes(user: User = Depends(actor), db: Session = Depends(get_db)):
    if user.system_admin:
        return [class_view(c, Membership(role="admin"), db) for c in db.scalars(select(Classroom))]
    return [
        class_view(db.get(Classroom, m.class_id), m, db)
        for m in db.scalars(select(Membership).where(Membership.user_id == user.id, Membership.active.is_(True)))
    ]


@app.post("/classes", status_code=201)
def create_class(body: S.ClassInput, user: User = Depends(actor), db: Session = Depends(get_db)):
    if not (user.system_admin or user.can_create_classes):
        fail(403, "class_creation_forbidden", "创建班级需要班主任或系统管理员权限，请联系管理员分配任教班级。")
    c = Classroom(
        owner_id=user.id,
        name=body.name,
        stage=body.stage,
        data={
            "subjects": body.subjects,
            "layout": layout_default(),
            "message": "",
            "event": "",
            "eventDate": "",
            "timetable": [],
            "templates": [],
            "overrides": {},
        },
    )
    db.add(c)
    db.flush()
    m = Membership(class_id=c.id, user_id=user.id, role="owner", subjects=body.subjects)
    db.add(m)
    audit(db, c, user, "class_created")
    return class_view(c, m, db)


@app.get("/drafts")
def list_drafts(user: User = Depends(actor), db: Session = Depends(get_db)):
    return [
        {**d.data, "revision": d.revision, "updated": iso(d.updated)}
        for d in db.scalars(
            select(Draft).where(Draft.owner_id == user.id, Draft.closed.is_(False)).order_by(Draft.updated.desc())
        )
    ]


@app.put("/drafts/{draft_id}")
def save_draft(draft_id: str, body: S.DraftInput, user: User = Depends(actor), db: Session = Depends(get_db)):
    if draft_id != body.id:
        fail(422, "id_mismatch", "草稿编号不一致。")
    d = db.scalar(select(Draft).where(Draft.id == draft_id).with_for_update())
    if d and d.owner_id != user.id:
        fail(404, "draft_not_found", "草稿不存在。")
    if d and (d.closed or d.revision != body.revision):
        fail(409, "draft_revision_conflict", "草稿已改变。本次输入请保留，比较最新草稿后继续。")
    if not d and body.revision != 0:
        fail(409, "draft_revision_conflict", "草稿版本已失效。")
    data = body.model_dump(mode="json", by_alias=True)
    if not d:
        d = Draft(id=draft_id, owner_id=user.id, revision=1, data=data)
        db.add(d)
    else:
        d.revision += 1
        d.data = data
    d.updated = now()
    return {**d.data, "revision": d.revision, "updated": iso(d.updated)}


@app.delete("/drafts/{draft_id}")
def discard_draft(draft_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    d = db.get(Draft, draft_id)
    if not d or d.owner_id != user.id:
        fail(404, "draft_not_found", "草稿不存在。")
    d.closed = True
    return {"ok": True}


def authorize_subject(c, membership, subject):
    if subject == "公共提醒":
        if membership.role == "teacher":
            fail(403, "subject_forbidden", "科任老师不能发布公共提醒。")
    elif subject not in c.data["subjects"] or (membership.role == "teacher" and subject not in membership.subjects):
        fail(403, "subject_forbidden", f"未获 {c.name} 的对应学科授权。")


def edit_permission(c, m, content, user, reason):
    authorize_subject(c, m, content.data["subject"])
    if content.author_id != user.id and (m.role == "teacher" or not reason.strip()):
        fail(403, "edit_forbidden", "修改他人记录需要管理权限并填写代改原因。")


def revision_record(db, content, actor_id, reason, before):
    db.add(
        History(
            content_id=content.id,
            revision=content.revision,
            actor_id=actor_id,
            reason=reason,
            before=before,
            after={**content.data, "state": content.state},
        )
    )


@app.post("/publish-batches")
def publish(
    body: S.PublishInput,
    idempotency_key: str | None = Header(default=None),
    user: User = Depends(actor),
    db: Session = Depends(get_db),
):
    key, signature, previous = idempotent(db, user, idempotency_key, "publish", body.model_dump())
    if previous is not None:
        return previous
    d = db.scalar(select(Draft).where(Draft.id == body.draftId).with_for_update())
    if not d or d.owner_id != user.id:
        fail(404, "draft_not_found", "草稿不存在。")
    if d.closed or d.revision != body.draftRevision:
        fail(409, "draft_revision_conflict", "草稿已改变，不能发布旧正文。")
    value = S.DraftInput.model_validate({**d.data, "revision": d.revision})
    if len(value.classIds) > 1:
        require_premium(user)
    if value.kind == "homework" and value.subject == "公共提醒":
        fail(422, "subject_invalid", "作业必须选择具体学科。")
    if not value.body.strip() or len(value.body) > (500 if value.kind == "homework" else 200):
        fail(422, "body_invalid", "正文不能为空，作业最多 500 字，提醒最多 200 字。")
    if (
        value.until <= value.from_
        or (value.until - value.from_).total_seconds() > 30 * 86400
        or value.from_.timestamp() > now() + 30 * 86400
    ):
        fail(422, "display_range_invalid", "展示起止无效或超出 30 天范围。")
    # 所有目标先验证，任何失败由请求事务回滚，不能出现隐性的部分成功。
    targets = [(cid, *member(db, user, cid, write=True)) for cid in sorted(value.classIds)]
    for _, c, m in targets:
        authorize_subject(c, m, value.subject)
    source = db.get(Content, value.sourceId) if value.sourceId else None
    if value.sourceId:
        if (
            not source
            or source.class_id not in value.classIds
            or len(value.classIds) != 1
            or source.revision != value.baseRevision
            or source.state != "published"
        ):
            fail(409, "content_revision_conflict", "正式内容已改变，请比较最新记录。草稿已保留。")
        edit_permission(targets[0][1], targets[0][2], source, user, value.reason)
        if source.data["kind"] != value.kind:
            fail(422, "content_type_immutable", "不能把正式作业改为提醒，或把提醒改为作业。")
    for _, c, m in targets:
        previous_priority = bool(source and source.data.get("priority"))
        if m.role == "teacher" and value.priority != previous_priority:
            fail(403, "priority_forbidden", "仅管理员可以调整高优先级。")
        if value.priority:
            if value.kind != "todo":
                fail(422, "priority_invalid", "只有待办提醒可以设置高优先级。")
            pinned = db.scalars(select(Content).where(Content.class_id == c.id, Content.state == "published")).all()
            if sum(item.data.get("priority", False) and item.id != value.sourceId for item in pinned) >= 3:
                fail(422, "priority_limit", f"{c.name}最多保留三条未完成的高优先级提醒。")
    batch_id = uid()
    results = []
    for class_id, c, _ in targets:
        content = source or Content(class_id=class_id, author_id=user.id, revision=1, state="published", data={})
        before = {**content.data, "state": content.state} if source else {}
        if source:
            content.revision += 1
        content.data = {
            "kind": value.kind,
            "subject": value.subject,
            "body": value.body,
            "from": value.from_.isoformat(),
            "until": value.until.isoformat(),
            "due": value.due.isoformat() if value.due else "",
            "created": content.data.get("created", iso(now())),
            "batchId": batch_id,
            "reason": value.reason,
            "editorId": user.id,
            "priority": value.priority,
        }
        db.add(content)
        db.flush()
        revision_record(db, content, user.id, value.reason, before)
        c.version += 1
        results.append({"classId": class_id, "contentId": content.id, "version": c.version})
    d.closed = True
    result = {"batchId": batch_id, "results": results}
    db.add(Idempotency(key=key, request_hash=signature, result=result))
    return result


@app.get("/publish-batches/{request_key}")
def publish_result(request_key: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    if not 8 <= len(request_key) <= 100:
        fail(422, "key_invalid", "请求标识无效。")
    batch = db.get(Idempotency, f"{user.id}:publish:{request_key}")
    if not batch:
        fail(404, "result_not_found", "尚未找到发布结果，请使用原请求安全重试。")
    return batch.result


@app.post("/contents/{content_id}/{action_name}")
def content_action(
    content_id: str, action_name: str, body: S.RevisionInput, user: User = Depends(actor), db: Session = Depends(get_db)
):
    content = db.get(Content, content_id)
    if not content:
        fail(404, "content_not_found", "内容不存在。")
    c, m = member(db, user, content.class_id, write=True)
    db.refresh(content)
    edit_permission(c, m, content, user, body.reason)
    if content.revision != body.revision:
        fail(409, "content_revision_conflict", "内容已改变，请刷新后重试。")
    if action_name not in ("withdraw", "complete", "restore") or (
        action_name != "withdraw" and content.data["kind"] != "todo"
    ):
        fail(422, "action_invalid", "该内容不支持此操作。")
    if content.state == "withdrawn":
        fail(409, "content_withdrawn", "已撤回内容只能复制为新记录。")
    if (action_name in ("complete", "withdraw") and content.state != "published") or (
        action_name == "restore" and content.state != "completed"
    ):
        fail(409, "state_conflict", "内容状态已改变。")
    before = {**content.data, "state": content.state}
    if action_name == "restore" and datetime.fromisoformat(content.data["until"]).timestamp() <= now():
        if not body.until or body.until.timestamp() <= now() or body.until.timestamp() - now() > 30 * 86400:
            fail(422, "extend_display", "展示已结束，请设置未来 30 天内的结束时间。")
        content.data = {**content.data, "until": body.until.astimezone(UTC).isoformat(), "from": iso(now())}
    content.state = {"withdraw": "withdrawn", "complete": "completed", "restore": "published"}[action_name]
    content.revision += 1
    c.version += 1
    revision_record(db, content, user.id, body.reason, before)
    return content_view(db, content, user.id)


@app.get("/contents/{content_id}/history")
def history(content_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    content = db.get(Content, content_id)
    if not content:
        fail(404, "content_not_found", "内容不存在。")
    member(db, user, content.class_id)
    return [
        {
            "revision": h.revision,
            "editor": db.get(User, h.actor_id).name,
            "reason": h.reason,
            "before": h.before,
            "after": h.after,
            "at": iso(h.at),
        }
        for h in db.scalars(select(History).where(History.content_id == content_id).order_by(History.revision.desc()))
    ]


@app.put("/classes/{class_id}/layout")
def layout(class_id: str, body: S.LayoutInput, user: User = Depends(actor), db: Session = Depends(get_db)):
    c, _ = member(db, user, class_id, admin=True, write=True)
    require_premium(user)
    if c.data["layout"]["revision"] != body.revision:
        fail(409, "layout_revision_conflict", "布局已被修改，本次预览方案请保留。")
    value = body.model_dump()
    value["revision"] += 1
    before = c.data["layout"]
    c.data = {**c.data, "layout": value, "layoutLicenseUserId": user.id}
    c.version += 1

    def audit_layout(item):
        # 图片只在配置快照保存一份，审计使用指纹，避免每次移动组件都复制整张图。
        picture = item.get("backgroundImage", "")
        widgets = [
            {**widget, "image": hashlib.sha256(widget["image"].encode()).hexdigest() if widget.get("image") else ""}
            for widget in item.get("canvas", [])
        ]
        return {
            **item,
            "canvas": widgets,
            "backgroundImage": hashlib.sha256(picture.encode()).hexdigest() if picture else "",
        }

    audit(
        db,
        c,
        user,
        "layout_published",
        {"revision": value["revision"], "before": audit_layout(before), "after": audit_layout(value)},
    )
    return value


@app.put("/classes/{class_id}/display")
def display(class_id: str, body: S.DisplayInput, user: User = Depends(actor), db: Session = Depends(get_db)):
    c, _ = member(db, user, class_id, admin=True, write=True)
    if c.revision != body.revision:
        fail(409, "configuration_conflict", "班级配置已改变，请刷新。")
    if body.eventDate:
        try:
            datetime.strptime(body.eventDate, "%Y-%m-%d")
        except ValueError:
            fail(422, "date_invalid", "倒计时日期无效。")
    audit(
        db,
        c,
        user,
        "display_published",
        {
            "before": {k: c.data.get(k, "") for k in ("message", "event", "eventDate")},
            "after": body.model_dump(exclude={"revision"}),
        },
    )
    c.data = {**c.data, **body.model_dump(exclude={"revision"})}
    c.revision += 1
    c.version += 1
    return {"revision": c.revision}


@app.put("/classes/{class_id}/timetable")
def timetable(class_id: str, body: S.TimetableInput, user: User = Depends(actor), db: Session = Depends(get_db)):
    c, _ = member(db, user, class_id, admin=True, write=True)
    if c.revision != body.revision:
        fail(409, "configuration_conflict", "课表配置已改变，请刷新。")
    templates = c.data.get("templates", [])
    # 未来生效的调整截短当前模板，保留历史区间；不允许改写过去或覆盖已排期的新模板。
    updated_templates = []
    for old in templates:
        if body.effectiveFrom <= old["effectiveUntil"] and old["effectiveFrom"] <= body.effectiveUntil:
            today = datetime.now(UTC).astimezone(ZoneInfo("Asia/Shanghai")).date().isoformat()
            if body.effectiveFrom <= today or old["effectiveFrom"] >= body.effectiveFrom:
                fail(422, "term_overlap", "模板区间冲突。修改当前模板时，请选择未来生效日期。")
            old = {
                **old,
                "effectiveUntil": (datetime.fromisoformat(body.effectiveFrom).date() - timedelta(days=1)).isoformat(),
            }
        updated_templates.append(old)
    if any(p.subject not in c.data["subjects"] for ps in body.weekdays.values() for p in ps):
        fail(422, "subject_unknown", "课表包含未配置学科。")
    audit(db, c, user, "timetable_published", {"before": templates, "effectiveFrom": body.effectiveFrom})
    c.data = {**c.data, "templates": [*updated_templates, body.model_dump(exclude={"revision"})]}
    c.version += 1
    c.revision += 1
    return {"revision": c.revision}


@app.put("/classes/{class_id}/overrides")
def override(class_id: str, body: S.OverrideInput, user: User = Depends(actor), db: Session = Depends(get_db)):
    c, _ = member(db, user, class_id, admin=True, write=True)
    if body.revision != c.revision:
        fail(409, "configuration_conflict", "课表配置已改变。")
    # 复用周模板验证器，避免日期覆盖绕过节次重叠校验。
    validated_periods = S.TimetableInput(
        revision=1, effectiveFrom=body.date, effectiveUntil=body.date, weekdays={"1": body.periods}
    ).weekdays["1"]
    body.periods = validated_periods
    if any(p.subject not in c.data["subjects"] for p in body.periods):
        fail(422, "subject_unknown", "日期例外包含未配置学科。")
    value = body.model_dump(exclude={"revision"})
    audit(
        db,
        c,
        user,
        "override_published",
        {"date": body.date, "before": c.data.get("overrides", {}).get(body.date), "after": value},
    )
    c.data = {**c.data, "overrides": {**c.data.get("overrides", {}), body.date: value}}
    c.revision += 1
    c.version += 1
    return {"revision": c.revision}


from .account_security import router as security_router
from .announcements import router as announcement_router
from .devices import router as device_router
from .licenses import router as license_router
from .lifecycle import router as lifecycle_router
from .maintenance import router as maintenance_router
from .onboarding import router as onboarding_router
from .queries import router as query_router
from .system_admin import router as system_router

app.include_router(device_router)
app.include_router(maintenance_router)
app.include_router(lifecycle_router)
app.include_router(query_router)
app.include_router(system_router)
app.include_router(onboarding_router)
app.include_router(license_router)
app.include_router(security_router)
app.include_router(announcement_router)
