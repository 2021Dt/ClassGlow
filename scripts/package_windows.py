"""生成当前用户安装包及逐文件卸载清单；验证包与正式包的路径和登记完全隔离。"""

import argparse
import os
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--qa", action="store_true", help="构建独立卸载验证包，不使用正式应用目录或启动项")
    parser.add_argument("--nsis", type=Path)
    args = parser.parse_args()
    sdk = ROOT / ".tools/dotnet/dotnet.exe"
    dotnet = str(sdk) if sdk.exists() else shutil.which("dotnet") or "dotnet"
    nsis = args.nsis or Path(os.environ["LOCALAPPDATA"]) / "tauri/NSIS/makensis.exe"
    if not nsis.is_file():
        raise SystemExit("请安装 NSIS 3，并通过 --nsis 指定 makensis.exe；脚本不会安装全局工具。")
    payload = ROOT / "dist" / ("windows-qa" if args.qa else "windows-0.4.1")
    output = ROOT / "dist" / ("ClassGlow-0.4.1-uninstall-QA.exe" if args.qa else "ClassGlow-0.4.1-win-x64-setup.exe")
    build = ROOT / ".qa" / ("installer-qa" if args.qa else "installer")
    build.mkdir(parents=True, exist_ok=True)
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
    shutil.copyfile(ROOT / "desktop/installer/privacy.txt", payload / "隐私与卸载说明.txt")
    folders = sorted(
        (path.relative_to(payload) for path in payload.rglob("*") if path.is_dir()), key=lambda p: len(p.parts)
    )
    install = [f'!insertmacro CheckDirectory "$INSTDIR\\{folder}"' for folder in folders]
    uninstall = install.copy()
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
        f"/DOUTPUT_FILE={output}",
        f"/DINSTALL_FILES={build / 'install.nsh'}",
        f"/DUNINSTALL_FILES={build / 'uninstall.nsh'}",
    ]
    if args.qa:
        command += ["/DQA_BUILD"]
    command += [str(ROOT / "desktop/installer/ClassGlow.nsi")]
    subprocess.run(command, check=True, cwd=ROOT / "desktop/installer")
    print(output)


if __name__ == "__main__":
    main()
