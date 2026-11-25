"""dynolab lab dev|heldout|report|gate   ·   dynolab serve"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys

from dynolab.lab import report as rep
from dynolab.paths import ROOT


def seeds() -> dict:
    return json.loads((ROOT / "config" / "seeds.json").read_text())


def study(kind: str) -> dict:
    from dynolab.lab.study import load_designs, load_json, run_seed

    designs = load_designs()
    per = [run_seed(s, kind, designs) for s in seeds()[kind]]
    summary: list[dict] = [{"id": d.id, "label": d.label, "kind": d.kind, "mean": rep.mean_metrics([p["designs"][d.id] for p in per]),
                "perSeed": [p["designs"][d.id] for p in per]} for d in designs]
    platform = next(d for d in summary if d["id"] == "platform")["mean"]
    sq = next(d for d in summary if d["kind"] == "status-quo")["mean"]
    gates = load_json("gates.json")["gates"]
    return {"kind": kind, "seeds": seeds()[kind], "requestsPerSeed": round(sum(p["requests"] for p in per) / len(per)), "designs": summary,
            "gates": rep.check_gates(gates, platform, sq)}


def regenerate() -> None:
    dev = rep.read("dev")
    if not dev:
        sys.exit("run `dynolab lab dev` first")
    heldout, bench = rep.read("heldout"), rep.read("bench")
    (ROOT / "REPORT.md").write_text(rep.full_report(dev, heldout) + "\n")
    rep.write_readme_block(rep.results_block(dev, heldout, bench))
    rep.write_json(ROOT / "web" / "public" / "data" / "study.json", {"dev": dev, "heldout": heldout, "bench": bench})


def config_hash() -> str:
    h = hashlib.sha256()
    for f in ("platform.json", "world.json", "designs.json", "gates.json", "seeds.json"):
        h.update((ROOT / "config" / f).read_bytes())
    return h.hexdigest()[:16]


def compare(old: dict | None, new: dict, tol: float = 1e-6) -> int:
    """The lab is deterministic: a rerun must give the same numbers."""
    if not old:
        print("no reports/dev.json to compare with")
        return 1
    bad = [f"{o['id']}.{k}: {v} vs {n['mean'][k]}" for o, n in zip(old["designs"], new["designs"], strict=True) for k, v in o["mean"].items()
           if isinstance(v, int | float) and isinstance(n["mean"][k], int | float) and abs(v - n["mean"][k]) > tol * max(1.0, abs(v))]
    print("\n".join(bad) if bad else f"reproduced: {len(old['designs'])} designs")
    return 1 if bad else 0


def write_demo(seed: int = 4242) -> None:
    from dynolab.world.catalog import build_catalog, load_world_config
    from dynolab.world.dyno import simulate_run
    from dynolab.world.requests import make_requests

    cfg = load_world_config()
    cat = build_catalog(seed, cfg)
    reqs = make_requests(seed, "demo", cat, cfg)[:60]
    out = ROOT / "config" / "demo"
    rep.write_json(out / "master-data.json", cat.master_data())
    rep.write_json(out / "requests.json", [{"id": q.id, "text": q.text, "submittedBy": q.text.rsplit(", ", 1)[-1]} for q in reqs])
    samples = {}
    for q in make_requests(seed, "demo", cat, cfg):
        if q.procedure != "AKM":
            continue
        run = simulate_run(seed, cat, cfg, q.id, 0, q.formulation, q.application, q.procedure)
        kind = run.fault or "healthy"
        if kind not in samples:
            samples[kind] = {"fault": run.fault, "onset": run.onset, "order": {"formulation": q.formulation, "application": q.application, "procedure": q.procedure},
                             "stops": run.stops}
        if len(samples) == 6:
            break
    rep.write_json(out / "sample-runs.json", samples)
    print(f"wrote {out}: {len(reqs)} requests, sample runs {sorted(samples)}")


def main(argv: list[str] | None = None) -> None:
    ap = argparse.ArgumentParser(prog="dynolab")
    sub = ap.add_subparsers(dest="cmd", required=True)
    lab = sub.add_parser("lab")
    lab.add_argument("what", choices=["dev", "heldout", "report", "gate"])
    lab.add_argument("--check", action="store_true")
    sub.add_parser("demo", help="write config/demo/: master data, request texts and sample runs for the service and console")
    serve = sub.add_parser("serve")
    serve.add_argument("--port", type=int, default=8100)
    a = ap.parse_args(argv)
    if a.cmd == "serve":
        import uvicorn

        uvicorn.run("dynolab.service:app", host="0.0.0.0", port=a.port)
        return
    if a.cmd == "demo":
        write_demo()
        return
    lock = ROOT / "reports" / "heldout.lock"
    if a.what == "dev" and a.check:
        sys.exit(compare(rep.read("dev"), json.loads(json.dumps(rep.clean(study("dev"))))))
    if a.what == "dev":
        rep.write_json(ROOT / "reports" / "dev.json", study("dev"))
        regenerate()
    elif a.what == "heldout":
        if lock.exists():
            sys.stderr.write(f"Held-out seeds were already run ({lock.read_text().strip()}). They are run once; see AGENTS.md.\n")
            sys.exit(3)
        rep.write_json(ROOT / "reports" / "heldout.json", study("heldout"))
        lock.write_text(f"config {config_hash()} seeds {','.join(map(str, seeds()['heldout']))}\n")
        regenerate()
    elif a.what == "report":
        regenerate()
    elif a.what == "gate":
        dev = rep.read("dev")
        if not dev:
            sys.exit("no reports/dev.json")
        for g in dev["gates"]:
            print("pass" if g["pass"] else "FAIL", g["label"], g["value"], g["limit"])
        sys.exit(0 if all(g["pass"] for g in dev["gates"]) else 1)


if __name__ == "__main__":
    main()
