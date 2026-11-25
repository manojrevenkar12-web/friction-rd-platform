"""Runs every design on the same simulated years and summarises what each one did."""

from __future__ import annotations

import json
import statistics
import time

from dynolab.lab.core import Core
from dynolab.lab.sim import Caches, Design, LabYear
from dynolab.lab.simmodel import SimulatedIntakeModel
from dynolab.paths import ROOT
from dynolab.world.catalog import build_catalog, load_world_config
from dynolab.world.requests import make_requests


def load_json(name: str) -> dict:
    return json.loads((ROOT / "config" / name).read_text())


def load_designs() -> list[Design]:
    by: dict[str, dict] = {}
    for d in load_json("designs.json")["designs"]:
        base = dict(by[d["base"]]) if "base" in d else {}
        by[d["id"]] = {**base, **{k: v for k, v in d.items() if k != "base"}}
    return [Design(**d) for d in by.values()]


def run_seed(seed: int, split: str, designs: list[Design]) -> dict:
    cfg = load_world_config()
    platform = load_json("platform.json")
    cat = build_catalog(seed, cfg)
    requests = make_requests(seed, split, cat, cfg)
    work = ROOT / "artifacts"
    work.mkdir(exist_ok=True)
    master = work / f"master-{split}-{seed}.json"
    master.write_text(json.dumps(cat.master_data()))
    caches = Caches()
    out: dict = {"requests": len(requests), "designs": {}}
    with Core(master) as core:
        year = LabYear(seed, cat, cfg, platform, requests, core, SimulatedIntakeModel(seed, cat, cfg["llm"]), caches)
        for d in designs:
            t = time.perf_counter()
            rows = year.simulate(d)
            out["designs"][d.id] = summarise(rows, d, cfg, platform)
            print(f"  {split} {seed} {d.id}: {time.perf_counter() - t:.1f} s", flush=True)
    return out


def pct(xs: list[float], p: float) -> float:
    if not xs:
        return float("nan")
    xs = sorted(xs)
    k = (len(xs) - 1) * p
    lo, hi = int(k), min(int(k) + 1, len(xs) - 1)
    return xs[lo] + (xs[hi] - xs[lo]) * (k - lo)


def summarise(rows: list[dict], d: Design, cfg: dict, platform: dict) -> dict:
    released = [r for r in rows if r["released"] is not None]
    lead = [(r["released"] - r["arrival"]) / 24 for r in released]
    with_deadline = [r for r in rows if r["deadline"] is not None]
    homolog = [r for r in with_deadline if r["kind"] == "homologation"]

    def missed(r: dict) -> bool:
        return r["released"] is None or r["released"] > r["deadline"]

    runs = sum(r["runs"] for r in rows)
    weeks = cfg["workingDays"] / 5
    agent = d.intake in ("agent", "model")
    llm = cfg["llm"]
    svc = platform["service"]
    calls = sum(r["modelCalls"] for r in rows) / len(rows)
    cost = (llm["inputTokens"] * svc["inputEurPerMillionTokens"] + llm["outputTokens"] * svc["outputEurPerMillionTokens"]) / 1e6 * 1000 * calls if agent else 0.0
    evaluated = [r for r in released if r["parity"] is not None]
    return {
        "requests": len(rows),
        "released": len(released),
        "leadTimeMedianDays": statistics.median(lead) if lead else float("nan"),
        "leadTimeP90Days": pct(lead, 0.9),
        "deadlineMissRate": sum(missed(r) for r in with_deadline) / len(with_deadline),
        "homologationMissRate": sum(missed(r) for r in homolog) / len(homolog),
        "wrongResultsPer1000": 1000 * sum(not r["resultOk"] for r in released) / len(released),
        "faultyResultsPer1000": 1000 * sum(r["faultsReleased"] > 0 for r in released) / len(released),
        "verdictErrorsPer1000": 1000 * sum(r["verdictWrong"] for r in released) / len(released),
        "wrongOrdersPer1000": 1000 * sum(r["orderCorrect"] is False for r in rows) / len(rows),
        "wrongOrderRunsPer1000": 1000 * sum(r["wrongOrderRun"] for r in rows) / len(rows),
        "clarificationShare": sum(r["intake"] == "clarified" for r in rows) / len(rows),
        "coordinatorHoursPerWeek": sum(r["minutes"] for r in rows) / 60 / weeks,
        "lostBenchHoursPerWeek": sum(r["lostHours"] for r in rows) / weeks,
        "changeoverHoursPerWeek": sum(r["changeoverHours"] for r in rows) / weeks,
        "faultsCaughtLive": sum(r["faultsCaughtLive"] for r in rows),
        "faultsCaughtReview": sum(r["faultsCaughtReview"] for r in rows),
        "falseStopsPer1000Runs": 1000 * sum(r["falseStops"] for r in rows) / max(runs, 1),
        "parityDiffersShare": sum(r["parity"] == "differs" for r in evaluated) / len(evaluated) if evaluated else float("nan"),
        "modelCallsPerRequest": calls,
        "modelCostPer1000Eur": cost,
    }
