"""部署前备份现有 SQLite；首次安装没有数据库时直接进入迁移。"""

from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4

from sqlalchemy.engine import make_url

from backend.config import settings
from scripts.sqlite_backup import backup


def main():
    url = make_url(settings.database_url)
    if url.get_backend_name() != "sqlite":
        raise SystemExit("一键部署当前适用于 SQLite；PostgreSQL 请按部署文档先备份再手动迁移。")
    source = Path(url.database)
    if not source.exists():
        print("首次安装：尚无数据库，下一步创建数据表。")
        return
    stamp = datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ")
    output = settings.storage_dir / "backups" / f"backup-before-deploy-{stamp}-{uuid4().hex[:8]}.db"
    backup(source, output)
    output.chmod(0o600)
    print(f"部署前备份完成：{output}")


if __name__ == "__main__":
    main()
