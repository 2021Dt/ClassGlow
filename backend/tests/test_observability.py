import logging

from sqlalchemy.exc import OperationalError

from backend.db import get_db
from backend.observability import Metrics
from backend.tests.test_access_licenses import root_account
from backend.tests.test_workflows import user


def test_metrics_are_bounded_and_report_errors_and_percentiles():
    metrics = Metrics()
    for i in range(200):
        metrics.observe("GET", "/bootstrap", 503 if i == 0 else 200, i)
    result = metrics.snapshot()
    assert result["requests"] == 200 and result["errors"] == 1
    assert result["routes"][0]["p95Ms"] == 193
    assert len(metrics.routes[("GET", "/bootstrap")]["recent"]) == 128


def test_diagnostics_require_admin_and_logs_use_templates_without_secrets(client, caplog):
    c, engine = client
    teacher, _ = user(c)
    assert c.get("/system/metrics", headers=teacher).status_code == 403
    root = root_account(c, engine)
    assert c.get("/system/metrics", headers=root).status_code == 200
    with caplog.at_level(logging.INFO, logger="uvicorn.error"):
        response = c.get("/drafts/private-draft-secret?private=secret-query", headers=teacher)
    assert response.status_code == 404 and response.headers["X-Request-ID"]
    log = "\n".join(item.message for item in caplog.records)
    assert '"route": "/drafts/{draft_id}"' in log
    assert "private-draft-secret" not in log and "secret-query" not in log
    assert teacher["Authorization"] not in log


def test_database_failure_has_trace_id_and_does_not_log_sql_or_parameters(client, caplog):
    c, _ = client

    def broken_database():
        raise OperationalError("SELECT sensitive-body", {"password": "private-password"}, RuntimeError("private-input"))

    c.app.dependency_overrides[get_db] = broken_database
    with caplog.at_level(logging.INFO, logger="uvicorn.error"):
        response = c.get("/health")
    assert response.status_code == 503
    assert response.json()["detail"]["request_id"] == response.headers["X-Request-ID"]
    output = response.text + "\n".join(item.message for item in caplog.records)
    assert all(secret not in output for secret in ("sensitive-body", "private-password", "private-input"))
