import base64
import json
import random
import sqlite3
from io import BytesIO
from pathlib import Path

from PIL import Image

from backend.tests.test_workflows import classroom, paired, user
from scripts.sqlite_backup import backup


def test_background_publishes_in_snapshot_and_survives_backup(client, tmp_path):
    c, engine = client
    headers, _ = user(c)
    class_id = classroom(c, headers)
    _, device_headers, _ = paired(c, headers, class_id)
    layout = c.get("/bootstrap", headers=headers).json()["classes"][0]["layout"]
    image = Image.frombytes("RGB", (640, 360), random.Random(19).randbytes(640 * 360 * 3))
    stream = BytesIO()
    image.save(stream, "JPEG", quality=80)
    picture = "data:image/jpeg;base64," + base64.b64encode(stream.getvalue()).decode()
    assert len(picture) > 131072  # 图片发布必须穿过真实请求体中间件，而不只是模型验证。
    layout.update(backgroundMode="image", backgroundImage=picture, surfaceOpacity=38)
    layout["grid"] = {
        "timetable": {"x": 0, "y": 0, "w": 5, "h": 12},
        "todo": {"x": 5, "y": 0, "w": 7, "h": 4},
        "homework": {"x": 5, "y": 4, "w": 7, "h": 8},
    }
    response = c.put(f"/classes/{class_id}/layout", headers=headers, json=layout)
    assert response.status_code == 200, response.text
    snapshot = c.get("/device/snapshot", headers=device_headers).json()
    assert snapshot["classroom"]["layout"]["backgroundImage"] == picture
    assert snapshot["classroom"]["layout"]["surfaceOpacity"] == 38
    assert snapshot["classroom"]["layout"]["grid"] == layout["grid"]
    saved = backup(Path(engine.url.database), tmp_path / "background-backup.db")
    with sqlite3.connect(saved) as database:
        data = json.loads(database.execute("SELECT data FROM classrooms WHERE id=?", (class_id,)).fetchone()[0])
    assert data["layout"]["backgroundImage"] == picture
    invalid = {**response.json(), "backgroundImage": "data:image/jpeg;base64,bm90LWFuLWltYWdl"}
    assert c.put(f"/classes/{class_id}/layout", headers=headers, json=invalid).status_code == 422
