"""教室看板按需回传，每台设备最多保留一张画面。"""

import sqlalchemy as sa
from alembic import op

revision = "d1529b3fc804"
down_revision = "c091d27b681e"
branch_labels = None
depends_on = None


def upgrade():
    op.create_table(
        "device_displays",
        sa.Column("device_id", sa.String(36), sa.ForeignKey("devices.id"), primary_key=True),
        sa.Column("request_id", sa.String(36), nullable=False),
        sa.Column("requested_at", sa.Float(), nullable=False),
        sa.Column("received_at", sa.Float(), nullable=False),
        sa.Column("data", sa.JSON(), nullable=False),
        sa.Column("image", sa.LargeBinary(), nullable=False),
    )


def downgrade():
    op.drop_table("device_displays")
