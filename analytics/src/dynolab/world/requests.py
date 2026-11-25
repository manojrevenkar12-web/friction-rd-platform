"""Test requests as engineers write them, in German or English, with the order each one really means."""

from __future__ import annotations

import datetime as dt
from dataclasses import dataclass

from dynolab.rng import Rng
from dynolab.world.catalog import CUSTOMERS, CV, NVH, PC, PROCEDURE_MIX, Catalog

T0 = dt.datetime(2027, 1, 4, 0, 0, tzinfo=dt.UTC)  # a Monday
NAMES = ["Anna", "Jens", "Mehmet", "Katrin", "Lukas", "Priya", "Tobias", "Sven", "Olga", "Marco"]


@dataclass
class Request:
    id: str
    arrival_h: float
    text: str
    lang: str
    kind: str  # homologation, development, exploratory
    formulation: str
    application: str
    procedure: str
    samples: int
    deadline: dt.date | None
    customer: str | None
    missing_axle: bool
    second_code: str | None
    formulation_status: str

    @property
    def arrival(self) -> dt.datetime:
        return T0 + dt.timedelta(hours=self.arrival_h)

    @property
    def deadline_h(self) -> float | None:
        if self.deadline is None:
            return None
        end = dt.datetime(self.deadline.year, self.deadline.month, self.deadline.day, 16, 0, tzinfo=dt.UTC)
        return (end - T0).total_seconds() / 3600


def working_day_start(day: int) -> float:
    """Hours from T0 to 08:00 on the given working day (Mon–Fri)."""
    weeks, rest = divmod(day, 5)
    return (weeks * 7 + rest) * 24 + 8


def make_requests(seed: int, split: str, cat: Catalog, cfg: dict) -> list[Request]:
    out: list[Request] = []
    released = [f for f in cat.formulations if f["status"] == "releasedForTest"]
    other = [f for f in cat.formulations if f["status"] != "releasedForTest"]
    amb = cfg["ambiguity"]
    for day in range(cfg["workingDays"]):
        for kind_key, bench in (("passengerCar", PC), ("commercialVehicle", CV), ("nvh", NVH)):
            dr = Rng(seed, split, "day", day, bench)
            for k in range(dr.poisson(cfg["requestsPerWorkingDay"][kind_key])):
                r = dr.child(k)
                rid = f"{split[0].upper()}{seed}-{day:03d}-{bench[:2]}{k}"
                proc = r.weighted(PROCEDURE_MIX[bench])
                cls = CV if bench == CV else PC
                pool = released if r.random() > 0.05 else other
                f = r.choice([x for x in pool if any(cat.application(a)["vehicleClass"] == cls for a in x["applications"])] or released)
                apps = [a for a in f["applications"] if cat.application(a)["vehicleClass"] == cls]
                # Mostly the specified application; sometimes an engineer tries a mix somewhere new.
                app = r.choice(apps) if apps and r.random() < 0.9 else r.choice([a["id"] for a in cat.applications if a["vehicleClass"] == cls])
                req_kind = r.weighted(cfg["requestKinds"])
                arrival = working_day_start(day) + r.uniform(0, 9)
                customer = None
                deadline = None
                if req_kind != "exploratory":
                    lo, hi = cfg["deadlineWeeks"][req_kind]
                    d = (T0 + dt.timedelta(hours=arrival + 24 * 7 * r.uniform(lo, hi))).date()
                    if req_kind == "homologation":
                        y, w, _ = d.isocalendar()
                        d = dt.date.fromisocalendar(y, w, 5)
                        customer = "OEM-B" if proc == "AKM" and r.random() < amb["customerVariant"] else r.choice([c for c in CUSTOMERS if c != "OEM-B"])
                    deadline = d
                if proc == "AKM" and customer == "OEM-B":
                    proc = "AKM-OEMB"
                min_samples = cat.procedure(proc)["minSamples"]
                states = r.random() < amb["statesSamples"] or min_samples > 1
                samples = max(min_samples, r.randint(1, 3)) if states else min_samples
                missing_axle = r.random() < amb["missingAxle"]
                second = r.choice([x["code"] for x in cat.formulations if x["code"] != f["code"]]) if r.random() < amb["twoFormulations"] else None
                lang = "de" if r.random() < cfg["germanShare"] else "en"
                text = render(r.child("text"), lang, cat, f["code"], app, proc, samples if states else None, deadline, req_kind, customer, missing_axle, second)
                out.append(Request(rid, arrival, text, lang, req_kind, f["code"], app, proc, samples, deadline, customer, missing_axle, second, f["status"]))
    return sorted(out, key=lambda q: q.arrival_h)


def render(r: Rng, lang: str, cat: Catalog, fm: str, app_id: str, proc_id: str, samples: int | None, deadline: dt.date | None,
           kind: str, customer: str | None, missing_axle: bool, second: str | None) -> str:
    app = cat.application(app_id)
    proc = cat.procedure(proc_id)
    base = cat.procedure(proc["customerVariantOf"]) if proc.get("customerVariantOf") else proc
    alias = r.choice(base["aliases"])
    code = fm if r.random() < 0.7 else fm.replace("-", " ")
    name = r.choice(NAMES)
    if lang == "de":
        axle = "" if missing_axle else " " + r.choice({"front": ["Vorderachse", "VA"], "rear": ["Hinterachse", "HA"]}[app["axle"]])
        parts = [r.choice(["Hallo Prüffeld,", "Hi zusammen,", "Moin,"]),
                 f"bitte {alias} für Rezeptur {code} auf {app['vehicle']}{axle}."]
        if second:
            parts.append(f"Zum Vergleich kennt ihr noch {second}.")
        if samples is not None:
            parts.append(f"{samples} Muster liegen bereit." if samples > 1 else "1 Muster liegt bereit.")
        if deadline is not None and kind == "homologation":
            parts.append(f"Wir brauchen die Ergebnisse bis KW {deadline.isocalendar()[1]} für die {customer} Freigabe.")
        elif deadline is not None:
            parts.append(f"Bitte bis {deadline.day:02d}.{deadline.month:02d}.{deadline.year} fertig.")
        parts.append(f"Danke, {name}")
    else:
        axle = "" if missing_axle else " " + r.choice({"front": ["front axle", "front"], "rear": ["rear axle", "rear"]}[app["axle"]])
        parts = [r.choice(["Hi lab,", "Hello test team,", "Hi all,"]),
                 f"please run {alias} on {code} for the {app['vehicle']}{axle}."]
        if second:
            parts.append(f"For reference, the predecessor was {second}.")
        if samples is not None:
            parts.append(f"{samples} pad sets are ready." if samples > 1 else "1 pad set is ready.")
        if deadline is not None and kind == "homologation":
            parts.append(f"We need results by week {deadline.isocalendar()[1]} for the {customer} release.")
        elif deadline is not None:
            parts.append(f"Please finish by {deadline.isoformat()}.")
        parts.append(f"Thanks, {name}")
    return " ".join(parts)
