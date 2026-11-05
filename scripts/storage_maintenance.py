"""每日清理 30 天前日志和失效记录，并回收 SQLite 空闲页。"""

import json

from backend.db import SessionLocal, engine
from backend.storage import clean_records
from backend.system_service import log_system


def main():
    with SessionLocal() as db:
        if engine.dialect.name == "sqlite":
            db.connection().exec_driver_sql("BEGIN IMMEDIATE")
        removed = clean_records(db)
        if any(removed.values()):
            log_system(db, "server-console", "storage_cleaned", "daily", {"removed": removed})
        db.commit()
    compacted = False
    if engine.dialect.name == "sqlite":
        with engine.connect().execution_options(isolation_level="AUTOCOMMIT") as connection:
            connection.exec_driver_sql("PRAGMA busy_timeout=5000")
            free = connection.exec_driver_sql("PRAGMA freelist_count").scalar()
            pages = connection.exec_driver_sql("PRAGMA page_count").scalar()
            size = connection.exec_driver_sql("PRAGMA page_size").scalar()
            if free * size >= 1024 * 1024 and free >= pages * 0.1:
                connection.exec_driver_sql("VACUUM")
                compacted = True
    print(json.dumps({"removed": removed, "compacted": compacted}, ensure_ascii=False))


if __name__ == "__main__":
    main()
