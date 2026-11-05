"""分离布局图片并按内容去重；备份仍只需要一致的数据库副本。"""

import base64
import hashlib

import sqlalchemy as sa
from alembic import op

revision = "f4859e63cb27"
down_revision = "f3748d52ba16"
branch_labels = None
depends_on = None


def images(value, convert):
    if isinstance(value, dict):
        return {
            key: convert(item)
            if key in ("image", "backgroundImage", "panelImage") and isinstance(item, str)
            else images(item, convert)
            for key, item in value.items()
        }
    return [images(item, convert) for item in value] if isinstance(value, list) else value


def tables():
    return (
        sa.table("classrooms", sa.column("id", sa.String()), sa.column("data", sa.JSON())),
        sa.table(
            "class_assets",
            sa.column("class_id", sa.String()),
            sa.column("digest", sa.String()),
            sa.column("mime", sa.String()),
            sa.column("data", sa.LargeBinary()),
        ),
    )


def upgrade():
    op.create_table(
        "class_assets",
        sa.Column("class_id", sa.String(36), sa.ForeignKey("classrooms.id"), primary_key=True),
        sa.Column("digest", sa.String(64), primary_key=True),
        sa.Column("mime", sa.String(32), nullable=False),
        sa.Column("data", sa.LargeBinary(), nullable=False),
    )
    connection = op.get_bind()
    classes, assets = tables()
    last_id = ""
    while rows := connection.execute(
        sa.select(classes.c.id, classes.c.data).where(classes.c.id > last_id).order_by(classes.c.id).limit(20)
    ).all():
        for row in rows:
            seen = set()

            def store(picture, class_id=row.id, seen=seen):
                if not picture:
                    return picture
                prefix, encoded = picture.split(",", 1)
                raw = base64.b64decode(encoded, validate=True)
                identity = hashlib.sha256(raw).hexdigest()
                if identity not in seen:
                    connection.execute(
                        assets.insert().values(
                            class_id=class_id,
                            digest=identity,
                            mime=prefix[5:].split(";")[0],
                            data=raw,
                        )
                    )
                    seen.add(identity)
                return "asset:" + identity

            if "layout" in row.data:
                data = {**row.data, "layout": images(row.data["layout"], store)}
                connection.execute(classes.update().where(classes.c.id == row.id).values(data=data))
        last_id = rows[-1].id


def downgrade():
    connection = op.get_bind()
    classes, assets = tables()
    last_id = ""
    while rows := connection.execute(
        sa.select(classes.c.id, classes.c.data).where(classes.c.id > last_id).order_by(classes.c.id).limit(20)
    ).all():
        for row in rows:

            def restore(picture, class_id=row.id):
                if not picture.startswith("asset:"):
                    return picture
                asset = connection.execute(
                    sa.select(assets.c.mime, assets.c.data).where(
                        assets.c.class_id == class_id, assets.c.digest == picture[6:]
                    )
                ).one()
                return f"data:{asset.mime};base64," + base64.b64encode(asset.data).decode()

            if "layout" in row.data:
                data = {**row.data, "layout": images(row.data["layout"], restore)}
                connection.execute(classes.update().where(classes.c.id == row.id).values(data=data))
        last_id = rows[-1].id
    op.drop_table("class_assets")
