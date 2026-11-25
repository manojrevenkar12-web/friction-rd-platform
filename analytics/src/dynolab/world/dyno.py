"""A run on an inertia dynamometer: the stops it records, and the truth behind them. Faults are
part of the world: a loose thermocouple, a drifting torque transducer, the wrong procedure loaded,
the cooling air failing, a cracked pad. The recorded stops show their traces; only the world knows."""

from __future__ import annotations

from dataclasses import dataclass, field

from dynolab.rng import Rng
from dynolab.world.catalog import Catalog

FAULTS = ("thermocoupleLoose", "torqueDrift", "wrongProcedureLoaded", "coolingAirFailure", "sampleCracked")


@dataclass
class Run:
    order_procedure: str
    executed_procedure: str
    formulation: str
    application: str
    stops: list[dict]
    true_mu: list[float]
    fault: str | None
    onset: int | None
    hours: float
    meta: dict = field(default_factory=dict)

    @property
    def valid(self) -> bool:
        return self.fault is None


def draw_fault(seed: int, request_id: str, attempt: int, cfg: dict) -> str | None:
    r = Rng(seed, "fault", request_id, attempt)
    return r.weighted(cfg["faults"]["mix"]) if r.random() < cfg["faults"]["shareOfRuns"] else None


def wrong_procedure(cat: Catalog, pid: str) -> str:
    """What gets loaded by mistake: the sibling variant, or the base procedure with the next pressure table."""
    return {"AKM": "AKM-OEMB", "AKM-OEMB": "AKM"}.get(pid, pid + "*")


def simulate_run(seed: int, cat: Catalog, cfg: dict, request_id: str, attempt: int, formulation: str, application: str, procedure: str) -> Run:
    r = Rng(seed, "run", request_id, attempt)
    rng = r.numpy()
    fault = draw_fault(seed, request_id, attempt, cfg)
    executed = wrong_procedure(cat, procedure) if fault == "wrongProcedureLoaded" else procedure
    proc = cat.procedure(executed.rstrip("*"))
    sections = [dict(s) for s in proc["sections"]]
    if executed.endswith("*"):
        for s in sections:
            s["pressureBar"] += 10
    truth = cat.truth[(formulation, application)]
    phys = cfg["physics"]
    n_total = sum(s["stops"] for s in sections)
    onset = None
    if fault is not None:
        onset = 0 if fault == "wrongProcedureLoaded" else int(rng.integers(n_total // 5, n_total - 5))
    stops: list[dict] = []
    true_mu: list[float] = []
    i = 0
    burnish_n = sum(s["stops"] for s in sections if s["name"] == "burnish")
    seen_fade = False
    for s in sections:
        is_fade = s["name"].startswith("fade")
        seen_fade = seen_fade or is_fade
        for j in range(s["stops"]):
            t_init = s["initialTempC"] + (j * 32.0 if is_fade else 0.0)
            mu = truth.mu0
            if s["name"] == "burnish":
                mu -= truth.green * (1 - i / max(burnish_n, 1))
            mu -= truth.speed_slope * (s["speedKmh"] - 80)
            mu -= 0.0004 * (s["pressureBar"] - 40)
            if t_init > truth.fade_onset:
                mu -= truth.fade_depth * min(1.0, (t_init - truth.fade_onset) / 200)
            if seen_fade and not is_fade:
                mu += truth.material_shift
            mu += float(rng.normal(0, phys["muNoise"]))
            measured_mu = mu
            measured_t = t_init + float(rng.normal(0, 3))
            temp_rise = 0.004 * s["speedKmh"] ** 2
            cooling = 30.0 if is_fade else (30 + 3.0 * temp_rise) * (1 + float(rng.normal(0, 0.08)))
            active = onset is not None and i >= onset
            if active and fault == "thermocoupleLoose" and not is_fade:
                cooling *= 0.35
            elif active and fault == "torqueDrift":
                measured_mu = mu * (1 + 0.0015 * (i - (onset or 0)))
            elif active and fault == "coolingAirFailure" and not is_fade:
                measured_t = s["initialTempC"] + 40 + float(rng.uniform(0, 40))
                cooling = 600.0
            elif active and fault == "sampleCracked":
                measured_mu = mu * 0.65 + float(rng.normal(0, 3 * phys["muNoise"]))
            true_mu.append(mu)
            stops.append({"index": i, "section": s["name"], "speedKmh": s["speedKmh"], "pressureBar": s["pressureBar"],
                          "initialTempC": round(measured_t, 1), "coolingSeconds": round(cooling, 1), "mu": round(measured_mu, 5)})
            i += 1
    return Run(procedure, executed, formulation, application, stops, true_mu, fault, onset, float(cat.procedure(procedure)["hours"]))
