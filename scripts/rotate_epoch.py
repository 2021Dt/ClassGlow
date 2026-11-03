"""备份还原后、开放服务前执行，使旧会话、旧设备及排队命令永久失效。"""

from sqlalchemy import select

from backend.db import Command, Device, Meta, SessionLocal, Token, uid

with SessionLocal.begin() as db:
    epoch = db.get(Meta, "sync_epoch")
    if not epoch:
        epoch = Meta(key="sync_epoch", value=uid())
        db.add(epoch)
    else:
        epoch.value = uid()
    for token in db.scalars(select(Token)):
        db.delete(token)
    for device in db.scalars(select(Device)):
        device.revoked = True
    for command in db.scalars(select(Command).where(Command.state.in_(["accepted", "deferred"]))):
        command.state = "rejected"
print("同步世代已更新；教师需要重新登录，所有展示设备需要重新绑定。")
