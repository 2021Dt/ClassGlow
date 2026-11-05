import base64
import hashlib
from io import BytesIO

from PIL import Image
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from backend.db import ClassAsset, Classroom
from backend.tests.test_workflows import classroom, paired, user


def picture():
    output = BytesIO()
    Image.new("RGB", (32, 32), "blue").save(output, "JPEG")
    return "data:image/jpeg;base64," + base64.b64encode(output.getvalue()).decode(), output.getvalue()


def test_asset_references_compatibility_authorization_deduplication_and_pruning(client):
    c, engine = client
    teacher, _ = user(c)
    cid = classroom(c, teacher)
    _, device, _ = paired(c, teacher, cid)
    layout = c.get("/bootstrap", headers=teacher).json()["classes"][0]["layout"]
    image, raw = picture()
    layout.update(backgroundMode="image", backgroundImage=image, panelImage=image)
    saved = c.put(f"/classes/{cid}/layout", headers=teacher, json=layout)
    assert saved.status_code == 200
    identity = hashlib.sha256(raw).hexdigest()
    compact = {**teacher, "X-ClassGlow-Assets": "1"}
    modern = c.get("/bootstrap", headers=compact).json()["classes"][0]["layout"]
    assert modern["backgroundImage"] == "asset:" + identity
    assert c.get("/bootstrap", headers=teacher).json()["classes"][0]["layout"]["backgroundImage"] == image
    endpoint = f"/classes/{cid}/assets/{identity}"
    assert c.get(endpoint, headers=teacher).content == raw
    assert c.get(endpoint).status_code == 401
    stranger, _ = user(c, "asset_stranger")
    assert c.get(endpoint, headers=stranger).status_code == 403
    other_class = classroom(c, stranger)
    _, other_device, _ = paired(c, stranger, other_class)
    assert c.get(f"/device/assets/{identity}", headers=other_device).status_code == 404
    assert c.get(f"/device/assets/{identity}", headers=device).content == raw
    old = c.get("/device/snapshot", headers=device)
    assert old.json()["classroom"]["layout"]["backgroundImage"] == image
    upgraded = c.get(
        "/device/snapshot", headers={**device, "X-ClassGlow-Assets": "1", "If-None-Match": old.headers["ETag"]}
    )
    assert upgraded.status_code == 200
    assert upgraded.json()["classroom"]["layout"]["backgroundImage"] == "asset:" + identity
    assert (
        c.get(f"/classes/{cid}/export", headers=teacher).json()["classroom"]["config"]["layout"]["backgroundImage"]
        == image
    )
    with Session(engine) as db:
        assert db.scalar(select(func.count()).select_from(ClassAsset)) == 1
        assert "data:image" not in str(db.get(Classroom, cid).data)
    plain = saved.json()
    plain.update(backgroundMode="color", backgroundImage="", panelImage="")
    assert c.put(f"/classes/{cid}/layout", headers=teacher, json=plain).status_code == 200
    assert c.get(endpoint, headers=teacher).status_code == 404
