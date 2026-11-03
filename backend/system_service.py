"""系统配置与首次安装密钥；班级角色和系统管理员是两套独立权限。"""

import secrets
from pathlib import Path

from sqlalchemy import select
from sqlalchemy.orm import Session

from .config import settings
from .db import SystemAudit, SystemConfig


def system_config(db: Session, lock=False) -> SystemConfig:
    query = select(SystemConfig).where(SystemConfig.id == 1)
    return db.scalar(query.with_for_update() if lock else query)


def setup_key_file() -> Path:
    return settings.storage_dir / "setup-key.txt"


def ensure_setup_key():
    settings.storage_dir.mkdir(parents=True, exist_ok=True)
    path = setup_key_file()
    if not path.exists():
        # 只保存在服务器数据目录，不在网页或启动日志中显示。
        with path.open("x", encoding="utf-8") as stream:
            stream.write(secrets.token_urlsafe(24))
        path.chmod(0o600)


def log_system(db: Session, actor: str, action: str, target="", data=None):
    db.add(SystemAudit(actor=actor, action=action, target=target, data=data or {}))
