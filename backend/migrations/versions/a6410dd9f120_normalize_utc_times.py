"""统一已有业务时间为 UTC，保持同一时刻及现有修订语义。

Revision ID: a6410dd9f120
Revises: 3fc3d970f5fe
"""

from datetime import UTC, datetime

import sqlalchemy as sa
from alembic import op

revision = "a6410dd9f120"
down_revision = "3fc3d970f5fe"
branch_labels = None
depends_on = None


def upgrade():
    connection = op.get_bind()
    for table_name in ("contents", "drafts"):
        table = sa.table(table_name, sa.column("id", sa.String), sa.column("data", sa.JSON))
        for row in connection.execute(sa.select(table.c.id, table.c.data)).mappings():
            data = dict(row["data"])
            for key in ("from", "until", "due"):
                if data.get(key):
                    parsed = datetime.fromisoformat(data[key])
                    if parsed.tzinfo is None:
                        raise RuntimeError("历史时间缺少时区，请人工确认，迁移不会猜测。")
                    data[key] = parsed.astimezone(UTC).isoformat()
            connection.execute(sa.update(table).where(table.c.id == row["id"]).values(data=data))


def downgrade():
    # UTC 标准化没有语义损失，不把已标准化时间改回混合偏移。
    pass
