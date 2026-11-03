from pathlib import Path
from typing import Literal
from urllib.parse import urlsplit

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict
from sqlalchemy.engine import make_url


class Settings(BaseSettings):
    """生产采用 HTTPS；单机 SQLite 须明确启用，后续可切换 PostgreSQL。"""

    model_config = SettingsConfigDict(env_file=".env", env_prefix="KANBAN_", extra="ignore")
    database_url: str = "sqlite:///./data/kanban.db"
    environment: Literal["development", "production"] = "development"
    allow_sqlite_production: bool = False
    allowed_origins: list[str] = ["http://127.0.0.1:5173", "http://localhost:5173"]
    public_url: str = "http://127.0.0.1:5173"
    session_hours: int = Field(default=24, ge=1, le=168)
    client_latest_version: str = "0.3.0"
    client_download_url: str = ""
    storage_dir: Path = Path("data")


def validate_deployment(value: Settings):
    """在启动与迁移前拒绝不完整部署；错误文本不包含连接凭据。"""
    if value.environment != "production":
        return
    url = make_url(value.database_url)
    if url.get_backend_name() == "sqlite":
        if not value.allow_sqlite_production:
            raise RuntimeError("单机 SQLite 部署须显式设置 KANBAN_ALLOW_SQLITE_PRODUCTION=true。")
        if not url.database or url.database == ":memory:" or url.query.get("mode") == "memory":
            raise RuntimeError("生产 SQLite 必须使用持久化文件，不能使用内存数据库。")
    elif url.get_backend_name() != "postgresql":
        raise RuntimeError("只支持文件 SQLite 或 PostgreSQL。")
    public = urlsplit(value.public_url)
    if (
        public.scheme != "https"
        or not public.hostname
        or public.username
        or public.password
        or public.query
        or public.fragment
        or public.path not in ("", "/")
    ):
        raise RuntimeError("生产公开地址须为 HTTPS 站点根地址，不能包含账号、路径、查询或片段。")
    origin = f"https://{public.netloc}"
    if not value.allowed_origins or any(item != origin for item in value.allowed_origins):
        raise RuntimeError("生产允许来源须与公开 HTTPS 站点一致，不能使用通配符或开发地址。")


settings = Settings()
validate_deployment(settings)
Path("data").mkdir(exist_ok=True)
