"""SQLite 在线备份及隔离恢复；恢复总是写入新文件，避免覆盖正在使用的数据库。"""

import argparse
import sqlite3
from contextlib import closing
from datetime import datetime
from pathlib import Path
from uuid import uuid4

from sqlalchemy.engine import make_url

from backend.config import settings


def checked_source(path: Path):
    if not path.is_file():
        raise ValueError("数据库文件不存在。")
    connection = sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True, timeout=30)
    if connection.execute("PRAGMA quick_check").fetchone()[0] != "ok":
        connection.close()
        raise ValueError("数据库完整性检查失败，停止操作。")
    if connection.execute("PRAGMA foreign_key_check").fetchone() is not None:
        connection.close()
        raise ValueError("数据库存在不一致的关联记录，停止备份或恢复。")
    return connection


def backup(source: Path, output: Path):
    if source.resolve() == output.resolve() or output.exists():
        raise ValueError("目标必须是新的文件，不能覆盖数据库或已有备份。")
    output.parent.mkdir(parents=True, exist_ok=True)
    with closing(checked_source(source)) as src, closing(sqlite3.connect(output)) as target:
        # SQLite 原生 backup API 能一致地复制在线数据库，不能简单复制可能变化的主文件。
        src.backup(target)
        if target.execute("PRAGMA quick_check").fetchone()[0] != "ok":
            raise ValueError("生成的备份未通过完整性检查。")
    return output


def restore(source: Path, output: Path):
    backup(source, output)
    with closing(sqlite3.connect(output)) as db:
        required = {"metadata", "sessions", "devices", "device_commands", "pairings"}
        tables = {row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        if not required <= tables:
            raise ValueError("备份结构不兼容，不能用于班级看板。")
        # 开放服务前更换同步世代，旧设备和旧会话不因备份还原而重新获得权限。
        db.execute("UPDATE metadata SET value=? WHERE key='sync_epoch'", (str(uuid4()),))
        db.execute("DELETE FROM sessions")
        db.execute("UPDATE devices SET revoked=1")
        db.execute("UPDATE device_commands SET state='rejected' WHERE state IN ('accepted','deferred')")
        db.execute("UPDATE pairings SET expires=0")
        db.commit()
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["backup", "restore"])
    parser.add_argument("--source", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    url = make_url(settings.database_url)
    if url.get_backend_name() != "sqlite" and args.source is None:
        parser.error("当前不是 SQLite，请显式指定来源，或使用 PostgreSQL 运维工具。")
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    source = args.source or Path(url.database or "data/kanban.db")
    target = args.output or Path(
        f"data/backups/kanban-{stamp}.db" if args.action == "backup" else f"data/restored-{stamp}.db"
    )
    result = backup(source, target) if args.action == "backup" else restore(source, target)
    print(f"完成：{result.resolve()}")
    if args.action == "restore":
        print("请停止服务并将 KANBAN_DATABASE_URL 指向恢复文件；重新登录和绑定设备后再开放使用。")


if __name__ == "__main__":
    main()
