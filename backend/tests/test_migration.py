import base64
import json
import sqlite3
from datetime import UTC, datetime
from pathlib import Path

from alembic import command as migrations
from alembic.config import Config
from sqlalchemy import create_engine, text

from backend.tests.test_workflows import classroom, draft, paired, publish, user
from scripts.migrate_database import migrate


def test_database_migration_preserves_data_and_refuses_nonempty_target(client, tmp_path):
    c, engine = client
    h, _ = user(c)
    cid = classroom(c, h)
    publish(c, h, draft(c, h, [cid]))
    paired(c, h, cid)
    root = Path(__file__).resolve().parents[2]
    config = Config(str(root / "alembic.ini"))
    config.set_main_option("script_location", str(root / "backend/migrations"))
    with engine.begin() as connection:
        config.attributes["connection"] = connection
        migrations.stamp(config, "head")
    target = tmp_path / "migrated.db"
    counts = migrate(str(engine.url), f"sqlite:///{target}")
    assert counts["contents"] == 1 and counts["devices"] == 1
    with sqlite3.connect(target) as db:
        assert db.execute("SELECT count(*) FROM sessions").fetchone()[0] == 0
        assert db.execute("SELECT revoked FROM devices").fetchone()[0] == 1
        assert db.execute("PRAGMA foreign_key_check").fetchone() is None
    try:
        migrate(str(engine.url), f"sqlite:///{target}")
    except ValueError as error:
        assert "空数据库" in str(error)
    else:
        raise AssertionError("不得覆盖非空目标数据库")


def test_security_upgrade_preserves_existing_account(tmp_path):
    root = Path(__file__).resolve().parents[2]
    config = Config(str(root / "alembic.ini"))
    config.set_main_option("script_location", str(root / "backend/migrations"))
    engine = create_engine(f"sqlite:///{tmp_path / 'before-security.db'}")
    try:
        with engine.begin() as connection:
            config.attributes["connection"] = connection
            migrations.upgrade(config, "d1529b3fc804")
            connection.execute(
                text(
                    "INSERT INTO users (id, login, name, password, active, system_admin) "
                    "VALUES ('existing', 'existing_teacher', '原有老师', 'existing-hash', 1, 0)"
                )
            )
            migrations.upgrade(config, "head")
            row = connection.execute(text("SELECT name, password, frozen_until, freeze_reason FROM users")).one()
            assert tuple(row) == ("原有老师", "existing-hash", 0, "")
    finally:
        engine.dispose()


def test_reliability_upgrade_and_asset_downgrade_preserve_legacy_data(tmp_path):
    root = Path(__file__).resolve().parents[2]
    config = Config(str(root / "alembic.ini"))
    config.set_main_option("script_location", str(root / "backend/migrations"))
    engine = create_engine(f"sqlite:///{tmp_path / 'legacy.db'}")
    picture = "data:image/jpeg;base64," + base64.b64encode(b"legacy-image").decode()
    layout = {"revision": 2, "backgroundImage": picture, "canvas": [{"image": picture}]}
    content = {
        "from": "2026-11-05T08:00:00+08:00",
        "until": "2026-11-06T08:00:00+08:00",
        "created": "2026-11-04T12:00:00+08:00",
        "body": "保留正文",
    }
    try:
        with engine.begin() as connection:
            config.attributes["connection"] = connection
            migrations.upgrade(config, "a913c07b5e2f")
            connection.execute(
                text(
                    "INSERT INTO users (id,login,name,password,active,system_admin) VALUES ('u','legacy','老师','hash',1,0)"
                )
            )
            connection.execute(
                text(
                    "INSERT INTO classrooms (id,owner_id,name,stage,archived,version,revision,data) VALUES ('c','u','原班级','小学',0,3,2,:data)"
                ),
                {"data": json.dumps({"layout": layout})},
            )
            connection.execute(
                text(
                    "INSERT INTO contents (id,class_id,author_id,revision,state,data) VALUES ('record','c','u',1,'published',:data)"
                ),
                {"data": json.dumps(content)},
            )
            connection.execute(
                text(
                    "INSERT INTO sessions (digest,user_id,expires,epoch) VALUES ('existing-digest','u',1999999999,'old-epoch')"
                )
            )
            migrations.upgrade(config, "head")
            assert connection.execute(text("SELECT count(*) FROM class_assets")).scalar() == 1
            migrated = json.loads(connection.execute(text("SELECT data FROM classrooms")).scalar())
            assert migrated["layout"]["backgroundImage"].startswith("asset:")
            assert tuple(connection.execute(text("SELECT display_from,display_until FROM contents")).one()) == (
                datetime(2026, 11, 5, tzinfo=UTC).timestamp(),
                datetime(2026, 11, 6, tzinfo=UTC).timestamp(),
            )
            assert connection.execute(text("SELECT id FROM sessions WHERE digest='existing-digest'")).scalar()
            migrations.downgrade(config, "a913c07b5e2f")
            assert json.loads(connection.execute(text("SELECT data FROM classrooms")).scalar())["layout"] == layout
            assert json.loads(connection.execute(text("SELECT data FROM contents")).scalar()) == content
    finally:
        engine.dispose()
