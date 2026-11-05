import base64
import json
import random
import sqlite3
from io import BytesIO
from pathlib import Path

from PIL import Image
from sqlalchemy import create_engine
from sqlalchemy.orm import Session

from backend.assets import materialize_layout
from backend.tests.test_workflows import classroom, paired, user
from scripts.sqlite_backup import backup


def test_canvas_publish_snapshot_backup_and_bounds(client, tmp_path):
    """一次完整链路覆盖自由画布、大请求体和备份；旧网格继续由已有背景用例覆盖。"""
    c, engine = client
    headers, _ = user(c)
    class_id = classroom(c, headers)
    _, device_headers, _ = paired(c, headers, class_id)
    old_headers = device_headers.copy()
    device_headers["X-ClassGlow-Client-Version"] = "0.4.0"
    layout = c.get("/bootstrap", headers=headers).json()["classes"][0]["layout"]
    image = Image.frombytes("RGB", (640, 360), random.Random(31).randbytes(640 * 360 * 3))
    stream = BytesIO()
    image.save(stream, "JPEG", quality=80)
    picture = "data:image/jpeg;base64," + base64.b64encode(stream.getvalue()).decode()
    # 图片允许重叠，图层次序属于老师的设计；四张图片可超过旧版 640 KiB 上限。
    widgets = [
        {"id": f"photo-{n}", "kind": "image", "x": n * 40, "y": n * 40, "w": 400, "h": 250, "image": picture}
        for n in range(4)
    ]
    widgets += [
        {
            "id": "message",
            "kind": "message",
            "x": 0,
            "y": 0,
            "w": 400,
            "h": 250,
            "dataSource": "custom",
            "text": "每天向前一点",
        },
        {
            "id": "countdown",
            "kind": "countdown",
            "x": 400,
            "y": 0,
            "w": 400,
            "h": 250,
            "dataSource": "custom",
            "event": "读书节",
            "eventDate": "2030-06-01",
        },
        {
            "id": "words",
            "kind": "text",
            "x": 500,
            "y": 400,
            "w": 600,
            "h": 300,
            "text": "让每一种想象都有位置",
            "fontSize": 72,
            "fontFamily": "kai",
            "fontWeight": 600,
            "lineHeight": 1.8,
            "align": "right",
            "backgroundImage": picture,
            "backgroundFit": "contain",
            "backgroundPosition": "bottom",
        },
    ]
    layout.update(
        editor="canvas",
        canvas=widgets,
        panelImage=picture,
        panelImageFit="cover",
        panelImagePosition="top",
        surfaceOpacity=0,
    )
    assert len(json.dumps(layout).encode()) > 640 * 1024
    response = c.put(f"/classes/{class_id}/layout", headers=headers, json=layout)
    assert response.status_code == 200, response.text
    saved_layout = response.json()
    assert saved_layout["canvas"][-1]["fontFamily"] == "kai"
    assert saved_layout["canvas"][-1]["backgroundImage"] == picture
    assert saved_layout["panelImage"] == picture
    assert saved_layout["canvas"][-3]["text"] == "每天向前一点"
    assert saved_layout["canvas"][-2]["dataSource"] == "custom"
    assert saved_layout["canvas"][-2]["eventDate"] == "2030-06-01"
    snapshot = c.get("/device/snapshot", headers=device_headers).json()
    assert snapshot["classroom"]["layout"] == saved_layout
    old = c.get("/device/snapshot", headers=old_headers)
    assert old.json()["classroom"]["layout"].get("editor", "grid") == "grid"
    upgraded = c.get("/device/snapshot", headers={**device_headers, "If-None-Match": old.headers["ETag"]})
    assert upgraded.status_code == 200
    assert upgraded.json()["classroom"]["layout"] == saved_layout
    saved = backup(Path(engine.url.database), tmp_path / "canvas-backup.db")
    with sqlite3.connect(saved) as database:
        stored = json.loads(database.execute("SELECT data FROM classrooms WHERE id=?", (class_id,)).fetchone()[0])
    assert stored["layout"]["panelImage"].startswith("asset:")
    restored = create_engine(f"sqlite:///{saved}")
    try:
        with Session(restored) as db:
            assert materialize_layout(db, class_id, stored["layout"]) == saved_layout
    finally:
        restored.dispose()
    bad = {**saved_layout, "canvas": [{**widgets[-1], "x": 1500}]}
    assert c.put(f"/classes/{class_id}/layout", headers=headers, json=bad).status_code == 422
    bad["canvas"] = [widgets[-1], widgets[-1]]
    assert c.put(f"/classes/{class_id}/layout", headers=headers, json=bad).status_code == 422
    assert c.get("/device/snapshot", headers=device_headers).json()["classroom"]["layout"] == saved_layout
