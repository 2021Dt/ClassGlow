import pytest
from fastapi import Request
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from backend.app import app
from backend.db import Base, Meta, SystemConfig, configure_sqlite, database_session, get_db, request_is_read_only
from backend.security import _buckets


@pytest.fixture
def client(tmp_path):
    """每个用例使用独立 SQLite 文件，事务行为与本地服务一致，避免污染实际班级。"""
    engine = create_engine(f"sqlite:///{tmp_path / 'test.db'}", connect_args={"check_same_thread": False})
    configure_sqlite(engine)
    Base.metadata.create_all(engine)
    with Session(engine) as db:
        db.add(Meta(key="sync_epoch", value="test-epoch"))
        db.add(SystemConfig(id=1, initialized=True, registration_open=True))
        db.commit()

    factory = sessionmaker(engine, expire_on_commit=False)

    def database(request: Request):
        yield from database_session(factory, read_only=request_is_read_only(request))

    app.dependency_overrides[get_db] = database
    _buckets.clear()
    test_client = TestClient(app)
    test_client.test_engine = engine
    yield test_client, engine
    app.dependency_overrides.clear()
    engine.dispose()
