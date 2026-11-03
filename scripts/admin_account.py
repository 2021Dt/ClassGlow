"""服务器本机初始化/重置管理员；密码仅从终端隐藏输入，不进入参数、环境变量或日志。"""

import getpass
import sys

from pydantic import ValidationError
from sqlalchemy import delete, select

from backend.db import SessionLocal, Token, User, now
from backend.schemas import Credentials
from backend.security import passwords
from backend.system_service import log_system, system_config


def initialize(db, credentials):
    config = system_config(db, lock=True)
    if config.initialized:
        raise ValueError("管理员已经初始化；如忘记密码，请使用重置管理员密码。")
    if db.scalar(select(User).where(User.login == credentials.login.casefold())):
        raise ValueError("该账号已存在，请为首个管理员选择另一个账号。")
    admin = User(
        login=credentials.login.casefold(),
        name=credentials.name,
        password=passwords.hash(credentials.password),
        system_admin=True,
        activated=True,
        can_create_classes=True,
    )
    db.add(admin)
    config.initialized, config.initialized_at, config.registration_open = True, now(), False
    log_system(db, "server-console", "system_initialized", admin.login)
    return admin


def main():
    action = sys.argv[1] if len(sys.argv) > 1 else "init"
    if action not in ("init", "reset") or not sys.stdin.isatty():
        raise SystemExit("请在服务器交互终端运行 sudo classglow admin-init 或 admin-reset。")
    if action == "init":
        with SessionLocal() as db:
            if system_config(db).initialized:
                raise SystemExit("管理员已经初始化；如忘记密码，请在菜单选择重置管理员密码。")
    print("初始化管理员" if action == "init" else "重置已有管理员密码（会退出该账号的所有登录）")
    login = input("管理员账号：").strip()
    password = getpass.getpass("密码（至少 10 个字符，不回显）：")
    if password != getpass.getpass("再次输入密码："):
        raise SystemExit("两次密码不一致，没有修改账号。")
    try:
        credentials = Credentials(login=login, password=password, name="系统管理员")
        with SessionLocal() as db:
            if db.bind.dialect.name == "sqlite":
                db.connection().exec_driver_sql("BEGIN IMMEDIATE")
            if action == "init":
                initialize(db, credentials)
            else:
                admin = db.scalar(
                    select(User).where(User.login == login.casefold(), User.system_admin.is_(True)).with_for_update()
                )
                if not admin:
                    raise ValueError("没有找到该系统管理员，没有修改任何账号。")
                admin.password = passwords.hash(password)
                admin.active, admin.activated, admin.expires_at = True, True, 0
                db.execute(delete(Token).where(Token.user_id == admin.id))
                log_system(db, "server-console", "password_reset", admin.login)
            db.commit()
    except ValidationError:
        raise SystemExit("账号需为 3–80 位字母、数字或邮箱字符；密码需为 10–128 位。") from None
    except ValueError as error:
        raise SystemExit(str(error)) from None
    print("已完成。请打开网站，使用刚才的账号和密码登录管理员后台。")


if __name__ == "__main__":
    main()
