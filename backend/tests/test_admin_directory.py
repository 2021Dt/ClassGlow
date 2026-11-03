"""管理后台的筛选必须覆盖全库，而不是只筛选当前页。"""

from uuid import uuid4

from sqlalchemy.orm import Session

from backend.db import FeatureLicense, User, now
from backend.tests.test_access_licenses import root_account
from backend.tests.test_workflows import user


def test_admin_filters_pagination_and_license_history(client):
    c, engine = client
    root = root_account(c, engine)
    teacher, _ = user(c, "ordinary_teacher")
    timestamp = now()
    with Session(engine) as db:
        # 24 个正常教师和 3 种异常账号，覆盖第一页之外的筛选结果与总数。
        accounts = [
            User(login=f"filter_{i:02}", name=f"老师{i}", password="unused", can_create_classes=False)
            for i in range(27)
        ]
        accounts[0].expires_at = timestamp + 2 * 86400
        accounts[0].premium_until = timestamp + 3 * 86400
        accounts[24].expires_at = timestamp - 86400
        accounts[25].active = False
        accounts[26].activated = False
        db.add_all(accounts)
        db.flush()
        target_id = accounts[0].id
        for i in range(25):
            db.add(
                FeatureLicense(
                    user_id=accounts[0 if i < 24 else 1].id,
                    code_hash=uuid4().hex,
                    epoch="test-epoch",
                    days=30,
                    expires=timestamp + 86400,
                    revoked=i == 22,
                    used_at=timestamp if i == 23 else 0,
                )
            )
        db.commit()

    first = c.get("/system/users?search=filter_&identity=teacher&status=active&limit=20", headers=root).json()
    second = c.get(
        "/system/users?search=filter_&identity=teacher&status=active&limit=20&offset=20", headers=root
    ).json()
    assert first["total"] == second["total"] == 24
    assert len(first["items"]) == 20 and len(second["items"]) == 4
    assert not {u["id"] for u in first["items"]} & {u["id"] for u in second["items"]}
    for status in ("disabled", "expired", "pending", "expiring"):
        assert c.get(f"/system/users?search=filter_&status={status}", headers=root).json()["total"] == 1
    assert c.get("/system/users?search=filter_&license=expiring", headers=root).json()["total"] == 1
    assert c.get("/system/overview", headers=root).json()["expiringAccounts"] == 1
    global_list = c.get("/system/licenses?status=pending&offset=20", headers=root).json()
    assert global_list["total"] == 23 and len(global_list["items"]) == 3
    assert all(entry["login"].startswith("filter_") for entry in global_list["items"])
    assert c.get(f"/system/licenses?userId={target_id}&status=pending", headers=root).json()["total"] == 22
    for status in ("used", "revoked"):
        assert c.get(f"/system/licenses?status={status}", headers=root).json()["total"] == 1
    assert c.get("/system/users?search=missing", headers=root).json() == {"items": [], "total": 0}
    assert c.get("/system/users", headers=teacher).status_code == 403
    assert c.get("/system/licenses", headers=teacher).status_code == 403
