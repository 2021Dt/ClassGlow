"""由 release.json 同步版本；CI 与打包均检查漂移，不连接部署服务器。"""

import argparse
import json
import re
import tomllib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def expected_files():
    release = json.loads((ROOT / "release.json").read_text(encoding="utf-8"))
    version = release["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("发布版本必须是三段数字。")
    paths = (
        "desktop/ClassroomBoard/ClassroomBoard.csproj",
        "pyproject.toml",
        "frontend/package.json",
        "frontend/package-lock.json",
    )
    result = {}
    for name in paths:
        value = (ROOT / name).read_text(encoding="utf-8")
        if name.endswith("csproj"):
            value = re.sub(r"<Version>[^<]+</Version>", f"<Version>{version}</Version>", value)
        elif name.endswith("toml"):
            value = re.sub(r'^version = "[^"]+"', f'version = "{version}"', value, count=1, flags=re.MULTILINE)
        else:
            # 只更新包自身的版本，绝不能替换依赖包的 version 字段。
            value = re.sub(r'("version":\s*")[^"]+', lambda match: match[1] + version, value, count=1)
            if name.endswith("package-lock.json"):
                value = re.sub(
                    r'("":\s*\{\s*"name":\s*"[^"]+",\s*"version":\s*")[^"]+',
                    lambda match: match[1] + version,
                    value,
                    count=1,
                )
        result[name] = value
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--runtime", action="store_true", help="容器内只检查服务版本与迁移链")
    args = parser.parse_args()
    if args.runtime:
        from alembic.config import Config
        from alembic.script import ScriptDirectory

        from backend.version import VERSION

        project = tomllib.loads((ROOT / "pyproject.toml").read_text(encoding="utf-8"))
        config = Config(str(ROOT / "alembic.ini"))
        config.set_main_option("script_location", str(ROOT / "backend/migrations"))
        if project["project"]["version"] != VERSION or len(ScriptDirectory.from_config(config).get_heads()) != 1:
            raise SystemExit("服务版本不一致或迁移链存在多个分支，停止部署。")
        print("容器版本与迁移链检查通过。")
        return
    changed = []
    for name, expected in expected_files().items():
        path = ROOT / name
        if path.read_text(encoding="utf-8") != expected:
            changed.append(name)
            if args.write:
                path.write_text(expected, encoding="utf-8", newline="\n")
    if changed and not args.write:
        raise SystemExit("版本未同步，请运行 uv run python -m scripts.release_check --write：" + ", ".join(changed))
    print("发布版本检查通过。")


if __name__ == "__main__":
    main()
