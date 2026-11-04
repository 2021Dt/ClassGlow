"""站点公告、指定账号与弹出记录。"""

import sqlalchemy as sa
from alembic import op

revision = "a913c07b5e2f"
down_revision = "e2637c41a905"
branch_labels = None
depends_on = None


def upgrade():
    op.create_table(
        "announcements",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("title", sa.String(120), nullable=False),
        sa.Column("markdown", sa.String(16000), nullable=False),
        sa.Column("scope", sa.String(10), nullable=False),
        sa.Column("published_at", sa.Float(), nullable=False),
        sa.Column("withdrawn_at", sa.Float()),
    )
    op.create_index("ix_announcements_published_at", "announcements", ["published_at"])
    op.create_table(
        "announcement_targets",
        sa.Column("announcement_id", sa.String(36), sa.ForeignKey("announcements.id"), primary_key=True),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), primary_key=True),
    )
    op.create_table(
        "announcement_receipts",
        sa.Column("announcement_id", sa.String(36), sa.ForeignKey("announcements.id"), primary_key=True),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), primary_key=True),
        sa.Column("shown_at", sa.Float(), nullable=False),
        sa.Column("claim_key", sa.String(36)),
        sa.UniqueConstraint("user_id", "claim_key", name="uq_announcement_receipt_claim"),
    )


def downgrade():
    op.drop_table("announcement_receipts")
    op.drop_table("announcement_targets")
    op.drop_table("announcements")
