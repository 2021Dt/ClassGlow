"""发布元数据与 Windows、前端共同来自根目录 release.json。"""

import json
import re
from pathlib import Path
from urllib.parse import urlsplit

RELEASE = json.loads((Path(__file__).resolve().parents[1] / "release.json").read_text(encoding="utf-8"))
VERSION = RELEASE["version"]
ASSET_PROTOCOL = str(RELEASE["assetProtocol"])


def downloadable_version(configured_version, download_url):
    # 服务可以先更新；仍在分发旧安装包时，下载页必须如实显示包的版本。
    match = re.search(r"/ClassGlow-(\d+\.\d+\.\d+)-win-x64-setup\.exe$", urlsplit(download_url).path)
    return match[1] if match else configured_version
