from backend.tests.test_access_licenses import root_account
from backend.tests.test_workflows import user


def test_announcement_delivery_by_account(client):
    c, engine = client
    admin = root_account(c, engine)
    recipient, recipient_id = user(c, "notice_recipient")
    other, _ = user(c, "notice_other")
    path = "/system/announcements"
    markdown = "## 校园提醒\n\n请携带 **学生手册**。"
    selected = {"title": "班级通知", "markdown": markdown, "scope": "selected", "accountIds": [recipient_id]}

    def publish(body, key):
        response = c.post(path, json=body, headers={**admin, "Idempotency-Key": key})
        assert response.status_code == 201, response.text
        return response.json()

    def next_notice(headers, key=None):
        response = c.post("/announcements/next", headers={**headers, **({"Idempotency-Key": key} if key else {})})
        assert response.status_code == 200, response.text
        assert response.headers["Cache-Control"] == "no-store"
        return response.json()["notice"]

    assert c.post("/announcements/next").status_code == 401
    assert c.post(path, json=selected, headers=recipient).status_code == 403
    assert c.post(path, json={**selected, "accountIds": []}, headers=admin).status_code == 422

    first = publish(selected, "publish-selected")
    assert publish(selected, "publish-selected") == first
    assert next_notice(other) is None
    assert next_notice(recipient, "claim-selected") == first
    assert first["markdown"] == markdown
    assert next_notice(recipient, "claim-selected") == first

    login = c.post("/auth/login", json={"login": "notice_recipient", "password": "CorrectPass!123"})
    assert login.status_code == 200, login.text
    recipient = {"Authorization": f"Bearer {login.json()['token']}"}
    assert next_notice(recipient, "claim-after-login") is None

    everyone = publish({"title": "全校通知", "markdown": markdown, "scope": "all"}, "publish-all")
    assert next_notice(recipient) == everyone
    assert next_notice(other) == everyone

    withdrawn = publish({"title": "撤回示例", "markdown": markdown, "scope": "all"}, "publish-withdraw")
    withdraw_url = f"{path}/{withdrawn['id']}/withdraw"
    assert c.post(withdraw_url, headers=recipient).status_code == 403
    assert c.post(withdraw_url, headers=admin).status_code == 200
    assert next_notice(other) is None
    history = {item["id"]: item for item in c.get(path, headers=admin).json()["items"]}
    assert history[first["id"]]["shownCount"] == history[first["id"]]["targetCount"] == 1
    assert history[everyone["id"]]["shownCount"] == 2
    assert history[withdrawn["id"]]["withdrawnAt"] is not None
