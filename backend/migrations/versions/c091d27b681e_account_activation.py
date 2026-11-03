"""账号一次性激活；现有账号继续登录，保留原有权限。"""

import sqlalchemy as sa
from alembic import op

revision = "c091d27b681e"
down_revision = "b83d142e79a0"
branch_labels = None
depends_on = None


def upgrade():
    op.add_column("users", sa.Column("activated", sa.Boolean(), nullable=False, server_default=sa.true()))
    op.add_column("users", sa.Column("can_create_classes", sa.Boolean(), nullable=False, server_default=sa.true()))
    op.add_column("users", sa.Column("expires_at", sa.Float(), nullable=False, server_default="0"))
    op.add_column("users", sa.Column("premium_until", sa.Float(), nullable=False, server_default="0"))
    op.create_table(
        "account_activations",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), nullable=False),
        sa.Column("code_hash", sa.String(64), nullable=False, unique=True),
        sa.Column("epoch", sa.String(36), nullable=False),
        sa.Column("expires", sa.Float(), nullable=False),
        sa.Column("used_at", sa.Float(), nullable=False),
        sa.Column("revoked", sa.Boolean(), nullable=False),
    )
    op.create_index("ix_account_activations_user_id", "account_activations", ["user_id"])
    op.create_table(
        "feature_licenses",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), nullable=False),
        sa.Column("code_hash", sa.String(64), nullable=False, unique=True),
        sa.Column("epoch", sa.String(36), nullable=False),
        sa.Column("days", sa.Integer(), nullable=False),
        sa.Column("expires", sa.Float(), nullable=False),
        sa.Column("used_at", sa.Float(), nullable=False),
        sa.Column("revoked", sa.Boolean(), nullable=False),
    )
    op.create_index("ix_feature_licenses_user_id", "feature_licenses", ["user_id"])


def downgrade():
    op.drop_table("feature_licenses")
    op.drop_table("account_activations")
    op.drop_column("users", "can_create_classes")
    op.drop_column("users", "activated")
    op.drop_column("users", "expires_at")
    op.drop_column("users", "premium_until")
