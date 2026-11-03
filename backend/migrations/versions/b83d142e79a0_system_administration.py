"""增加系统初始化、全局管理员和系统审计，现有教师账号及班级保持原有归属。"""

import sqlalchemy as sa
from alembic import op

revision = "b83d142e79a0"
down_revision = "a6410dd9f120"
branch_labels = None
depends_on = None


def upgrade():
    op.add_column("users", sa.Column("active", sa.Boolean(), nullable=False, server_default=sa.true()))
    op.add_column("users", sa.Column("system_admin", sa.Boolean(), nullable=False, server_default=sa.false()))
    op.create_table(
        "system_config",
        sa.Column("id", sa.Integer(), primary_key=True),
        sa.Column("initialized", sa.Boolean(), nullable=False),
        sa.Column("site_name", sa.String(80), nullable=False),
        sa.Column("registration_open", sa.Boolean(), nullable=False),
        sa.Column("initialized_at", sa.Float(), nullable=False),
    )
    op.create_table(
        "system_audit",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("actor", sa.String(80), nullable=False),
        sa.Column("action", sa.String(60), nullable=False),
        sa.Column("target", sa.String(200), nullable=False),
        sa.Column("data", sa.JSON(), nullable=False),
        sa.Column("at", sa.Float(), nullable=False),
    )
    op.bulk_insert(
        sa.table(
            "system_config",
            sa.column("id", sa.Integer()),
            sa.column("initialized", sa.Boolean()),
            sa.column("site_name", sa.String()),
            sa.column("registration_open", sa.Boolean()),
            sa.column("initialized_at", sa.Float()),
        ),
        [{"id": 1, "initialized": False, "site_name": "班级看板", "registration_open": False, "initialized_at": 0}],
    )


def downgrade():
    op.drop_table("system_audit")
    op.drop_table("system_config")
    op.drop_column("users", "system_admin")
    op.drop_column("users", "active")
