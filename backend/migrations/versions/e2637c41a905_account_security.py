"""临时安全冻结、短期登录记录与一次性密码恢复；保留现有账号和教室授权。"""

import sqlalchemy as sa
from alembic import op

revision = "e2637c41a905"
down_revision = "d1529b3fc804"
branch_labels = None
depends_on = None


def upgrade():
    op.add_column("users", sa.Column("frozen_until", sa.Float(), nullable=False, server_default="0"))
    op.add_column("users", sa.Column("freeze_reason", sa.String(200), nullable=False, server_default=""))
    op.add_column("users", sa.Column("security_reviewed_at", sa.Float(), nullable=False, server_default="0"))
    op.add_column("sessions", sa.Column("revoked_reason", sa.String(40), nullable=False, server_default=""))
    op.create_table(
        "login_records",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), nullable=False),
        sa.Column("at", sa.Float(), nullable=False),
        sa.Column("client_hash", sa.String(64), nullable=False),
        sa.Column("ip", sa.String(64), nullable=False),
    )
    op.create_index("ix_login_records_user_id", "login_records", ["user_id"])
    op.create_index("ix_login_records_at", "login_records", ["at"])
    op.create_table(
        "password_recoveries",
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), primary_key=True),
        sa.Column("code_hash", sa.String(64), unique=True, nullable=False),
        sa.Column("epoch", sa.String(36), nullable=False),
        sa.Column("expires", sa.Float(), nullable=False),
        sa.Column("attempts", sa.Integer(), nullable=False),
    )


def downgrade():
    op.drop_table("password_recoveries")
    op.drop_table("login_records")
    op.drop_column("sessions", "revoked_reason")
    op.drop_column("users", "security_reviewed_at")
    op.drop_column("users", "freeze_reason")
    op.drop_column("users", "frozen_until")
