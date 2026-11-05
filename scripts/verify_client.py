"""启动隔离临时服务，验证真实 Windows 引擎的配对、图片同步与控制回执。"""

import os
import socket
import subprocess
import sys
import time
from contextlib import contextmanager
from pathlib import Path

import httpx

ROOT = Path(__file__).resolve().parents[1]


@contextmanager
def isolated_server():
    with socket.socket() as probe:
        if probe.connect_ex(("127.0.0.1", 8019)) == 0:
            raise SystemExit("8019 端口已占用，请结束本项目的隔离回归服务后重试。")
    env = {**os.environ, "PYTHONIOENCODING": "utf-8"}
    server = subprocess.Popen(
        [sys.executable, "-m", "scripts.e2e_server"],
        cwd=ROOT,
        env=env,
        creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
    )
    try:
        with httpx.Client(trust_env=False, timeout=2) as client:
            for _ in range(60):
                if server.poll() is not None:
                    raise RuntimeError("隔离测试服务启动失败。")
                try:
                    if client.get("http://127.0.0.1:8019/setup/status").is_success:
                        break
                except httpx.TransportError:
                    pass
                time.sleep(0.25)
            else:
                raise RuntimeError("隔离测试服务未就绪。")
        yield
    finally:
        server.terminate()
        server.wait(timeout=10)


def main():
    with isolated_server():
        env = {
            **os.environ,
            "PYTHONIOENCODING": "utf-8",
            "KANBAN_TEST_API_URL": "http://127.0.0.1:8019",
            "KANBAN_TEST_LOGIN": "e2e_admin",
            "KANBAN_TEST_PASSWORD": "E2e-Temporary-Only!2026",
        }
        subprocess.run([sys.executable, "scripts/client_integration.py"], cwd=ROOT, env=env, check=True, timeout=120)


if __name__ == "__main__":
    main()
