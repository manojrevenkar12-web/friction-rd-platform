from __future__ import annotations

import ast
from pathlib import Path

from dynolab.rng import Rng
from dynolab.world.catalog import build_catalog
from dynolab.world.dyno import simulate_run
from dynolab.world.requests import make_requests

WORLD = Path(__file__).parents[1] / "src" / "dynolab" / "world"


def test_world_does_not_read_platform_settings():
    for f in WORLD.glob("*.py"):
        src = f.read_text()
        assert "platform.json" not in src and "designs.json" not in src and "gates.json" not in src, f.name
        for node in ast.walk(ast.parse(src)):
            if isinstance(node, ast.ImportFrom) and node.module:
                assert not node.module.startswith(("dynolab.lab", "dynolab.monitor", "dynolab.service")), f"{f.name} imports {node.module}"


def test_keyed_streams():
    a = Rng(1, "run", "q7").random()
    Rng(1, "run", "q1").random()
    assert Rng(1, "run", "q7").random() == a


def test_world_is_deterministic(cfg):
    a, b = build_catalog(3, cfg), build_catalog(3, cfg)
    assert a.master_data() == b.master_data()
    qa, qb = make_requests(3, "x", a, cfg), make_requests(3, "x", b, cfg)
    assert [q.text for q in qa[:50]] == [q.text for q in qb[:50]]


def test_master_data_hides_the_truth(cat):
    md = cat.master_data()
    assert set(md) == {"benches", "applications", "formulations", "procedures"}
    assert "mu0" not in str(md)


def test_requests_are_in_both_languages_and_carry_their_traps(requests_):
    langs = {q.lang for q in requests_}
    assert langs == {"de", "en"}
    assert any(q.missing_axle for q in requests_)
    assert any(q.procedure == "AKM-OEMB" and "OEM-B" in q.text for q in requests_)
    assert all(q.formulation[3:] in q.text for q in requests_)


def test_runs_follow_their_procedure_and_faults_leave_traces(cat, cfg, requests_):
    q = next(q for q in requests_ if q.procedure == "AKM")
    run = simulate_run(5, cat, cfg, q.id, 0, q.formulation, q.application, q.procedure)
    assert len(run.stops) == sum(s["stops"] for s in cat.procedure("AKM")["sections"])
    again = simulate_run(5, cat, cfg, q.id, 0, q.formulation, q.application, q.procedure)
    assert run.stops == again.stops
    faults = {simulate_run(5, cat, cfg, x.id, 0, x.formulation, x.application, x.procedure).fault for x in requests_[:400]}
    assert {"thermocoupleLoose", "torqueDrift", "wrongProcedureLoaded", "coolingAirFailure", "sampleCracked"} <= faults
