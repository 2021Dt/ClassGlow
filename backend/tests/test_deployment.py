import pytest

from backend.config import Settings, validate_deployment


def configured(**overrides):
    return Settings(
        _env_file=None,
        **{
            "environment": "production",
            "public_url": "https://board.example.com",
            "allowed_origins": ["https://board.example.com"],
            "database_url": "sqlite:///./data/test.db",
            **overrides,
        },
    )


def test_sqlite_server_requires_explicit_persistent_storage():
    with pytest.raises(RuntimeError, match="显式"):
        validate_deployment(configured())
    validate_deployment(configured(allow_sqlite_production=True))
    for url in ("sqlite://", "sqlite:///:memory:", "sqlite:///file:test?mode=memory&uri=true"):
        with pytest.raises(RuntimeError, match="持久化"):
            validate_deployment(configured(database_url=url, allow_sqlite_production=True))
    validate_deployment(configured(database_url="postgresql+psycopg://user:password@db/kanban"))


def test_production_origin_and_https_must_match():
    for public, origins in [
        ("http://board.example.com", ["http://board.example.com"]),
        ("https://board.example.com/api", ["https://board.example.com"]),
        ("https://board.example.com", ["*"]),
        ("https://board.example.com", ["http://localhost:5173"]),
    ]:
        with pytest.raises(RuntimeError):
            validate_deployment(configured(public_url=public, allowed_origins=origins, allow_sqlite_production=True))
