"""在服务器终端读取首次安装密钥；初始化完成后不再提供入口。"""

from sqlalchemy.orm import Session

from backend.db import engine
from backend.system_service import ensure_setup_key, setup_key_file, system_config


def main():
    with Session(engine) as db:
        if system_config(db).initialized:
            print("系统已初始化，安装入口已锁定。请使用系统管理员账号登录。")
            return
    ensure_setup_key()
    print("首次安装密钥（只在初始化页面输入，不要发送给其他人）：")
    print(setup_key_file().read_text(encoding="utf-8"))


if __name__ == "__main__":
    main()
