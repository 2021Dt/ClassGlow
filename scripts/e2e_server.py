"""浏览器回归专用服务：随机临时库、固定回环端口，不读取真实教学数据。"""

import os
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    with tempfile.TemporaryDirectory(prefix="classglow-e2e-") as folder:
        os.environ.update(
            KANBAN_ENVIRONMENT="development",
            KANBAN_DATABASE_URL=f"sqlite:///{Path(folder) / 'e2e.db'}",
            KANBAN_PUBLIC_URL="http://127.0.0.1:5179",
            KANBAN_ALLOWED_ORIGINS='["http://127.0.0.1:5179"]',
            KANBAN_STORAGE_DIR=folder,
        )
        import uvicorn
        from alembic import command
        from alembic.config import Config

        from backend.db import Meta, SessionLocal, engine, uid
        from backend.schemas import Credentials
        from scripts.admin_account import initialize

        config = Config(str(ROOT / "alembic.ini"))
        config.set_main_option("script_location", str(ROOT / "backend/migrations"))
        try:
            command.upgrade(config, "head")
            with SessionLocal() as db:
                if not db.get(Meta, "sync_epoch"):
                    db.add(Meta(key="sync_epoch", value=uid()))
                # 仅存在于这个临时数据库，不作为生产管理员或配置默认值。
                initialize(
                    db, Credentials(login="e2e_admin", name="隔离回归管理员", password="E2e-Temporary-Only!2026")
                )
                db.commit()
            uvicorn.run("backend.app:app", host="127.0.0.1", port=8019, access_log=False, log_level="warning")
        finally:
            engine.dispose()


if __name__ == "__main__":
    main()
