def main():
    """本地启动入口；生产环境由进程管理器运行 ASGI 服务。"""
    import uvicorn
    # 请求路径可能含短期邀请秘密，关闭原始访问日志，诊断使用请求编号与审计记录。
    uvicorn.run("backend.app:app", host="127.0.0.1", port=8000, access_log=False)


if __name__ == "__main__":
    main()
