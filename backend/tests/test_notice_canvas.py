import base64
import json
from copy import deepcopy
from io import BytesIO
from uuid import uuid4

import pytest
from PIL import Image
from pydantic import ValidationError
from sqlalchemy.orm import Session
from test_workflows import classroom, paired, user

from backend.db import Command, Device, Idempotency, now
from backend.schemas import NoticeCanvas, NoticeElement, ScreenNotice
from backend.storage import clean_records


def design():
    output = BytesIO()
    Image.new("RGB", (128, 64), "#429978").save(output, "JPEG")
    image = "data:image/jpeg;base64," + base64.b64encode(output.getvalue()).decode()
    return NoticeCanvas(
        elements=[
            NoticeElement(id="card", kind="card", x=200, y=200, w=1200, h=500, text="课前准备"),
            NoticeElement(id="picture", kind="image", x=300, y=300, w=200, h=200, image=image),
            NoticeElement(id="clock", kind="countdown", x=600, y=450, w=400, h=200),
        ]
    ).model_dump()


def test_canvas_validation_rejects_invalid_or_oversized_payloads():
    good = design()
    for patch in (
        {"x": 1500},
        {"w": 0},
        {"text": "字" * 241},
        {"fontSize": 161},
        {"fillColor": "red"},
        {"image": "https://example.com/image.jpg"},
        {"kind": "html"},
    ):
        bad = deepcopy(good)
        bad["elements"][0].update(patch)
        with pytest.raises(ValidationError):
            NoticeCanvas.model_validate(bad)
    bad = deepcopy(good)
    bad["elements"][1]["image"] = "data:image/jpeg;base64,YmFk"
    with pytest.raises(ValidationError):
        NoticeCanvas.model_validate(bad)
    for elements in (
        [],
        good["elements"] * 5,
        [good["elements"][0]] * 2,
        [{**e, "hidden": True} for e in good["elements"]],
    ):
        with pytest.raises(ValidationError):
            NoticeCanvas.model_validate({**good, "elements": elements})
    output = BytesIO()
    Image.effect_noise((640, 480), 80).convert("RGB").save(output, "JPEG", quality=55)
    image = "data:image/jpeg;base64," + base64.b64encode(output.getvalue()).decode()
    assert 524288 / 12 < len(image) < 240000
    with pytest.raises(ValidationError, match="512 KB"):
        NoticeCanvas.model_validate(
            {**good, "elements": [{**good["elements"][1], "id": str(i), "image": image} for i in range(12)]}
        )
    for notice in (
        {"mode": "canvas"},
        {"mode": "popup", "canvas": good},
        {"mode": "canvas", "canvas": good, "style": {}},
    ):
        with pytest.raises(ValidationError):
            ScreenNotice.model_validate({"message": "测试", **notice})


def test_canvas_delivery_idempotency_history_and_storage_retention(client):
    c, engine = client
    headers, uid = user(c, "canvas_notice")
    cid = classroom(c, headers)
    did, device_headers, _ = paired(c, headers, cid)
    canvas = design()
    output = BytesIO()
    Image.effect_noise((640, 480), 80).convert("RGB").save(output, "JPEG", quality=55)
    canvas["elements"][1]["image"] = "data:image/jpeg;base64," + base64.b64encode(output.getvalue()).decode()
    assert 131072 < len(json.dumps(canvas).encode()) < 524288
    notice = {"mode": "canvas", "message": "自由画布提醒", "duration": 60, "canvas": canvas}

    def send(revision=0, key=None):
        return c.post(
            f"/devices/{did}/commands",
            headers={**headers, "Idempotency-Key": key or str(uuid4())},
            json={"action": "notice", "expectedControlRevision": revision, "notice": notice},
        )

    with Session(engine) as db:
        db.get(Device, did).state = {"clientVersion": "0.4.13"}
        db.commit()
    assert send().status_code == 409
    with Session(engine) as db:
        db.get(Device, did).state = {"clientVersion": "0.4.14"}
        db.commit()
    key = str(uuid4())
    sent = send(key=key)
    assert sent.status_code == 202, sent.text
    command_id = sent.json()["id"]
    assert "canvas" not in sent.json()["notice"]
    snapshot = c.get("/device/snapshot", headers=device_headers).json()
    delivered = next(v for v in snapshot["commands"] if v["id"] == command_id)
    assert delivered["notice"]["canvas"] == canvas
    assert send(key=key).json() == sent.json()
    assert "data:image" not in c.get(f"/devices/{did}/control", headers=headers).text
    newer = send(1).json()["id"]
    with Session(engine) as db:
        assert "canvas" not in db.get(Idempotency, f"{uid}:control:{did}:{key}").result["notice"]
        db.get(Command, command_id).expires = now() - 86401
        db.flush()
        assert clean_records(db)["过期提醒画布"] == 1
        db.commit()
        assert "canvas" not in db.get(Command, command_id).data["notice"]
        assert db.get(Command, newer).data["notice"]["canvas"] == canvas
        assert clean_records(db)["过期提醒画布"] == 0


def test_notice_body_limit_also_bounds_streaming_uploads(client):
    c, _ = client
    response = c.post(
        "/devices/unknown/commands",
        content=iter([b" " * 300001, b" " * 300001]),
        headers={"Content-Type": "application/json"},
    )
    assert response.status_code == 413
    # 普通业务接口没有因画布支持而放宽大小限制。
    assert c.post("/classes", content=b" " * 131073).status_code == 413
