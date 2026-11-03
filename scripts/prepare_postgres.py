"""运维菜单的配置准备步骤，仅使用宿主机 Python 标准库，不在输出中暴露数据库密码。"""

import os
import secrets
from pathlib import Path


def prepare(root):
    source = root / "deploy/.env"
    pending = root / ".deploy/database-migration.env"
    previous = root / ".deploy/sqlite-before-postgres.env"
    if pending.exists():
        return  # 中断后沿用同一数据库密码与目标，避免创建另一个空目标。
    lines = source.read_text(encoding="utf-8").splitlines()
    config = {
        k: v.strip().strip("\"'")
        for line in lines
        if "=" in line and not line.startswith("#")
        for k, v in [line.split("=", 1)]
    }
    source_url = config.get("KANBAN_DATABASE_URL", "sqlite:////app/data/kanban.db")
    if not source_url.startswith("sqlite:") or config.get("KANBAN_DATABASE") == "postgres":
        raise SystemExit("当前不是 SQLite，不需要执行此迁移。")
    if previous.exists():
        raise SystemExit("检测到旧迁移配置，请先核对 .deploy/sqlite-before-postgres.env，不能覆盖恢复材料。")
    password = secrets.token_hex(32)
    changes = {
        "KANBAN_DATABASE": "postgres",
        "KANBAN_DATABASE_URL": f"postgresql+psycopg://kanban:{password}@db:5432/kanban",
        "KANBAN_ALLOW_SQLITE_PRODUCTION": "false",
        "KANBAN_SQLITE_SOURCE_URL": source_url,
        "POSTGRES_PASSWORD": password,
        "POSTGRES_IMAGE": config.get("POSTGRES_IMAGE")
        or (
            "public.ecr.aws/docker/library/postgres:17"
            if "public.ecr.aws" in config.get("PYTHON_IMAGE", "")
            else "postgres:17"
        ),
    }
    kept = [line for line in lines if line.split("=", 1)[0] not in changes]
    # Compose 的 dotenv 规则中单引号保留原文；此处源地址来自已有配置，其余值均由程序生成。
    for key, value in changes.items():
        if "'" in value or "\n" in value:
            raise SystemExit("连接地址含不支持的引号或换行，请先规范配置后重试。")
        kept.append(f"{key}='{value}'" if key != "KANBAN_DATABASE" else f"{key}={value}")
    # 创建时即限制权限，避免密码文件在 chmod 之前短暂对其他用户可读。
    for path, contents in ((previous, source.read_text(encoding="utf-8")), (pending, "\n".join(kept) + "\n")):
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            stream.write(contents)


if __name__ == "__main__":
    prepare(Path(__file__).resolve().parents[1])
    print("迁移配置已准备；密码仅保存在本项目权限为 0600 的配置文件中。")
