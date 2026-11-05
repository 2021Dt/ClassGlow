"""账号临时冻结与恢复。浏览器标识和 IP 只是风险信号，不能证明账号外借。"""

import secrets
from typing import Literal

from fastapi import APIRouter, Depends, Request
from fastapi.responses import JSONResponse
from pydantic import Field
from sqlalchemy import delete, select, update
from sqlalchemy.orm import Session

from . import schemas as S
from .db import Command, LoginRecord, PasswordRecovery, Token, User, get_db, now, sync_epoch
from .security import digest, fail, limit, passwords, system_admin
from .system_service import log_system

router = APIRouter()


class LoginInput(S.Credentials):
    # 不采集硬件指纹；随机浏览器标识可被清除，仅用于辅助判断。
    clientId: str = Field(default="", max_length=80, pattern=r"^[a-zA-Z0-9_-]*$")


class FreezeInput(S.Input):
    minutes: Literal[30, 120, 1440] = 30
    reason: str = Field(min_length=2, max_length=200)


class RecoverInput(S.Credentials):
    code: str = Field(min_length=8, max_length=40)


def revoke_sessions(db: Session, user: User, reason: str):
    timestamp = now()
    db.execute(
        update(Token)
        .where(Token.user_id == user.id, Token.expires > timestamp)
        .values(expires=timestamp, revoked_reason=reason)
    )


def freeze_account(db: Session, target: User, minutes: int, reason: str, actor: str):
    target.frozen_until = now() + minutes * 60
    target.freeze_reason = reason
    revoke_sessions(db, target, "account_frozen")
    for command in db.scalars(
        select(Command).where(
            Command.actor_id == target.id,
            Command.state.in_(["accepted", "deferred"]),
        )
    ):
        command.state = "rejected"
        command.data = {**command.data, "detail": "发起账号已安全冻结，旧命令不会补执行"}
    # 冻结前发出的恢复码不能被用来绕过本次冻结，管理员可核实后重发。
    db.execute(delete(PasswordRecovery).where(PasswordRecovery.user_id == target.id))
    log_system(
        db,
        actor,
        "account_frozen",
        target.login,
        {"until": target.frozen_until, "reason": reason, "minutes": minutes},
    )


def clear_freeze(db: Session, target: User, actor: str):
    was_frozen = target.frozen_until > now()
    target.frozen_until, target.freeze_reason, target.security_reviewed_at = 0, "", now()
    revoke_sessions(db, target, "session_expired")
    db.execute(delete(PasswordRecovery).where(PasswordRecovery.user_id == target.id))
    if was_frozen:
        log_system(db, actor, "account_unfrozen", target.login)


def record_login(db: Session, user: User, request: Request, client_id: str) -> bool:
    timestamp = now()
    # 只信任部署网关传入 ASGI 的地址，不自行读取可由请求方伪造的转发头。
    address = request.client.host[:64]
    client_hash = digest(f"{user.id}:{client_id or request.headers.get('user-agent', '')[:300]}")
    db.execute(delete(LoginRecord).where(LoginRecord.at < timestamp - 30 * 86400))
    db.execute(delete(Token).where(Token.expires < timestamp - 86400))
    recent = list(
        db.scalars(
            select(LoginRecord)
            .where(
                LoginRecord.user_id == user.id,
                LoginRecord.at > max(timestamp - 600, user.security_reviewed_at, user.frozen_until),
            )
            .order_by(LoginRecord.at)
            .limit(100)
        )
    )
    record = LoginRecord(user_id=user.id, at=timestamp, client_hash=client_hash, ip=address)
    db.add(record)
    recent.append(record)
    # 至少六次成功认证、三个浏览器、两个网络，才触发 30 分钟保护。
    # 错误密码、新 IP 或一次换设备都不能单独冻结账号；更不作永久处罚。
    if len(recent) >= 6 and len({r.client_hash for r in recent}) >= 3 and len({r.ip for r in recent}) >= 2:
        freeze_account(db, user, 30, "10 分钟内多个浏览器、多个网络反复登录", "security-service")
        return True
    return False


def managed_user(db: Session, user_id: str) -> User:
    target = db.scalar(select(User).where(User.id == user_id).with_for_update())
    if not target:
        fail(404, "user_missing", "账号不存在。")
    return target


@router.post("/system/users/{user_id}/freeze")
def freeze(
    user_id: str, body: FreezeInput, user: User = Depends(system_admin), db: Session = Depends(get_db, scope="function")
):
    target = managed_user(db, user_id)
    if target.id == user.id:
        fail(409, "current_admin", "不能冻结当前管理员账号。")
    if not target.active or not target.activated:
        fail(409, "account_unavailable", "仅可冻结已启用并设置密码的账号。")
    reason = body.reason.strip()
    if len(reason) < 2:
        fail(422, "reason_required", "请填写冻结原因。")
    freeze_account(db, target, body.minutes, reason, user.login)
    return {"frozenUntil": target.frozen_until}


@router.post("/system/users/{user_id}/unfreeze")
def unfreeze(user_id: str, user: User = Depends(system_admin), db: Session = Depends(get_db, scope="function")):
    target = managed_user(db, user_id)
    clear_freeze(db, target, user.login)
    return {"ok": True}


@router.post("/system/users/{user_id}/recovery-code")
def issue_recovery(user_id: str, user: User = Depends(system_admin), db: Session = Depends(get_db, scope="function")):
    target = managed_user(db, user_id)
    if not target.active or (target.expires_at and target.expires_at <= now()):
        fail(409, "account_unavailable", "请先恢复账号的启用状态和使用期限。")
    raw = "".join(secrets.choice("ABCDEFGHJKLMNPQRSTUVWXYZ23456789") for _ in range(16))
    db.execute(delete(PasswordRecovery).where(PasswordRecovery.user_id == target.id))
    expires = now() + 900
    db.add(PasswordRecovery(user_id=target.id, code_hash=digest(raw), epoch=sync_epoch(db), expires=expires))
    log_system(db, user.login, "password_recovery_issued", target.login)
    return {"code": "-".join(raw[i : i + 4] for i in range(0, 16, 4)), "expiresAt": expires}


@router.post("/auth/recover-password")
def recover(body: RecoverInput, request: Request, db: Session = Depends(get_db, scope="function")):
    limit(f"recovery-ip:{request.client.host}", 30, 300)
    target = db.scalar(select(User).where(User.login == body.login.casefold()).with_for_update())
    entry = db.get(PasswordRecovery, target.id) if target else None
    code = digest(body.code.replace("-", "").replace(" ", "").upper())
    available = bool(target and target.active and (not target.expires_at or target.expires_at > now()))
    if not available or not entry or entry.expires <= now() or entry.epoch != sync_epoch(db) or entry.attempts >= 5:
        fail(400, "recovery_invalid", "账号或重置码无效，或重置码已失效，请联系管理员重新获取。")
    if not secrets.compare_digest(entry.code_hash, code):
        entry.attempts += 1
        # 失败次数需要提交；抛出异常会让事务回滚，导致次数限制失效。
        return JSONResponse(
            {
                "detail": {
                    "code": "recovery_invalid",
                    "message": "账号或重置码无效，或重置码已失效，请联系管理员重新获取。",
                }
            },
            400,
        )
    target.password = passwords.hash(body.password)
    target.activated = True
    clear_freeze(db, target, target.login)
    log_system(db, target.login, "password_recovered", target.login)
    return {"ok": True, "message": "密码已更新，请使用新密码登录。"}
