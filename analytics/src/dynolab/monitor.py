"""The run monitor: platform code that watches a run stop by stop and stops it as soon as the data
says the result cannot be used. It checks the run against the procedure that was ordered, not the
one the bench says it loaded. Settings come from config/platform.json ("monitor")."""

from __future__ import annotations

import statistics
from dataclasses import dataclass


@dataclass
class Verdict:
    stop_at: int | None  # index of the stop after which the run is stopped; None: the run completes
    check: str | None
    reason: str | None


def expected_stops(procedure: dict) -> list[dict]:
    out = []
    for s in procedure["sections"]:
        for j in range(s["stops"]):
            out.append({"section": s["name"], "speedKmh": s["speedKmh"], "pressureBar": s["pressureBar"], "initialTempC": s["initialTempC"], "j": j})
    return out


def check_run(stops: list[dict], procedure: dict, cfg: dict, enabled: set[str] | None = None) -> Verdict:
    """Replays the stops as they arrive and returns the first stop at which a check fails."""
    on = enabled if enabled is not None else {"procedure", "temperature", "cooling", "muJump", "characteristic"}
    exp = expected_stops(procedure)
    tol = cfg["procedureTolerance"]
    cooling: dict[str, list[float]] = {}
    recent: list[float] = []
    char_means: dict[str, list[float]] = {}
    seen_fade = False
    for k, s in enumerate(stops):
        if k >= len(exp):
            if "procedure" in on:
                return Verdict(k, "procedure", f"stop {k} is beyond the {len(exp)} stops the procedure has")
            break
        e = exp[k]
        if "procedure" in on and (s["section"] != e["section"] or abs(s["pressureBar"] - e["pressureBar"]) > tol["pressureBar"]
                                  or abs(s["speedKmh"] - e["speedKmh"]) > tol["speedKmh"]):
            return Verdict(k, "procedure", f"stop {k}: {s['pressureBar']} bar at {s['speedKmh']} km/h, procedure says {e['pressureBar']} bar at {e['speedKmh']} km/h")
        is_fade = e["section"].startswith("fade")
        seen_fade = seen_fade or is_fade
        burnish = e["section"] == "burnish"
        if "temperature" in on and not is_fade and abs(s["initialTempC"] - e["initialTempC"]) > cfg["initialTempToleranceC"]:
            return Verdict(k, "temperature", f"stop {k}: initial temperature {s['initialTempC']} °C, procedure says {e['initialTempC']} °C")
        if "cooling" in on and not is_fade:
            key = f"{e['speedKmh']}"
            base = cooling.setdefault(key, [])
            if len(base) >= cfg["coolingBaselineStops"] and s["coolingSeconds"] < (1 - cfg["coolingDropShare"]) * statistics.median(base):
                return Verdict(k, "cooling", f"stop {k}: cooled in {s['coolingSeconds']} s, {statistics.median(base):.0f} s before; is the thermocouple attached?")
            base.append(s["coolingSeconds"])
        if "muJump" in on and not is_fade and len(recent) >= 5 and abs(s["mu"] - statistics.median(recent[-5:])) > cfg["muJump"]:
            return Verdict(k, "muJump", f"stop {k}: µ {s['mu']:.3f} against {statistics.median(recent[-5:]):.3f} just before")
        if not is_fade:
            recent.append(s["mu"])
        else:
            recent.clear()
        if burnish and cfg["afterBurnishOnly"]:
            continue
        if e["section"].startswith("characteristic"):
            char_means.setdefault(e["section"], []).append(s["mu"])
            first = char_means.get("characteristic-1")
            last_of_section = k + 1 >= len(exp) or exp[k + 1]["section"] != e["section"]
            if "characteristic" in on and last_of_section and e["section"] != "characteristic-1" and first:
                shift = statistics.fmean(char_means[e["section"]]) - statistics.fmean(first)
                # Before the first fade a pad's µ holds still; after it, real pads may settle a few hundredths.
                limit = cfg["characteristicShiftAfterFade"] if seen_fade else cfg["characteristicShift"]
                if abs(shift) > limit:
                    return Verdict(k, "characteristic", f"{e['section']} µ differs from characteristic-1 by {shift:+.3f}; check the torque transducer")
    return Verdict(None, None, None)
