"""initial schema

Revision ID: 3fc3d970f5fe
Revises:
"""

from uuid import uuid4

import sqlalchemy as sa
from alembic import op

revision = "3fc3d970f5fe"
down_revision = None
branch_labels = None
depends_on = None


def upgrade():
    # 初始结构固定在此迁移中，后续模型修改必须新增迁移。
    op.create_table(
        "idempotency",
        sa.Column("key", sa.String(length=200), nullable=False),
        sa.Column("request_hash", sa.String(length=64), nullable=False),
        sa.Column("result", sa.JSON(), nullable=False),
        sa.PrimaryKeyConstraint("key"),
    )
    op.create_table(
        "metadata",
        sa.Column("key", sa.String(length=60), nullable=False),
        sa.Column("value", sa.String(length=200), nullable=False),
        sa.PrimaryKeyConstraint("key"),
    )
    op.create_table(
        "pairings",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("code_hash", sa.String(length=64), nullable=False),
        sa.Column("secret_hash", sa.String(length=64), nullable=False),
        sa.Column("name", sa.String(length=60), nullable=False),
        sa.Column("expires", sa.Float(), nullable=False),
        sa.Column("consumed", sa.Boolean(), nullable=False),
        sa.Column("device_id", sa.String(length=36), nullable=True),
        sa.Column("credential", sa.String(length=200), nullable=True),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("code_hash"),
    )
    op.create_table(
        "users",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("login", sa.String(length=80), nullable=False),
        sa.Column("name", sa.String(length=40), nullable=False),
        sa.Column("password", sa.String(length=300), nullable=False),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("login"),
    )
    op.create_table(
        "classrooms",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("owner_id", sa.String(length=36), nullable=False),
        sa.Column("name", sa.String(length=60), nullable=False),
        sa.Column("stage", sa.String(length=20), nullable=False),
        sa.Column("archived", sa.Boolean(), nullable=False),
        sa.Column("version", sa.Integer(), nullable=False),
        sa.Column("revision", sa.Integer(), nullable=False),
        sa.Column("data", sa.JSON(), nullable=False),
        sa.ForeignKeyConstraint(
            ["owner_id"],
            ["users.id"],
        ),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_table(
        "drafts",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("owner_id", sa.String(length=36), nullable=False),
        sa.Column("revision", sa.Integer(), nullable=False),
        sa.Column("closed", sa.Boolean(), nullable=False),
        sa.Column("data", sa.JSON(), nullable=False),
        sa.Column("updated", sa.Float(), nullable=False),
        sa.ForeignKeyConstraint(
            ["owner_id"],
            ["users.id"],
        ),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(op.f("ix_drafts_owner_id"), "drafts", ["owner_id"], unique=False)
    op.create_table(
        "sessions",
        sa.Column("digest", sa.String(length=64), nullable=False),
        sa.Column("user_id", sa.String(length=36), nullable=False),
        sa.Column("expires", sa.Float(), nullable=False),
        sa.Column("epoch", sa.String(length=36), nullable=False),
        sa.ForeignKeyConstraint(
            ["user_id"],
            ["users.id"],
        ),
        sa.PrimaryKeyConstraint("digest"),
    )
    op.create_table(
        "audit_log",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("class_id", sa.String(length=36), nullable=False),
        sa.Column("actor", sa.String(length=36), nullable=False),
        sa.Column("action", sa.String(length=60), nullable=False),
        sa.Column("data", sa.JSON(), nullable=False),
        sa.Column("at", sa.Float(), nullable=False),
        sa.ForeignKeyConstraint(
            ["class_id"],
            ["classrooms.id"],
        ),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(op.f("ix_audit_log_class_id"), "audit_log", ["class_id"], unique=False)
    op.create_table(
        "contents",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("class_id", sa.String(length=36), nullable=False),
        sa.Column("author_id", sa.String(length=36), nullable=False),
        sa.Column("revision", sa.Integer(), nullable=False),
        sa.Column("state", sa.String(length=12), nullable=False),
        sa.Column("data", sa.JSON(), nullable=False),
        sa.ForeignKeyConstraint(
            ["author_id"],
            ["users.id"],
        ),
        sa.ForeignKeyConstraint(
            ["class_id"],
            ["classrooms.id"],
        ),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(op.f("ix_contents_class_id"), "contents", ["class_id"], unique=False)
    op.create_table(
        "devices",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("class_id", sa.String(length=36), nullable=False),
        sa.Column("name", sa.String(length=60), nullable=False),
        sa.Column("binding", sa.String(length=36), nullable=False),
        sa.Column("credential_hash", sa.String(length=64), nullable=False),
        sa.Column("epoch", sa.String(length=36), nullable=False),
        sa.Column("revoked", sa.Boolean(), nullable=False),
        sa.Column("last_seen", sa.Float(), nullable=False),
        sa.Column("control_revision", sa.Integer(), nullable=False),
        sa.Column("report_seq", sa.Integer(), nullable=False),
        sa.Column("state", sa.JSON(), nullable=False),
        sa.ForeignKeyConstraint(
            ["class_id"],
            ["classrooms.id"],
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("credential_hash"),
    )
    op.create_index(op.f("ix_devices_class_id"), "devices", ["class_id"], unique=False)
    op.create_table(
        "invitations",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("class_id", sa.String(length=36), nullable=False),
        sa.Column("digest", sa.String(length=64), nullable=False),
        sa.Column("subjects", sa.JSON(), nullable=False),
        sa.Column("applicant", sa.String(length=36), nullable=True),
        sa.Column("state", sa.String(length=16), nullable=False),
        sa.Column("expires", sa.Float(), nullable=False),
        sa.ForeignKeyConstraint(
            ["class_id"],
            ["classrooms.id"],
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("digest"),
    )
    op.create_table(
        "memberships",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("class_id", sa.String(length=36), nullable=False),
        sa.Column("user_id", sa.String(length=36), nullable=False),
        sa.Column("role", sa.String(length=10), nullable=False),
        sa.Column("subjects", sa.JSON(), nullable=False),
        sa.Column("active", sa.Boolean(), nullable=False),
        sa.ForeignKeyConstraint(
            ["class_id"],
            ["classrooms.id"],
        ),
        sa.ForeignKeyConstraint(
            ["user_id"],
            ["users.id"],
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("class_id", "user_id"),
    )
    op.create_index(op.f("ix_memberships_class_id"), "memberships", ["class_id"], unique=False)
    op.create_table(
        "ownership_transfers",
        sa.Column("class_id", sa.String(length=36), nullable=False),
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("sender", sa.String(length=36), nullable=False),
        sa.Column("recipient", sa.String(length=36), nullable=False),
        sa.Column("state", sa.String(length=16), nullable=False),
        sa.Column("expires", sa.Float(), nullable=False),
        sa.ForeignKeyConstraint(
            ["class_id"],
            ["classrooms.id"],
        ),
        sa.PrimaryKeyConstraint("class_id"),
        sa.UniqueConstraint("id"),
    )
    op.create_table(
        "content_revisions",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("content_id", sa.String(length=36), nullable=False),
        sa.Column("revision", sa.Integer(), nullable=False),
        sa.Column("actor_id", sa.String(length=36), nullable=False),
        sa.Column("reason", sa.String(length=100), nullable=False),
        sa.Column("before", sa.JSON(), nullable=False),
        sa.Column("after", sa.JSON(), nullable=False),
        sa.Column("at", sa.Float(), nullable=False),
        sa.ForeignKeyConstraint(
            ["actor_id"],
            ["users.id"],
        ),
        sa.ForeignKeyConstraint(
            ["content_id"],
            ["contents.id"],
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("content_id", "revision"),
    )
    op.create_index(op.f("ix_content_revisions_content_id"), "content_revisions", ["content_id"], unique=False)
    op.create_table(
        "device_commands",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("device_id", sa.String(length=36), nullable=False),
        sa.Column("actor_id", sa.String(length=36), nullable=False),
        sa.Column("binding", sa.String(length=36), nullable=False),
        sa.Column("epoch", sa.String(length=36), nullable=False),
        sa.Column("seq", sa.Integer(), nullable=False),
        sa.Column("action", sa.String(length=16), nullable=False),
        sa.Column("group", sa.String(length=12), nullable=False),
        sa.Column("issued", sa.Float(), nullable=False),
        sa.Column("expires", sa.Float(), nullable=False),
        sa.Column("state", sa.String(length=20), nullable=False),
        sa.Column("data", sa.JSON(), nullable=False),
        sa.ForeignKeyConstraint(
            ["actor_id"],
            ["users.id"],
        ),
        sa.ForeignKeyConstraint(
            ["device_id"],
            ["devices.id"],
        ),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(op.f("ix_device_commands_device_id"), "device_commands", ["device_id"], unique=False)
    op.get_bind().execute(
        sa.text("INSERT INTO metadata (key, value) VALUES (:key, :value)"), {"key": "sync_epoch", "value": str(uuid4())}
    )


def downgrade():
    # 回退会删除业务表，仅供未投入使用的开发环境显式操作。
    op.drop_index(op.f("ix_device_commands_device_id"), table_name="device_commands")
    op.drop_table("device_commands")
    op.drop_index(op.f("ix_content_revisions_content_id"), table_name="content_revisions")
    op.drop_table("content_revisions")
    op.drop_table("ownership_transfers")
    op.drop_index(op.f("ix_memberships_class_id"), table_name="memberships")
    op.drop_table("memberships")
    op.drop_table("invitations")
    op.drop_index(op.f("ix_devices_class_id"), table_name="devices")
    op.drop_table("devices")
    op.drop_index(op.f("ix_contents_class_id"), table_name="contents")
    op.drop_table("contents")
    op.drop_index(op.f("ix_audit_log_class_id"), table_name="audit_log")
    op.drop_table("audit_log")
    op.drop_table("sessions")
    op.drop_index(op.f("ix_drafts_owner_id"), table_name="drafts")
    op.drop_table("drafts")
    op.drop_table("classrooms")
    op.drop_table("users")
    op.drop_table("pairings")
    op.drop_table("metadata")
    op.drop_table("idempotency")
