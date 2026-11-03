"""旧命令兼容提示；不再生成或输出已经停用的网页安装密钥。"""


def main():
    print("管理员初始化已移到服务器菜单：sudo classglow admin-init。")
    print("本机开发使用：uv run python -m scripts.admin_account init。")


if __name__ == "__main__":
    main()
