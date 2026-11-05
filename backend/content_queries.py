"""展示只取仍需展示的内容，历史始终由分页接口保留和查询。"""

from sqlalchemy import select

from .db import Content, now


def display_contents(class_ids, timestamp=None):
    timestamp = now() if timestamp is None else timestamp
    # 所有未来发布都要进入离线缓存；不能只取当前页或截断前 N 条。
    return (
        select(Content)
        .where(Content.class_id.in_(class_ids), Content.state == "published", Content.display_until > timestamp)
        .order_by(Content.display_from, Content.id)
    )
