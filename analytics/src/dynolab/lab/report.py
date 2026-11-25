"""Writes reports/*.json, REPORT.md, the README results block and the console's study data."""

from __future__ import annotations

import json
import math
from pathlib import Path

from dynolab.paths import ROOT


def clean(x):
    if isinstance(x, float):
        return None if math.isnan(x) else round(x, 6)
    if isinstance(x, dict):
        return {k: clean(v) for k, v in x.items()}
    if isinstance(x, list):
        return [clean(v) for v in x]
    return x


def write_json(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(clean(data), indent=2, ensure_ascii=False) + "\n")


def read(name: str) -> dict | None:
    p = ROOT / "reports" / f"{name}.json"
    return json.loads(p.read_text()) if p.exists() else None


def mean_metrics(per_seed: list[dict]) -> dict:
    out = {}
    for k in per_seed[0]:
        vals = [m[k] for m in per_seed if m[k] is not None and not (isinstance(m[k], float) and math.isnan(m[k]))]
        out[k] = sum(vals) / len(vals) if vals else None
    return out


def check_gates(gates: list[dict], platform: dict, status_quo: dict) -> list[dict]:
    out = []
    for g in gates:
        v = platform.get(g["metric"])
        ok = v is not None
        limit = ""
        if "max" in g:
            ok = ok and v <= g["max"]
            limit = f"≤ {g['max']}"
        if "maxShareOfStatusQuo" in g:
            sq = status_quo[g["metric"]]
            ok = ok and v <= g["maxShareOfStatusQuo"] * sq
            limit = f"≤ {g['maxShareOfStatusQuo']} × today ({sq:.1f})"
        out.append({"id": g["id"], "label": g["label"], "value": v, "limit": limit, "pass": bool(ok)})
    return out


def pct(x, d=1):
    return "–" if x is None else f"{x * 100:.{d}f}%"


def num(x, d=1):
    return "–" if x is None else f"{x:,.{d}f}"


COLS = [("Median lead time, days", "leadTimeMedianDays", num), ("p90 lead time, days", "leadTimeP90Days", num),
        ("Homologation deadlines missed", "homologationMissRate", pct), ("Wrong results per 1,000", "wrongResultsPer1000", num),
        ("Wrong orders per 1,000", "wrongOrdersPer1000", num), ("Wrong verdicts per 1,000", "verdictErrorsPer1000", num),
        ("Bench hours lost per week", "lostBenchHoursPerWeek", lambda x: num(x, 0)), ("Changeover hours per week", "changeoverHoursPerWeek", lambda x: num(x, 0)),
        ("Coordinator hours per week", "coordinatorHoursPerWeek", num), ("Asked back", "clarificationShare", pct)]


def table(report: dict) -> str:
    head = "| Design | " + " | ".join(c[0] for c in COLS) + " |"
    sep = "|---|" + "---:|" * len(COLS)
    rows = []
    for d in report["designs"]:
        b = "**" if d["kind"] == "platform" else ""
        rows.append(f"| {b}{d['label']}{b} | " + " | ".join(f(d["mean"].get(k)) for _, k, f in COLS) + " |")
    return "\n".join([head, sep, *rows])


def gates_table(gates: list[dict]) -> str:
    rows = [f"| {g['label']} | {'–' if g['value'] is None else round(g['value'], 3)} | {g['limit']} | {'pass' if g['pass'] else '**fail**'} |" for g in gates]
    return "\n".join(["| Gate | Value | Limit | |", "|---|---:|---|---|", *rows])


def by_id(report: dict, i: str) -> dict:
    return next(d for d in report["designs"] if d["id"] == i)["mean"]


def results_block(dev: dict, heldout: dict | None, bench: dict | None) -> str:
    s, m, p = by_id(dev, "status-quo"), by_id(dev, "model-alone"), by_id(dev, "platform")
    passed = sum(g["pass"] for g in dev["gates"])
    out = [
        f"Dev: {len(dev['seeds'])} simulated lab years, about {dev['requestsPerSeed']:,} test requests each, 8 dynamometers at about 90% load. "
        f"Every design meets the same requests, runs, faults and model behaviour. **{passed} of {len(dev['gates'])} gates pass.**",
        "",
        "| | Today | Model alone | Platform |",
        "|---|---:|---:|---:|",
        f"| Median days from request to result | {num(s['leadTimeMedianDays'])} | {num(m['leadTimeMedianDays'])} | **{num(p['leadTimeMedianDays'])}** |",
        f"| Homologation deadlines missed | {pct(s['homologationMissRate'])} | {pct(m['homologationMissRate'])} | **{pct(p['homologationMissRate'])}** |",
        f"| Wrong results released per 1,000 | {num(s['wrongResultsPer1000'])} | {num(m['wrongResultsPer1000'])} | **{num(p['wrongResultsPer1000'])}** |",
        f"| Orders that are not what was asked, per 1,000 | {num(s['wrongOrdersPer1000'])} | {num(m['wrongOrdersPer1000'])} | **{num(p['wrongOrdersPer1000'])}** |",
        f"| Bench hours lost to faulty or wrong runs, per week | {num(s['lostBenchHoursPerWeek'], 0)} | {num(m['lostBenchHoursPerWeek'], 0)} | **{num(p['lostBenchHoursPerWeek'], 0)}** |",
        f"| Fixture changeover hours per week | {num(s['changeoverHoursPerWeek'], 0)} | {num(m['changeoverHoursPerWeek'], 0)} | **{num(p['changeoverHoursPerWeek'], 0)}** |",
        f"| Coordinator hours per week entering requests | {num(s['coordinatorHoursPerWeek'])} | {num(m['coordinatorHoursPerWeek'])} | **{num(p['coordinatorHoursPerWeek'])}** |",
        f"| Model cost per 1,000 requests | €0 | €{num(m['modelCostPer1000Eur'], 2)} | **€{num(p['modelCostPer1000Eur'], 2)}** |",
        "",
        "**What each part is worth** (each row changes one part of the platform)",
        "",
        table(dev),
        "",
        f"**Modernization parity:** on runs the new and the legacy evaluator both scored, they differ beyond rounding on {pct(p['parityDiffersShare'])}: "
        "the legacy fade loop skips the last fade stop, which is usually the hottest. With the legacy calculation the platform "
        f"releases {num(by_id(dev, 'legacy-evaluator')['verdictErrorsPer1000'])} wrong pass/fail verdicts per 1,000 results; with the new one, {num(p['verdictErrorsPer1000'])}.",
        "",
        "**Gates (dev)**",
        "",
        gates_table(dev["gates"]),
        "",
    ]
    if heldout:
        hs, hp = by_id(heldout, "status-quo"), by_id(heldout, "platform")
        hpass = sum(g["pass"] for g in heldout["gates"])
        out += [f"**Held-out ({len(heldout['seeds'])} further lab years, run once after the design was frozen): {hpass} of {len(heldout['gates'])} gates pass.** "
                f"Median lead time {num(hs['leadTimeMedianDays'])} → {num(hp['leadTimeMedianDays'])} days; wrong results per 1,000 "
                f"{num(hs['wrongResultsPer1000'])} → {num(hp['wrongResultsPer1000'])}; homologation deadlines missed {pct(hs['homologationMissRate'])} → {pct(hp['homologationMissRate'])}.", ""]
        failed = [g for g in heldout["gates"] if not g["pass"]]
        if failed:
            out += ["Failed on held-out: " + "; ".join(f"{g['label']} ({round(g['value'], 3)}, limit {g['limit']})" for g in failed) + ".", ""]
        out += [gates_table(heldout["gates"]), ""]
    else:
        out += ["_Held-out not run yet._", ""]
    if bench:
        out += [bench["line"]]
    return "\n".join(out)


def write_readme_block(block: str) -> None:
    p = ROOT / "README.md"
    if not p.exists():
        return
    t = p.read_text()
    a, b = t.find("<!-- results:start -->"), t.find("<!-- results:end -->")
    if a < 0 or b < 0:
        return
    p.write_text(t[: a + len("<!-- results:start -->")] + "\n" + block + "\n" + t[b:])


def full_report(dev: dict, heldout: dict | None) -> str:
    out = ["# Study report", "", "Generated by `dynolab lab report`. Do not edit by hand.", ""]
    for r in (dev, heldout):
        if not r:
            continue
        out += [f"## {'Dev' if r['kind'] == 'dev' else 'Held-out'} seeds {', '.join(map(str, r['seeds']))}", "", table(r), "", gates_table(r["gates"]), ""]
        out += ["### Per seed, platform vs today", "", "| Seed | Today lead days | Platform lead days | Today wrong /1,000 | Platform wrong /1,000 |", "|---|---:|---:|---:|---:|"]
        sq = next(d for d in r["designs"] if d["id"] == "status-quo")
        pl = next(d for d in r["designs"] if d["id"] == "platform")
        for i, seed in enumerate(r["seeds"]):
            a, b = sq["perSeed"][i], pl["perSeed"][i]
            out.append(f"| {seed} | {num(a['leadTimeMedianDays'])} | {num(b['leadTimeMedianDays'])} | {num(a['wrongResultsPer1000'])} | {num(b['wrongResultsPer1000'])} |")
        out.append("")
    return "\n".join(out)
