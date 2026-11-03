"""系统配置与审计；班级角色和系统管理员是两套独立权限。"""

from sqlalchemy import select
from sqlalchemy.orm import Session

from .db import SystemAudit, SystemConfig


def system_config(db: Session, lock=False) -> SystemConfig:
    query = select(SystemConfig).where(SystemConfig.id == 1)
    return db.scalar(query.with_for_update() if lock else query)


def log_system(db: Session, actor: str, action: str, target="", data=None):
    db.add(SystemAudit(actor=actor, action=action, target=target, data=data or {}))
