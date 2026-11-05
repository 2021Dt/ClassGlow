"""生成当前用户安装包及逐文件卸载清单；验证包与正式包的路径和登记完全隔离。"""

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
# 0.4.10 发布的离线资源采用哈希名称；后续版本固定文件名，仅清理这些确定归属的旧文件。
LEGACY_RENDERER_FILES = ("Renderer/assets/desktop-D_VyVcpg.js", "Renderer/assets/desktop-yi8noDad.css")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--qa", action="store_true", help="构建独立卸载验证包，不使用正式应用目录或启动项")
    parser.add_argument("--nsis", type=Path)
    args = parser.parse_args()
    subprocess.run([sys.executable, "-m", "scripts.release_check"], cwd=ROOT, check=True)
    subprocess.run([sys.executable, "-m", "scripts.generate_contracts", "--check"], cwd=ROOT, check=True)
    sdk = ROOT / ".tools/dotnet/dotnet.exe"
    dotnet = str(sdk) if sdk.exists() else shutil.which("dotnet") or "dotnet"
    nsis = args.nsis or Path(os.environ["LOCALAPPDATA"]) / "tauri/NSIS/makensis.exe"
    if not nsis.is_file():
        raise SystemExit("请安装 NSIS 3，并通过 --nsis 指定 makensis.exe；脚本不会安装全局工具。")
    version = ET.parse(ROOT / "desktop/ClassroomBoard/ClassroomBoard.csproj").findtext(".//Version")
    payload = ROOT / "dist" / ("windows-qa" if args.qa else f"windows-{version}")
    output = (
        ROOT
        / "dist"
        / (f"ClassGlow-{version}-uninstall-QA.exe" if args.qa else f"ClassGlow-{version}-win-x64-setup.exe")
    )
    build = ROOT / ".qa" / ("installer-qa" if args.qa else "installer")
    build.mkdir(parents=True, exist_ok=True)
    npm = shutil.which("npm") or "npm"
    subprocess.run(
        [npm, "exec", "vite", "build", "--", "--config", "vite.desktop.config.ts"], check=True, cwd=ROOT / "frontend"
    )
    command = [
        dotnet,
        "publish",
        str(ROOT / "desktop/ClassroomBoard/ClassroomBoard.csproj"),
        "-c",
        "Release",
        "-r",
        "win-x64",
        "--self-contained",
        "true",
        "-o",
        str(payload),
    ]
    if args.qa:
        command += ["-p:DefineConstants=LIFECYCLE_QA"]
    subprocess.run(command, check=True, cwd=ROOT)
    renderer = payload / "Renderer"
    if renderer.exists():
        # 只替换本次版本产物中的离线网页目录，不清理用户安装目录。
        assert renderer.resolve().is_relative_to((ROOT / "dist").resolve())
        shutil.rmtree(renderer)
    shutil.copytree(ROOT / "dist/desktop-material", renderer)
    shutil.copyfile(ROOT / "frontend/public/third-party-notices.txt", renderer / "third-party-notices.txt")
    privacy = (ROOT / "desktop/installer/privacy.txt").read_text(encoding="utf-8-sig")
    # NSIS 许可页单独读取文本，不能依赖 .nsi 的 /INPUTCHARSET；明确使用带 BOM 的 UTF-16。
    (build / "license.txt").write_text(privacy, encoding="utf-16")
    (payload / "隐私与卸载说明.txt").write_text(privacy, encoding="utf-8-sig")
    folders = sorted(
        (path.relative_to(payload) for path in payload.rglob("*") if path.is_dir()), key=lambda p: len(p.parts)
    )
    install = [f'!insertmacro CheckDirectory "$INSTDIR\\{folder}"' for folder in folders]
    uninstall = install.copy()
    for name in LEGACY_RENDERER_FILES:
        relative = Path(name)
        install += [f'Delete "$INSTDIR\\{relative}"']
        uninstall += [f'Delete "$INSTDIR\\{relative}"', f'IfFileExists "$INSTDIR\\{relative}" 0 +2', "StrCpy $2 1"]
    # 清单来自编译产物的实际文件，不由卸载程序遍历并猜测哪些文件属于自己。
    for file in sorted(path for path in payload.rglob("*") if path.is_file()):
        relative = file.relative_to(payload)
        install += [f'SetOutPath "$INSTDIR\\{relative.parent}"', f'File "{file}"']
        uninstall += [f'Delete "$INSTDIR\\{relative}"', f'IfFileExists "$INSTDIR\\{relative}" 0 +2', "StrCpy $2 1"]
    uninstall += [f'RMDir "$INSTDIR\\{folder}"' for folder in reversed(folders)]
    for name, lines in [("install.nsh", install), ("uninstall.nsh", uninstall)]:
        (build / name).write_text("\n".join(lines) + "\n", encoding="utf-8-sig")
    command = [
        str(nsis),
        "/INPUTCHARSET",
        "UTF8",
        "/V2",
        f"/DAPP_VERSION={version}",
        f"/DLICENSE_FILE={build / 'license.txt'}",
        f"/DOUTPUT_FILE={output}",
        f"/DINSTALL_FILES={build / 'install.nsh'}",
        f"/DUNINSTALL_FILES={build / 'uninstall.nsh'}",
    ]
    if args.qa:
        command += ["/DQA_BUILD"]
    command += [str(ROOT / "desktop/installer/ClassGlow.nsi")]
    subprocess.run(command, check=True, cwd=ROOT / "desktop/installer")
    with output.open("rb") as package:
        checksum = hashlib.file_digest(package, "sha256").hexdigest()
    output.with_suffix(".json").write_text(
        json.dumps(
            {
                "version": version,
                "file": output.name,
                "bytes": output.stat().st_size,
                "sha256": checksum,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    print(output)


if __name__ == "__main__":
    main()
