"""为内容时间查询建立索引，保留旧会话并补充可撤销的设备标识。"""

from datetime import datetime
from uuid import uuid4

import sqlalchemy as sa
from alembic import op

revision = "f3748d52ba16"
down_revision = "a913c07b5e2f"
branch_labels = None
depends_on = None


def upgrade():
    connection = op.get_bind()
    for name in ("display_from", "display_until", "created_at"):
        op.add_column("contents", sa.Column(name, sa.Float(), nullable=False, server_default="0"))
    contents = sa.table(
        "contents",
        sa.column("id", sa.String()),
        sa.column("data", sa.JSON()),
        sa.column("display_from", sa.Float()),
        sa.column("display_until", sa.Float()),
        sa.column("created_at", sa.Float()),
    )
    # 分批回填，不把一个学期的正文全部载入迁移进程。
    last_id = ""
    while rows := connection.execute(
        sa.select(contents.c.id, contents.c.data).where(contents.c.id > last_id).order_by(contents.c.id).limit(500)
    ).all():
        for row in rows:
            values = {}
            for field, key in (("display_from", "from"), ("display_until", "until"), ("created_at", "created")):
                values[field] = datetime.fromisoformat(row.data[key]).timestamp() if row.data.get(key) else 0
            connection.execute(contents.update().where(contents.c.id == row.id).values(**values))
        last_id = rows[-1].id
    op.create_index("ix_contents_class_state_until", "contents", ["class_id", "state", "display_until"])
    op.create_index("ix_contents_class_created", "contents", ["class_id", "created_at", "id"])

    op.add_column("sessions", sa.Column("id", sa.String(36), nullable=True))
    op.add_column("sessions", sa.Column("client_hash", sa.String(64), nullable=False, server_default=""))
    op.add_column("sessions", sa.Column("label", sa.String(80), nullable=False, server_default="浏览器（升级前登录）"))
    op.add_column("sessions", sa.Column("created_at", sa.Float(), nullable=False, server_default="0"))
    sessions = sa.table("sessions", sa.column("digest", sa.String()), sa.column("id", sa.String()))
    for row in connection.execute(sa.select(sessions.c.digest)).all():
        connection.execute(sessions.update().where(sessions.c.digest == row.digest).values(id=str(uuid4())))
    with op.batch_alter_table("sessions") as batch:
        batch.alter_column("id", existing_type=sa.String(36), nullable=False)
    op.create_index("uq_sessions_id", "sessions", ["id"], unique=True)
    op.create_index("ix_sessions_user_expires", "sessions", ["user_id", "expires"])


def downgrade():
    op.drop_index("ix_sessions_user_expires", "sessions")
    op.drop_index("uq_sessions_id", "sessions")
    for name in ("created_at", "label", "client_hash", "id"):
        op.drop_column("sessions", name)
    op.drop_index("ix_contents_class_created", "contents")
    op.drop_index("ix_contents_class_state_until", "contents")
    for name in ("created_at", "display_until", "display_from"):
        op.drop_column("contents", name)
