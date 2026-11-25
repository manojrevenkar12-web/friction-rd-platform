"""One simulated year of the lab under one design. Requests arrive, become orders (by a coordinator
or by the intake agent), wait for a bench, run, are watched (or reviewed afterwards), and are
released. The world decides what really happened; the design only decides what the lab does."""

from __future__ import annotations

import datetime as dt
import heapq
import itertools
from dataclasses import dataclass, field
from typing import cast

from dynolab.lab.core import Core
from dynolab.lab.simmodel import SimulatedIntakeModel
from dynolab.monitor import check_run
from dynolab.rng import Rng
from dynolab.world.catalog import Catalog
from dynolab.world.dyno import Run, simulate_run
from dynolab.world.requests import T0, Request


def iso(h: float) -> str:
    return (T0 + dt.timedelta(hours=h)).isoformat()


def working_days(r: Rng, lo: float, hi: float) -> float:
    """A delay in working days, in calendar hours (a working week is 5 of 7 days)."""
    return r.uniform(lo, hi) * 24 * 7 / 5


@dataclass
class Order:
    request: Request
    formulation: str
    application: str
    procedure: str
    samples: int
    deadline_h: float | None
    priority: str
    ready_h: float
    correct: bool
    attempt: int = 0
    overrides: set[str] = field(default_factory=set)  # checks an engineer has cleared for this order after a false stop


@dataclass
class Design:
    id: str
    label: str
    kind: str
    intake: str  # manual, agent, model
    policy: str  # fifo, slack-aware
    monitor: list[str]  # checks switched on; [] = review after the run only
    engine: str  # legacy, new


@dataclass
class Caches:
    runs: dict = field(default_factory=dict)
    verdicts: dict = field(default_factory=dict)
    evals: dict = field(default_factory=dict)
    intake: dict = field(default_factory=dict)


PRIORITY = {"homologation": "homologation", "development": "development", "exploratory": "exploratory"}


class LabYear:
    def __init__(self, seed: int, cat: Catalog, cfg: dict, platform: dict, requests: list[Request], core: Core, model: SimulatedIntakeModel,
                 caches: Caches) -> None:
        self.seed, self.cat, self.cfg, self.platform = seed, cat, cfg, platform
        self.requests = requests
        self.core = core
        self.model = model
        self.c = caches
        self.people = cfg["people"]
        self.calls: dict[str, int] = {}

    # Intake ------------------------------------------------------------------------------------
    def intake(self, d: Design, q: Request) -> tuple[Order | None, float, float, str]:
        """Returns (order or None if correctly closed, ready time, coordinator minutes, outcome)."""
        r = Rng(self.seed, "people", q.id)
        truth_proc = q.procedure
        unclear = q.missing_axle or q.second_code is not None or q.formulation_status == "inDevelopment"
        if q.formulation_status == "obsolete":
            # Any path ends with the request refused; it costs the requester a day either way.
            return None, q.arrival_h + 24, (9 if d.intake == "manual" else 2), "rejected"
        if d.intake == "manual":
            p = self.people["coordinator"]
            ready = q.arrival_h + working_days(r.child("delay"), *p["delayWorkingDays"])
            minutes = p["minutesPerRequest"]
            if unclear and r.random() < p["asksWhenUnclear"]:
                ready += r.uniform(*self.people["requesterAnswerDays"]) * 24
                return self.order(q, q.formulation, q.application, truth_proc, q.samples, ready, True), ready, minutes + 5, "clarified"
            app = q.application
            if q.missing_axle and r.random() < 0.5:
                app = self.sibling(q.application)
            proc = truth_proc
            if self.cat.procedure(truth_proc).get("customerVariantOf") and r.random() < p["missesCustomerVariant"]:
                proc = self.cat.procedure(truth_proc)["customerVariantOf"]
            fm = q.formulation
            if r.random() < p["fieldErrorRate"]:
                fm = self.neighbour(q.formulation)
            if r.random() < p["fieldErrorRate"]:
                app = self.sibling(app)
            correct = (fm, app, proc) == (q.formulation, q.application, truth_proc) and q.formulation_status == "releasedForTest"
            return self.order(q, fm, app, proc, q.samples, ready, correct), ready, minutes, "entered"
        draft = self.model.draft(q)
        if d.intake == "model":
            if draft["questions"] or any(draft[k]["value"] is None for k in ("formulation", "application", "procedure")):
                ready = q.arrival_h + r.uniform(*self.people["requesterAnswerDays"]) * 24
                self.calls[q.id] = 1
                return self.order(q, q.formulation, q.application, truth_proc, q.samples, ready, True), ready, 3, "clarified"
            self.calls[q.id] = 1
            fm, app, proc = draft["formulation"]["value"], draft["application"]["value"], draft["procedure"]["value"]
            known = fm in {f["code"] for f in self.cat.formulations}
            fm = fm if known else q.formulation
            correct = (fm, app, proc) == (q.formulation, q.application, truth_proc) and q.formulation_status == "releasedForTest"
            return self.order(q, fm, app, proc, q.samples, q.arrival_h + 0.1, correct), q.arrival_h + 0.1, 0, "drafted"
        key = q.id
        if key not in self.c.intake:
            dec = self.core.call({"cmd": "validate", "text": q.text, "draft": draft, "orderId": f"TO-{q.id}", "now": iso(q.arrival_h)})
            calls = 1
            if dec["status"] == "needsClarification" and dec["problems"]:
                # As IntakeAgent does: the model gets the findings and one more read.
                dec = self.core.call({"cmd": "validate", "text": q.text, "draft": self.model.draft(q, 1), "orderId": f"TO-{q.id}", "now": iso(q.arrival_h)})
                calls = 2
            self.c.intake[key] = (dec, calls)
        dec, calls = self.c.intake[key]
        self.calls[q.id] = calls
        if dec["status"] == "accepted":
            o = dec["order"]
            correct = (o["formulation"], o["application"], o["procedure"]) == (q.formulation, q.application, truth_proc)
            return self.order(q, o["formulation"], o["application"], o["procedure"], o["samples"], q.arrival_h + 0.1, correct), q.arrival_h + 0.1, 0, "accepted"
        if dec["status"] == "rejected":
            return None, q.arrival_h + 0.1, 2, "rejected"
        ready = q.arrival_h + r.uniform(*self.people["requesterAnswerDays"]) * 24
        return self.order(q, q.formulation, q.application, truth_proc, q.samples, ready, True), ready, 3, "clarified"

    def order(self, q: Request, fm: str, app: str, proc: str, samples: int, ready: float, correct: bool) -> Order:
        return Order(q, fm, app, proc, samples, q.deadline_h, PRIORITY[q.kind], ready, correct)

    def sibling(self, app_id: str) -> str:
        a = self.cat.application(app_id)
        others = [x["id"] for x in self.cat.applications if x["vehicle"] == a["vehicle"] and x["id"] != app_id]
        return others[0] if others else app_id

    def neighbour(self, code: str) -> str:
        codes = sorted(f["code"] for f in self.cat.formulations)
        i = codes.index(code)
        return codes[i + 1] if i + 1 < len(codes) else codes[i - 1]

    # Runs --------------------------------------------------------------------------------------
    def run(self, o: Order) -> Run:
        key = (o.request.id, o.attempt, o.formulation, o.application, o.procedure)
        if key not in self.c.runs:
            self.c.runs[key] = simulate_run(self.seed, self.cat, self.cfg, o.request.id, o.attempt, o.formulation, o.application, o.procedure)
        return self.c.runs[key]

    def verdict(self, run: Run, o: Order, checks: list[str]) -> tuple[int | None, str | None]:
        if not checks:
            return None, None
        key = (o.request.id, o.attempt, o.formulation, o.application, o.procedure, tuple(checks))
        if key not in self.c.verdicts:
            v = check_run(run.stops, self.cat.procedure(o.procedure), self.platform["monitor"], set(checks))
            self.c.verdicts[key] = (v.stop_at, v.check)
        return self.c.verdicts[key]

    def evaluate(self, run: Run, o: Order) -> dict | None:
        proc = self.cat.procedure(o.procedure)
        if not any(s["name"].startswith("fade") for s in proc["sections"]):
            return None
        key = (o.request.id, o.attempt, o.formulation, o.application, o.procedure)
        if key not in self.c.evals:
            payload = {"orderId": f"TO-{o.request.id}", "procedure": o.procedure, "formulation": o.formulation,
                       "stops": [{k: s[k] for k in ("index", "section", "speedKmh", "pressureBar", "initialTempC", "mu")} for s in run.stops]}
            self.c.evals[key] = self.core.call({"cmd": "evaluate", "run": payload})
        return self.c.evals[key]

    def true_pass(self, q: Request) -> bool | None:
        """The verdict the right test, run cleanly, would give: computed on the true µ."""
        proc = self.cat.procedure(q.procedure)
        if not any(s["name"].startswith("fade") for s in proc["sections"]):
            return None
        run = simulate_run(self.seed, self.cat, {**self.cfg, "faults": {**self.cfg["faults"], "shareOfRuns": 0}}, q.id, 0, q.formulation, q.application, q.procedure)
        names = [s["section"] for s in run.stops]
        mu = run.true_mu
        char = [m for m, n in zip(mu, names, strict=True) if n.startswith("characteristic")]
        fade = [m for m, n in zip(mu, names, strict=True) if n.startswith("fade")]
        f = self.cat.formulation(q.formulation)
        nominal = sum(char) / len(char)
        return f["muTargetLow"] <= nominal <= f["muTargetHigh"] and min(fade) >= self.platform["evaluation"]["fadeFloorShareOfTarget"] * f["muTargetLow"]

    # The year ----------------------------------------------------------------------------------
    def simulate(self, d: Design) -> list[dict]:
        cfg_people = self.people
        self.calls = {}
        benches = {b["id"]: {"id": b["id"], "kind": b["kind"], "mountedFixture": None, "freeAt": 0.0, "busy": False} for b in self.cat.benches}
        queue: dict[str, list[Order]] = {"passengerCar": [], "commercialVehicle": [], "nvh": []}
        events: list[tuple[float, int, str, object]] = []
        tick = itertools.count()
        rows: dict[str, dict] = {}
        for q in self.requests:
            o, ready, minutes, outcome = self.intake(d, q)
            rows[q.id] = {"id": q.id, "kind": q.kind, "arrival": q.arrival_h, "deadline": q.deadline_h, "intake": outcome, "minutes": minutes,
                          "orderCorrect": None if o is None else o.correct, "released": None, "resultOk": None, "lostHours": 0.0, "changeoverHours": 0.0,
                          "runs": 0, "faultsCaughtLive": 0, "faultsCaughtReview": 0, "faultsReleased": 0, "falseStops": 0, "verdictWrong": False,
                          "parity": None, "wrongOrderRun": False, "modelCalls": self.calls.get(q.id, 0)}
            if o is not None:
                heapq.heappush(events, (ready, next(tick), "ready", o))

        def bench_kind(o: Order) -> str:
            return self.cat.procedure(o.procedure)["bench"]

        def try_dispatch(now: float) -> None:
            for b in sorted(benches.values(), key=lambda x: x["id"]):
                if b["busy"] or not queue[b["kind"]]:
                    continue
                qd = [{"id": f"{o.request.id}#{o.attempt}", "bench": bench_kind(o), "fixture": self.cat.application(o.application)["fixture"],
                       "hours": self.cat.procedure(o.procedure)["hours"], "readyAt": iso(o.ready_h),
                       "deadline": None if o.deadline_h is None else iso(o.deadline_h), "priority": o.priority} for o in queue[b["kind"]]]
                pick = self.core.call({"cmd": "dispatch", "policy": d.policy, "now": iso(now),
                                       "bench": {"id": b["id"], "kind": b["kind"], "mountedFixture": b["mountedFixture"], "freeAt": iso(max(now, b["freeAt"]))},
                                       "queue": qd})
                if pick is None:
                    continue
                o = next(x for x in queue[b["kind"]] if f"{x.request.id}#{x.attempt}" == pick["orderId"])
                queue[b["kind"]].remove(o)
                start(b, o, now, pick["changeover"])

        def start(b: dict, o: Order, now: float, changeover: bool) -> None:
            row = rows[o.request.id]
            ch = self.platform["planner"]["changeoverHours"] if changeover else 0.0
            row["changeoverHours"] += ch
            run = self.run(o)
            row["runs"] += 1
            n = len(run.stops)
            stop_at, check = self.verdict(run, o, [c for c in d.monitor if c not in o.overrides])
            b["busy"] = True
            b["mountedFixture"] = self.cat.application(o.application)["fixture"]
            if stop_at is not None:
                hours = run.hours * (stop_at + 1) / n
                done = now + ch + hours
                row["lostHours"] += hours
                if run.fault is None:
                    row["falseStops"] += 1
                    # The engineer checks the bench, finds nothing, and clears that check for this order.
                    o.overrides.add(check or "")
                    nxt = done + self.platform["monitor"]["restartHours"]
                else:
                    row["faultsCaughtLive"] += 1
                    nxt = done + cfg_people["faultFixHours"]
                heapq.heappush(events, (done, next(tick), "free", b))
                o.attempt += 1
                o.ready_h = nxt
                heapq.heappush(events, (nxt, next(tick), "ready", o))
                return
            done = now + ch + run.hours
            heapq.heappush(events, (done, next(tick), "free", b))
            heapq.heappush(events, (done, next(tick), "review", (o, run)))

        def review(now: float, o: Order, run: Run) -> None:
            row = rows[o.request.id]
            r = Rng(self.seed, "review", o.request.id, o.attempt)
            at = now + r.uniform(*cfg_people["postRunReview"]["delayDays"]) * 24
            if run.fault is not None and r.random() < cfg_people["postRunReview"]["detects"][run.fault]:
                row["faultsCaughtReview"] += 1
                row["lostHours"] += run.hours
                o.attempt += 1
                o.ready_h = at + cfg_people["faultFixHours"]
                heapq.heappush(events, (o.ready_h, next(tick), "ready", o))
                return
            if not o.correct:
                row["wrongOrderRun"] = True
                row["lostHours"] += run.hours
                # The requester reads the report and sees it is not what was asked for, most of the time.
                if r.random() < 0.85 and o.attempt < 6:
                    fixed = self.order(o.request, o.request.formulation, o.request.application, o.request.procedure, o.request.samples, at + 24, True)
                    fixed.attempt = o.attempt + 1
                    heapq.heappush(events, (fixed.ready_h, next(tick), "ready", fixed))
                    return
            row["released"] = at
            if run.fault is not None:
                row["faultsReleased"] += 1
            ev = self.evaluate(run, o) if o.correct and run.fault is None else None
            truth = self.true_pass(o.request) if ev is not None else None
            verdict_ok = True
            if ev is not None:
                got = ev[d.engine]["pass"]
                verdict_ok = got == truth
                row["verdictWrong"] = not verdict_ok
                row["parity"] = ev["parity"]["outcome"]
            row["resultOk"] = o.correct and run.fault is None and verdict_ok

        while events:
            now, _, kind, obj = heapq.heappop(events)
            if kind == "ready":
                o = obj  # type: ignore[assignment]
                queue[bench_kind(o)].append(o)  # type: ignore[arg-type]
            elif kind == "free":
                obj["busy"] = False  # type: ignore[index]
                obj["freeAt"] = now  # type: ignore[index]
            elif kind == "review":
                done_order, done_run = cast(tuple[Order, Run], obj)
                review(now, done_order, done_run)
                continue
            try_dispatch(now)
        return list(rows.values())
