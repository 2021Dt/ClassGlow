import base64
import binascii
import json
import re
import secrets
from datetime import UTC, datetime
from io import BytesIO

import qrcode
from fastapi import APIRouter, Depends, Header, Request, Response
from PIL import Image, UnidentifiedImageError
from sqlalchemy import delete, select
from sqlalchemy.orm import Session

from . import schemas as S
from .config import settings
from .db import (
    Audit,
    Classroom,
    Command,
    Content,
    Device,
    DeviceDisplay,
    Idempotency,
    Membership,
    Pairing,
    User,
    get_db,
    now,
    sync_epoch,
    uid,
)
from .entitlements import account_available, class_entitlement, has_premium, require_premium
from .security import actor, bearer, device_actor, digest, fail, limit, member, secret

router = APIRouter()
GROUPS = {"sync": "sync", "pause": "pause", "resume": "pause", "set_page": "page", "auto_page": "page"}


def device_view(d):
    s = d.state
    return {
        "id": d.id,
        "classId": d.class_id,
        "name": d.name,
        "clientVersion": s.get("clientVersion", ""),
        "bindingId": d.binding,
        "online": not d.revoked and now() - d.last_seen <= 90,
        "revoked": d.revoked,
        "reported": datetime.fromtimestamp(d.last_seen, UTC).isoformat(),
        "localPaused": s.get("localPaused", False),
        "remotePaused": s.get("remotePaused", False),
        "cached": s.get("cachedVersion", 0),
        "applied": s.get("appliedVersion", 0),
        "page": s.get("page", 1),
        "pageCount": s.get("pageCount", 1),
        "manualUntil": s.get("manualUntil", 0) * 1000,
        "pageSetId": s.get("pageSetId", ""),
        "effectiveTheme": s.get("effectiveTheme", "mica_light"),
        "effectiveMaterial": s.get("effectiveMaterial", "solid"),
        "materialReason": s.get("materialReason", ""),
        "deferred": s.get("applyState") == "deferred",
        "applyState": s.get("applyState", "waiting"),
        "error": s.get("error", ""),
        "revision": d.control_revision,
        "localControlEpoch": s.get("localControlEpoch", 0),
    }


def command_view(c):
    return {
        "id": c.id,
        "deviceId": c.device_id,
        "action": c.action,
        "actor": c.data.get("actorName", ""),
        "state": c.state,
        "time": datetime.fromtimestamp(c.issued, UTC).isoformat(),
        "detail": c.data.get("detail", "请求已受理，等待教室端执行"),
        "seq": c.seq,
        "bindingId": c.binding,
        "syncEpoch": c.epoch,
        "expiresAt": c.expires,
        "issuedAt": c.issued,
        **{k: v for k, v in c.data.items() if k in ("page", "pageSetId", "localControlEpoch")},
    }


@router.post("/device-pairings", status_code=201)
def pairing(body: S.PairInput, request: Request, db: Session = Depends(get_db)):
    limit(f"pair-new:{request.client.host}", 30, 600)
    code = f"{secrets.randbelow(1000000):06d}"
    token = secret()
    # 长期凭证从设备持有的配对秘密派生，数据库仅存摘要，手机永远不能读出该秘密。
    p = Pairing(code_hash=digest(code), secret_hash=digest(token), name=body.name, expires=now() + 600)
    db.add(p)
    db.flush()
    pairing_url = f"{settings.public_url}/?pair={code}"
    image = qrcode.make(pairing_url)
    buffer = BytesIO()
    image.save(buffer, format="PNG")
    return {
        "id": p.id,
        "code": code,
        "secret": token,
        "expiresAt": p.expires,
        "pairingUrl": pairing_url,
        "qrPngBase64": base64.b64encode(buffer.getvalue()).decode(),
    }


@router.post("/device-pairings/confirm")
def pair_confirm(body: S.PairConfirm, request: Request, user: User = Depends(actor), db: Session = Depends(get_db)):
    limit(f"pair-confirm:{user.id}", 5, 300)
    c, _ = member(db, user, body.classId, admin=True, write=True)
    p = db.scalar(select(Pairing).where(Pairing.code_hash == digest(body.code)).with_for_update())
    if not p or p.consumed or p.expires <= now():
        fail(422, "pairing_invalid", "绑定码无效、已使用或已过期，请在教室端重新生成。")
    device = Device(class_id=c.id, name=p.name, credential_hash=p.secret_hash, epoch=sync_epoch(db), state={})
    db.add(device)
    db.flush()
    p.consumed = True
    p.device_id = device.id
    db.add(
        Audit(class_id=c.id, actor=user.id, action="device_bound", data={"deviceId": device.id, "name": device.name})
    )
    return device_view(device)


@router.post("/device-pairings/inspect")
def inspect_pairing(body: S.PairConfirm, user: User = Depends(actor), db: Session = Depends(get_db)):
    member(db, user, body.classId, admin=True, write=True)
    limit(f"pair-inspect:{user.id}", 5, 300)
    p = db.scalar(select(Pairing).where(Pairing.code_hash == digest(body.code)))
    if not p or p.consumed or p.expires <= now():
        fail(422, "pairing_invalid", "绑定码无效、已使用或已过期。")
    return {"name": p.name, "expiresAt": p.expires}


@router.get("/device-pairings/{pair_id}")
def pair_poll(pair_id: str, authorization: str = Header(), db: Session = Depends(get_db)):
    from .security import bearer

    p = db.get(Pairing, pair_id)
    if not p or not secrets.compare_digest(p.secret_hash, digest(bearer(authorization))):
        fail(404, "pairing_not_found", "配对请求不存在。")
    if not p.consumed:
        if p.expires <= now():
            fail(410, "pairing_expired", "配对请求已过期。")
        return {"state": "waiting"}
    d = db.get(Device, p.device_id)
    if not d or d.revoked or d.epoch != sync_epoch(db):
        fail(401, "device_revoked", "设备授权已失效。")
    return {"state": "bound", "deviceId": d.id, "bindingId": d.binding, "syncEpoch": d.epoch, "classId": d.class_id}


def valid_commands(db, d):
    commands = db.scalars(
        select(Command)
        .where(Command.device_id == d.id, Command.state.in_(["accepted", "deferred"]))
        .order_by(Command.seq)
    ).all()
    result = []
    for command in commands:
        author = db.get(User, command.actor_id)
        membership = db.scalar(
            select(Membership).where(
                Membership.class_id == d.class_id, Membership.user_id == command.actor_id, Membership.active.is_(True)
            )
        )
        if (
            command.binding != d.binding
            or command.epoch != d.epoch
            or not account_available(author)
            or not has_premium(author)
            or not (author.system_admin or membership and membership.role != "teacher")
        ):
            command.state = "rejected"
            command.data = {**command.data, "detail": "绑定或操作权限已失效"}
        elif command.expires <= now():
            command.state = "expired"
            command.data = {**command.data, "detail": "命令已过期，不会补执行"}
        elif command.group != "sync" and command.data["localControlEpoch"] != d.state.get("localControlEpoch", 0):
            command.state = "rejected"
            command.data = {**command.data, "detail": "现场操作已改变，旧命令失效"}
        else:
            result.append(command)
    return result


@router.get("/device/snapshot")
def snapshot(
    if_none_match: str | None = Header(default=None),
    x_classglow_client_version: str | None = Header(default=None),
    d: Device = Depends(device_actor),
    db: Session = Depends(get_db),
):
    c = db.get(Classroom, d.class_id)
    entitlement = class_entitlement(db, c)
    commands = valid_commands(db, d)
    version = re.fullmatch(
        r"(\d{1,4})\.(\d{1,4})\.(\d{1,4})", x_classglow_client_version or d.state.get("clientVersion", "")
    )
    canvas_supported = bool(version and tuple(map(int, version.groups())) >= (0, 4, 0))
    # 许可状态进入 ETag；到期必须返回新快照，不能被内容版本未变的 304 遮住。
    etag = f'"{d.binding}:{d.epoch}:{c.version}:{d.control_revision}:{int(entitlement["premiumActive"])}:{entitlement["premiumUntil"]}:{int(canvas_supported)}"'
    # 即使内容未变，只要有待确认命令也必须返回 200，不能让 304 吞掉控制。
    display = db.get(DeviceDisplay, d.id)
    display_request = pending_display(display)
    if if_none_match == etag and not commands and not display_request:
        return Response(status_code=304, headers={"ETag": etag})
    from .app import content_view, layout_default

    basic = layout_default()
    classroom_data = {k: v for k, v in c.data.items() if k not in ("layoutLicenseUserId", "_displayPremium")}
    classroom_data["layout"] = c.data["layout"] if entitlement["premiumActive"] else basic
    # 老客户端只能显示基础看板；版本能力进入 ETag，升级后同一内容版本也立即重新同步。
    if classroom_data["layout"].get("editor") == "canvas" and not canvas_supported:
        classroom_data["layout"] = basic

    contents = db.scalars(select(Content).where(Content.class_id == c.id, Content.state == "published")).all()
    return Response(
        json.dumps(
            {
                "schema": 1,
                "deviceId": d.id,
                "bindingId": d.binding,
                "syncEpoch": d.epoch,
                "classVersion": c.version,
                "serverTime": now(),
                "classroom": {
                    "id": c.id,
                    "name": c.name,
                    **classroom_data,
                    "premiumUntil": entitlement["premiumUntil"],
                    "basicLayout": basic,
                },
                "contents": [content_view(db, item) for item in contents],
                "commands": [command_view(cmd) for cmd in commands],
                "displayRequest": display_request,
            },
            ensure_ascii=False,
        ),
        media_type="application/json",
        headers={"ETag": etag},
    )


def apply_report(db, d, body: S.ReportInput):
    c = db.get(Classroom, d.class_id)
    if body.bindingId != d.binding or body.syncEpoch != d.epoch:
        fail(409, "binding_changed", "回执绑定或同步世代已失效。")
    if body.appliedVersion > body.cachedVersion or body.cachedVersion > c.version or body.page > body.pageCount:
        fail(422, "report_invalid", "缓存版本、应用版本或页码不合法。")
    if body.stateReportSeq <= d.report_seq:
        return False
    if body.cachedVersion < d.state.get("cachedVersion", 0) or body.appliedVersion < d.state.get("appliedVersion", 0):
        fail(409, "version_rollback", "同一绑定内缓存与应用版本不能倒退。")
    if body.applyState == "paused" and not (body.localPaused or body.remotePaused):
        fail(422, "pause_report_invalid", "暂停回执必须说明暂停来源。")
    if body.localControlEpoch < d.state.get("localControlEpoch", 0):
        fail(409, "local_epoch_stale", "现场操作世代不能倒退。")
    d.report_seq = body.stateReportSeq
    d.state = body.model_dump(
        include=set(S.ReportInput.model_fields),
        exclude={"bindingId", "syncEpoch", "stateReportSeq", "commandId", "result", "startedAt", "finishedAt"},
    )
    return True


@router.post("/device/heartbeat")
@router.post("/device/ack")
def heartbeat(body: S.ReportInput, d: Device = Depends(device_actor), db: Session = Depends(get_db)):
    accepted = apply_report(db, d, body)
    return {"accepted": accepted, "serverTime": now()}


def pending_display(display):
    if display and display.request_id != display.data.get("requestId") and display.requested_at > now() - 90:
        return display.request_id
    return ""


def display_device(db, user, device_id):
    d = db.get(Device, device_id)
    if not d:
        fail(404, "device_not_found", "设备不存在。")
    member(db, user, d.class_id)
    if d.revoked or d.epoch != sync_epoch(db):
        fail(409, "device_revoked", "设备已解绑，不能查看原设备画面。")
    return d


def display_view(display):
    if not display:
        return {"requestId": "", "requestedAt": 0, "receivedAt": 0, "pending": False, "report": {}}
    return {
        "requestId": display.request_id,
        "requestedAt": display.requested_at,
        "receivedAt": display.received_at,
        "pending": bool(pending_display(display)),
        "report": display.data,
    }


@router.post("/devices/{device_id}/display-request")
def request_display(device_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    # 查看画面是基础能力；只能读取本班看板，不授予暂停、翻页等控制权限。
    d = display_device(db, user, device_id)
    if now() - d.last_seen > 90:
        fail(409, "device_offline", "教室电脑离线，恢复联网后再刷新。")
    version = re.match(r"^(\d+)\.(\d+)\.(\d+)", d.state.get("clientVersion", ""))
    if not version or tuple(map(int, version.groups())) < (0, 4, 6):
        fail(409, "client_update_required", "请先将教室电脑升级到 Windows 0.4.6 或更新版本。")
    display = db.get(DeviceDisplay, d.id)
    if not display:
        display = DeviceDisplay(device_id=d.id, data={})
        db.add(display)
    # 多位老师同时刷新共用一次回传，防止后来的请求让前一位一直等待。
    if not display.requested_at or display.requested_at < now() - (45 if pending_display(display) else 5):
        display.request_id, display.requested_at = uid(), now()
    db.flush()
    return display_view(display)


@router.get("/devices/{device_id}/display")
def get_display(device_id: str, after: float = 0, user: User = Depends(actor), db: Session = Depends(get_db)):
    d = display_device(db, user, device_id)
    display = db.get(DeviceDisplay, d.id)
    result = display_view(display)
    # 轮询只返回状态；仅收到新画面时传图片，避免每次刷新重复传输。
    if display and display.received_at > after:
        result["image"] = "data:image/jpeg;base64," + base64.b64encode(display.image).decode() if display.image else ""
    result["device"] = device_view(d)
    result["classVersion"] = db.get(Classroom, d.class_id).version
    return result


@router.post("/device/display")
def receive_display(body: S.DeviceDisplayInput, d: Device = Depends(device_actor), db: Session = Depends(get_db)):
    display = db.get(DeviceDisplay, d.id)
    if not display or pending_display(display) != body.requestId:
        return {"accepted": False}
    image = b""
    if body.availability == "displayed":
        if body.localPaused or body.remotePaused or not body.appliedVersion:
            fail(422, "display_state_invalid", "暂停或尚未应用的设备不能报告正在展示。")
        try:
            image = base64.b64decode(body.imageBase64, validate=True)
            if len(image) > 400000:
                raise ValueError()
            with Image.open(BytesIO(image)) as preview:
                if preview.format != "JPEG" or not (1 <= preview.width <= 1280 and 1 <= preview.height <= 1280):
                    raise ValueError()
                preview.verify()
        except (ValueError, binascii.Error, OSError, UnidentifiedImageError, Image.DecompressionBombError):
            fail(422, "display_image_invalid", "看板回传图片无效或超过大小限制。")
    elif body.imageBase64:
        fail(422, "display_state_invalid", "未展示时不应上传图片。")
    if not apply_report(db, d, body):
        return {"accepted": False}
    display.image, display.received_at = image, now()
    display.data = {
        "requestId": body.requestId,
        "availability": body.availability,
        "appliedVersion": body.appliedVersion,
        "renderedAt": body.renderedAt,
        "page": body.page,
        "pageCount": body.pageCount,
        "applyState": body.applyState,
    }
    return {"accepted": True}


@router.get("/devices/{device_id}/control")
def control_state(device_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    d = db.get(Device, device_id)
    if not d:
        fail(404, "device_not_found", "设备不存在。")
    member(db, user, d.class_id)
    valid_commands(db, d)
    return {
        "device": device_view(d),
        "commands": [
            command_view(c)
            for c in db.scalars(
                select(Command).where(Command.device_id == d.id).order_by(Command.seq.desc()).limit(100)
            )
        ],
    }


@router.post("/devices/{device_id}/commands", status_code=202)
def command(
    device_id: str,
    body: S.ControlInput,
    idempotency_key: str | None = Header(default=None),
    user: User = Depends(actor),
    db: Session = Depends(get_db),
):
    from .app import idempotent

    d = db.get(Device, device_id)
    if not d:
        fail(404, "device_not_found", "设备不存在。")
    member(db, user, d.class_id, admin=True, write=True)
    require_premium(user)
    # 锁定后刷新，防止并发请求使用相同控制修订。
    d = db.scalar(
        select(Device).where(Device.id == device_id).with_for_update().execution_options(populate_existing=True)
    )
    key, signature, previous = idempotent(db, user, idempotency_key, f"control:{device_id}", body.model_dump())
    if previous is not None:
        return previous
    if d.revoked or d.epoch != sync_epoch(db) or now() - d.last_seen > 90:
        fail(409, "device_unreachable", "设备不可达，不能提交即时控制。")
    if body.expectedControlRevision != d.control_revision:
        fail(409, "control_revision_conflict", "其他操作已提交，请刷新设备状态。")
    s = d.state
    if body.action == "resume" and s.get("localPaused"):
        fail(409, "local_pause", "本机暂停需现场老师恢复，手机不能解除。")
    if GROUPS[body.action] == "page":
        if s.get("localPaused") or s.get("remotePaused"):
            fail(409, "device_paused", "暂停期间不能翻页。")
        if body.pageSetId != s.get("pageSetId", "") or body.page > s.get("pageCount", 1):
            fail(409, "page_set_changed", "页集合已改变，请刷新后重试。")
    group = GROUPS[body.action]
    if group != "sync":
        for old in db.scalars(
            select(Command).where(
                Command.device_id == d.id, Command.group == group, Command.state.in_(["accepted", "deferred"])
            )
        ):
            old.state = "superseded"
            old.data = {**old.data, "detail": "已被同组的新操作替代"}
    d.control_revision += 1
    cmd = Command(
        device_id=d.id,
        actor_id=user.id,
        binding=d.binding,
        epoch=d.epoch,
        seq=d.control_revision,
        action=body.action,
        group=group,
        expires=min(
            [now() + (120 if group == "pause" else 60)]
            + ([user.premium_until] if not user.system_admin else [])
            + ([user.expires_at] if user.expires_at else [])
        ),
        data={
            "page": body.page,
            "pageSetId": body.pageSetId,
            "localControlEpoch": s.get("localControlEpoch", 0),
            "actorName": user.name,
        },
    )
    db.add(cmd)
    db.flush()
    result = command_view(cmd)
    db.add(Idempotency(key=key, request_hash=signature, result=result))
    db.add(
        Audit(
            class_id=d.class_id,
            actor=user.id,
            action="device_command",
            data={"commandId": cmd.id, "action": cmd.action},
        )
    )
    return result


@router.post("/device/command-acks")
def command_ack(body: S.CommandAck, d: Device = Depends(device_actor), db: Session = Depends(get_db)):
    cmd = db.get(Command, body.commandId)
    if not cmd or cmd.device_id != d.id or cmd.binding != d.binding or cmd.epoch != d.epoch:
        fail(409, "command_binding_changed", "命令已失效或属于其他绑定。")
    if body.result == "executed" and (
        body.startedAt is None
        or body.startedAt < cmd.issued - 2
        or body.startedAt >= cmd.expires
        or body.finishedAt is None
        or body.finishedAt < body.startedAt
        or body.finishedAt > now() + 5
    ):
        fail(422, "command_time_invalid", "命令执行时间无效，不能伪报执行成功。")
    # 终态重复回执不重复执行；迟到的真实执行结果可修正到期历史，但不回滚新状态。
    if cmd.state in ("accepted", "deferred") or (cmd.state in ("expired", "superseded") and body.result == "executed"):
        cmd.state = body.result
        cmd.data = {
            **cmd.data,
            "startedAt": body.startedAt,
            "finishedAt": body.finishedAt,
            "detail": body.error
            or {"executed": "教室端已执行", "deferred": "前台保护，等待安静应用", "rejected": "设备拒绝执行"}[
                body.result
            ],
        }
    accepted = apply_report(db, d, body)
    return {"accepted": accepted, "state": cmd.state}


@router.post("/devices/{device_id}/revoke")
def revoke(device_id: str, user: User = Depends(actor), db: Session = Depends(get_db)):
    d = db.get(Device, device_id)
    if not d:
        fail(404, "device_not_found", "设备不存在。")
    c, _ = member(db, user, d.class_id, admin=True, write=True)
    d.revoked = True
    db.execute(delete(DeviceDisplay).where(DeviceDisplay.device_id == d.id))
    for cmd in db.scalars(
        select(Command).where(Command.device_id == d.id, Command.state.in_(["accepted", "deferred"]))
    ):
        cmd.state = "rejected"
    db.add(Audit(class_id=c.id, actor=user.id, action="device_revoked", data={"deviceId": d.id}))
    return {"ok": True, "message": "旧设备恢复联网后停止使用；离线缓存无法立即远程清除。"}


@router.post("/device/uninstall")
def uninstall_device(authorization: str = Header(), db: Session = Depends(get_db)):
    """本机卸载只撤销凭证对应的设备/配对，不接受目标设备 ID，不删除教学内容。"""
    credential = digest(bearer(authorization))
    d = db.scalar(select(Device).where(Device.credential_hash == credential).with_for_update())
    p = db.scalar(select(Pairing).where(Pairing.secret_hash == credential).with_for_update())
    if not d and not p:
        fail(401, "device_revoked", "设备或配对授权已失效。")
    if p:
        p.expires = 0
    if d and not d.revoked:
        d.revoked = True
        db.execute(delete(DeviceDisplay).where(DeviceDisplay.device_id == d.id))
        for cmd in db.scalars(
            select(Command).where(Command.device_id == d.id, Command.state.in_(["accepted", "deferred"]))
        ):
            cmd.state = "rejected"
            cmd.data = {**cmd.data, "detail": "教室端已卸载，设备授权已撤销"}
        db.add(Audit(class_id=d.class_id, actor=d.id, action="device_uninstalled", data={"deviceId": d.id}))
    return {"ok": True}
