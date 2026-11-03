import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.orm import Session

from backend.app import app
from backend.db import Base, Meta, SystemConfig, get_db
from backend.security import _buckets


@pytest.fixture
def client(tmp_path):
    """每个用例使用独立 SQLite 文件，事务行为与本地服务一致，避免污染实际班级。"""
    engine = create_engine(f"sqlite:///{tmp_path / 'test.db'}", connect_args={"check_same_thread": False})
    Base.metadata.create_all(engine)
    with Session(engine) as db:
        db.add(Meta(key="sync_epoch", value="test-epoch"))
        db.add(SystemConfig(id=1, initialized=True, registration_open=True))
        db.commit()

    def database():
        with Session(engine, expire_on_commit=False) as db:
            db.connection().exec_driver_sql("BEGIN IMMEDIATE")
            try:
                yield db
                db.commit()
            except Exception:
                db.rollback()
                raise

    app.dependency_overrides[get_db] = database
    _buckets.clear()
    yield TestClient(app), engine
    app.dependency_overrides.clear()
    engine.dispose()
