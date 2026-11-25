"""A stand-in for the language model that reads test requests. It reads well but not perfectly:
it sometimes takes a neighbouring formulation code, guesses an axle the request does not state,
misses that a customer needs its own procedure variant, converts a calendar week wrongly, or quotes
words that are not in the request. Every draw is keyed by the request, so every design meets the
same behaviour."""

from __future__ import annotations

import datetime as dt
import re

from dynolab.rng import Rng
from dynolab.world.catalog import Catalog
from dynolab.world.requests import Request

AXLE_WORDS = ["Vorderachse", "VA", "Hinterachse", "HA", "front axle", "rear axle", "front", "rear"]


def span(text: str, pattern: str) -> str | None:
    m = re.search(pattern, text, re.IGNORECASE)
    return m.group(0) if m else None


class SimulatedIntakeModel:
    name = "simulated-llm"

    def __init__(self, seed: int, cat: Catalog, cfg: dict) -> None:
        self.seed = seed
        self.cat = cat
        self.cfg = cfg

    def draft(self, q: Request, attempt: int = 0) -> dict:
        r = Rng(self.seed, "intake-llm", q.id, attempt) if attempt else Rng(self.seed, "intake-llm", q.id)
        c = self.cfg
        text = q.text
        questions: list[str] = []
        # Formulation
        code_span = span(text, r"FM[\s-]?" + q.formulation[3:])
        value = q.formulation
        if r.random() < c["pickNeighbourFormulation"]:
            d = q.formulation[3:]
            value = f"FM-{d[0]}{d[2]}{d[1]}{d[3]}" if d[1] != d[2] else f"FM-{d[:3]}{(int(d[3]) + 1) % 10}"
        if q.second_code and r.random() < c["asksWhenTwoCodes"]:
            questions.append(f"Which formulation is to be tested, {q.formulation} or {q.second_code}?")
        formulation = {"value": value, "evidence": code_span}
        # Application
        app = self.cat.application(q.application)
        vehicle_span = span(text, re.escape(app["vehicle"]) + r"(\s+(" + "|".join(map(re.escape, AXLE_WORDS)) + r"))?")
        app_value: str | None = app["id"]
        if q.missing_axle:
            if r.random() < c["guessAxleWhenMissing"]:
                same = [a for a in self.cat.applications if a["vehicle"] == app["vehicle"]]
                app_value = sorted(same, key=lambda a: a["axle"])[0]["id"]  # "front" sorts first
            else:
                app_value = None
                questions.append(f"Front or rear axle of the {app['vehicle']}?")
        elif r.random() < c["pickWrongVehicle"]:
            app_value = r.choice([a["id"] for a in self.cat.applications if a["vehicleClass"] == app["vehicleClass"] and a["vehicle"] != app["vehicle"]])
        if r.random() < c["inventEvidence"]:
            vehicle_span = f"{app['vehicle']} brake system"
        application = {"value": app_value, "evidence": vehicle_span}
        # Procedure
        proc = self.cat.procedure(q.procedure)
        base = self.cat.procedure(proc["customerVariantOf"]) if proc.get("customerVariantOf") else proc
        alias_span = next((span(text, re.escape(a)) for a in base["aliases"] if span(text, re.escape(a))), None)
        proc_value = q.procedure
        if proc.get("customerVariantOf") and r.random() < c["missCustomerVariant"]:
            proc_value = base["id"]
        procedure = {"value": proc_value, "evidence": alias_span}
        # Samples
        sample_span = span(text, r"\b\d{1,2}\s*(Muster|pad sets?)\b")
        samples = None
        if sample_span:
            n = int(re.match(r"\d+", sample_span).group(0))  # type: ignore[union-attr]
            samples = {"value": str(n + 1 if r.random() < c["wrongSamples"] else n), "evidence": sample_span}
        # Deadline
        deadline = None
        if q.deadline is not None:
            dspan = span(text, r"(KW|week)\s*\d{1,2}") or span(text, r"\d{2}\.\d{2}\.\d{4}") or span(text, r"\d{4}-\d{2}-\d{2}")
            due = q.deadline
            if r.random() < c["wrongWeekToDate"]:
                due = due - dt.timedelta(days=7)
            deadline = {"value": due.isoformat(), "evidence": dspan}
        return {"formulation": formulation, "application": application, "procedure": procedure,
                "samples": samples or {"value": None, "evidence": None}, "deadline": deadline or {"value": None, "evidence": None},
                "questions": questions}
