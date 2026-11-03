"""将停写的 SQLite 业务数据迁移到空目标库，支持 PostgreSQL；不会覆盖已有目标数据。"""

import os
from pathlib import Path
from uuid import uuid4

from alembic import command
from alembic.config import Config
from alembic.script import ScriptDirectory
from alembic.util.exc import CommandError
from sqlalchemy import create_engine, delete, func, inspect, select, text, update
from sqlalchemy.engine import make_url
from sqlalchemy.exc import SQLAlchemyError

from backend.config import settings
from backend.db import Base


def migrate(source_url: str, target_url: str):
    source_address, target_address = make_url(source_url), make_url(target_url)
    if source_address.get_backend_name() != "sqlite":
        raise ValueError("本工具的来源必须为 SQLite。")
    if target_address.get_backend_name() not in ("sqlite", "postgresql"):
        raise ValueError("目标仅支持 PostgreSQL，或用于演练的独立 SQLite。")
    if (
        target_address.get_backend_name() == "sqlite"
        and Path(source_address.database).resolve() == Path(target_address.database).resolve()
    ):
        raise ValueError("来源和目标不能是同一数据库。")
    source_engine = create_engine(source_url, hide_parameters=True)
    target_engine = create_engine(target_url, hide_parameters=True)
    root = Path(__file__).resolve().parents[1]
    config = Config(str(root / "alembic.ini"))
    config.set_main_option("script_location", str(root / "backend/migrations"))
    head = ScriptDirectory.from_config(config).get_current_head()
    copied = {}
    try:
        with source_engine.connect() as source, target_engine.begin() as target:
            # 停服是操作前提；额外取得写锁，避免遗漏的写入让迁移结果前后不一致。
            source.exec_driver_sql("BEGIN IMMEDIATE")
            if (
                "alembic_version" not in inspect(source).get_table_names()
                or source.scalar(text("SELECT version_num FROM alembic_version")) != head
            ):
                raise ValueError("来源尚未升级到当前迁移版本，请先运行 Alembic 并完成备份。")
            if inspect(target).get_table_names():
                raise ValueError("目标必须为空数据库；本工具不会覆盖任何已有表。")
            config.attributes["connection"] = target
            command.upgrade(config, "head")
            # 按外键依赖顺序搬迁；JSON、布尔与浮点由 SQLAlchemy 类型转换，不能拼接 SQL 文本。
            for table in Base.metadata.sorted_tables:
                if table.name in ("metadata", "system_config"):
                    target.execute(delete(table))
                result = source.execute(select(table))
                while rows := result.fetchmany(200):
                    target.execute(table.insert(), [dict(row._mapping) for row in rows])
                expected = source.scalar(select(func.count()).select_from(table))
                actual = target.scalar(select(func.count()).select_from(table))
                if expected != actual:
                    raise RuntimeError("逐表条数核对失败，目标事务已停止。")
                copied[table.name] = actual
            # 新库启用新世代。旧会话、设备、配对与未执行命令不得随迁移复活。
            tables = Base.metadata.tables
            target.execute(
                update(tables["metadata"]).where(tables["metadata"].c.key == "sync_epoch").values(value=str(uuid4()))
            )
            target.execute(delete(tables["sessions"]))
            target.execute(update(tables["devices"]).values(revoked=True))
            target.execute(update(tables["pairings"]).values(expires=0))
            target.execute(
                update(tables["device_commands"])
                .where(tables["device_commands"].c.state.in_(["accepted", "deferred"]))
                .values(state="rejected")
            )
            source.rollback()
        return copied
    finally:
        source_engine.dispose()
        target_engine.dispose()


def main():
    # 目标连接串通过环境变量传入，避免数据库口令出现在进程参数和 shell 历史中。
    target = os.environ.get("KANBAN_MIGRATION_TARGET_URL")
    if not target:
        raise SystemExit("请设置 KANBAN_MIGRATION_TARGET_URL，并先停服、备份；目标库必须为空。")
    try:
        result = migrate(settings.database_url, target)
        print(f"迁移完成，已逐表核对 {len(result)} 张业务表。请切换连接配置，并重新登录、绑定设备。")
    except ValueError as error:
        raise SystemExit(str(error)) from None
    except (SQLAlchemyError, OSError, RuntimeError, CommandError):
        raise SystemExit("迁移未完成，请保留来源与备份，检查目标连接及迁移日志；不要开放目标库。") from None


if __name__ == "__main__":
    main()
