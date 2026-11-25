from __future__ import annotations

import json

import pytest

from dynolab import cli
from dynolab.lab import report as rep
from dynolab.lab.simmodel import SimulatedIntakeModel
from dynolab.lab.study import load_designs
from dynolab.paths import ROOT


def test_ablations_change_one_thing():
    ds = {d.id: d for d in load_designs()}
    p = vars(ds["platform"])
    for d in ds.values():
        if d.kind == "ablation":
            diff = [k for k in p if k not in ("id", "label", "kind") and p[k] != vars(d)[k]]
            assert len(diff) == 1, (d.id, diff)


def test_simulated_model_has_the_same_luck_for_every_design(cat, cfg, requests_):
    a = SimulatedIntakeModel(5, cat, cfg["llm"])
    b = SimulatedIntakeModel(5, cat, cfg["llm"])
    assert [a.draft(q) for q in requests_[:40]] == [b.draft(q) for q in reversed(requests_[:40])][::-1]


def test_drafts_quote_the_request(cat, cfg, requests_):
    m = SimulatedIntakeModel(5, cat, cfg["llm"])
    quoted = [m.draft(q)["formulation"]["evidence"] in q.text for q in requests_[:200]]
    assert sum(quoted) / len(quoted) > 0.95


def test_heldout_runs_once(tmp_path, monkeypatch):
    (tmp_path / "reports").mkdir()
    (tmp_path / "reports" / "heldout.lock").write_text("config x seeds 1\n")
    monkeypatch.setattr(cli, "ROOT", tmp_path)
    with pytest.raises(SystemExit) as e:
        cli.main(["lab", "heldout"])
    assert e.value.code == 3


def test_gates():
    g = [{"id": "a", "metric": "x", "max": 5, "label": "x"}, {"id": "b", "metric": "y", "maxShareOfStatusQuo": 0.5, "label": "y"}]
    assert [x["pass"] for x in rep.check_gates(g, {"x": 4, "y": 10}, {"y": 30})] == [True, True]
    assert [x["pass"] for x in rep.check_gates(g, {"x": 6, "y": 20}, {"y": 30})] == [False, False]


@pytest.mark.skipif(not (ROOT / "reports" / "dev.json").exists(), reason="no dev report yet")
def test_readme_results_are_generated():
    dev = json.loads((ROOT / "reports" / "dev.json").read_text())
    block = rep.results_block(dev, rep.read("heldout"), rep.read("bench"))
    assert block in (ROOT / "README.md").read_text(), "README results are stale: run `dynolab lab report`"
