"""布局保存图片引用；新客户端独立获取并缓存图片，旧客户端仍取得内嵌图片。"""

import base64
import copy
import hashlib
import re

from fastapi import APIRouter, Depends, Response
from sqlalchemy import delete
from sqlalchemy.orm import Session

from .db import ClassAsset, Device, User, get_db
from .security import actor, device_actor, fail, member

router = APIRouter()
IMAGE_FIELDS = frozenset(("image", "backgroundImage", "panelImage"))
ASSET_PATTERN = re.compile(r"^asset:([0-9a-f]{64})$")


def map_images(value, convert):
    if isinstance(value, dict):
        return {
            key: convert(item) if key in IMAGE_FIELDS and isinstance(item, str) else map_images(item, convert)
            for key, item in value.items()
        }
    if isinstance(value, list):
        return [map_images(item, convert) for item in value]
    return value


def store_layout(db, class_id, value):
    used = set()

    def store(picture):
        if not picture:
            return picture
        # 仅在 LayoutInput 已验证后调用，不接受外部 URL 或用户自带的内部引用。
        prefix, encoded = picture.split(",", 1)
        raw = base64.b64decode(encoded, validate=True)
        identity = hashlib.sha256(raw).hexdigest()
        if identity not in used and db.get(ClassAsset, (class_id, identity)) is None:
            db.add(ClassAsset(class_id=class_id, digest=identity, mime=prefix[5:].split(";")[0], data=raw))
        used.add(identity)
        return "asset:" + identity

    result = map_images(value, store)
    # 旧图片的完整副本已由设备缓存保留；当前库只保留当前布局实际引用的图片。
    db.execute(delete(ClassAsset).where(ClassAsset.class_id == class_id, ClassAsset.digest.not_in(used)))
    return result


def materialize_layout(db, class_id, value):
    cached = {}

    def load(picture):
        match = ASSET_PATTERN.fullmatch(picture)
        if not match:
            return picture
        identity = match[1]
        if identity not in cached:
            asset = db.get(ClassAsset, (class_id, identity))
            if asset is None:
                fail(503, "asset_unavailable", "看板图片暂时不可用，请联系管理员检查备份或重新发布布局。")
            cached[identity] = f"data:{asset.mime};base64," + base64.b64encode(asset.data).decode()
        return cached[identity]

    return map_images(value, load)


def layout_view(db, class_id, value, references=False):
    return copy.deepcopy(value) if references else materialize_layout(db, class_id, value)


def asset_response(db, class_id, asset_id):
    if not re.fullmatch(r"[0-9a-f]{64}", asset_id):
        fail(404, "asset_not_found", "图片不存在。")
    asset = db.get(ClassAsset, (class_id, asset_id))
    if asset is None:
        fail(404, "asset_not_found", "图片不存在或已更新，请重新同步。")
    return Response(asset.data, media_type=asset.mime, headers={"ETag": f'"{asset_id}"'})


@router.get("/classes/{class_id}/assets/{asset_id}")
def class_asset(
    class_id: str, asset_id: str, user: User = Depends(actor), db: Session = Depends(get_db, scope="function")
):
    member(db, user, class_id)
    return asset_response(db, class_id, asset_id)


@router.get("/device/assets/{asset_id}")
def device_asset(asset_id: str, d: Device = Depends(device_actor), db: Session = Depends(get_db, scope="function")):
    return asset_response(db, d.class_id, asset_id)
