from __future__ import annotations

import json
from collections import Counter

from fastapi.testclient import TestClient

from dynolab.monitor import check_run
from dynolab.paths import ROOT
from dynolab.service import app
from dynolab.world.dyno import simulate_run

MON = json.loads((ROOT / "config" / "platform.json").read_text())["monitor"]


def outcomes(cat, cfg, requests_, n=900):
    c: Counter = Counter()
    for q in requests_[:n]:
        run = simulate_run(5, cat, cfg, q.id, 0, q.formulation, q.application, q.procedure)
        v = check_run(run.stops, cat.procedure(q.procedure), MON)
        c[(run.fault, v.check is not None)] += 1
    return c


def test_monitor_catches_most_faults_and_stops_few_healthy_runs(cat, cfg, requests_):
    c = outcomes(cat, cfg, requests_)
    healthy = c[(None, False)] + c[(None, True)]
    assert c[(None, True)] / healthy < 0.02
    assert c[("wrongProcedureLoaded", False)] == 0
    caught = sum(v for (f, hit), v in c.items() if f and hit)
    faults = sum(v for (f, _), v in c.items() if f)
    assert caught / faults > 0.7


def test_wrong_procedure_is_stopped_at_the_first_stop(cat):
    proc = cat.procedure("AKM")
    other = cat.procedure("AKM-OEMB")
    stops = [{"index": 0, "section": "burnish", "speedKmh": 80, "pressureBar": other["sections"][0]["pressureBar"], "initialTempC": 100,
              "coolingSeconds": 100, "mu": 0.4}]
    v = check_run(stops, proc, MON)
    assert v.stop_at == 0 and v.check == "procedure"


def test_service_checks_a_run(cat, cfg, requests_):
    q = next(q for q in requests_ if q.procedure == "AKM")
    run = simulate_run(5, cat, cfg, q.id, 0, q.formulation, q.application, q.procedure)
    client = TestClient(app)
    res = client.post("/v1/runs/check", json={"procedure": cat.procedure("AKM"), "stops": run.stops})
    assert res.status_code == 200
    body = res.json()
    assert body["stopsChecked"] == len(run.stops)
    assert body["usable"] == (check_run(run.stops, cat.procedure("AKM"), MON).stop_at is None)
    assert client.post("/v1/runs/check", json={"procedure": {"id": "x", "sections": []}, "stops": "nope"}).status_code == 422
