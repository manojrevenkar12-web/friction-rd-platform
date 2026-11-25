"""The lab's catalogue: benches, brake applications, friction formulations and test procedures,
with the true friction behaviour of every formulation on every application. The master data the
platform loads is the catalogue without the truth."""

from __future__ import annotations

import json
from dataclasses import dataclass, field

from dynolab.paths import ROOT
from dynolab.rng import Rng

PC, CV, NVH = "passengerCar", "commercialVehicle", "nvh"
VEHICLES = {
    PC: ["Compact C1", "Compact C2", "Midsize M1", "Midsize M3", "Sports S2", "SUV U1", "SUV U4", "Electric E1", "Electric E3", "Estate K2"],
    CV: ["Van V1", "Van V2", "Truck T4", "Truck T7", "Bus B2"],
}
CUSTOMERS = ["OEM-A", "OEM-B", "OEM-C"]


def section(name: str, stops: int, bar: float, kmh: float, temp: float) -> dict:
    return {"name": name, "stops": stops, "pressureBar": bar, "speedKmh": kmh, "initialTempC": temp}


def akm_sections(burnish_stops: int, burnish_bar: float, speed_bar: float) -> list[dict]:
    return [
        section("burnish", burnish_stops, burnish_bar, 80, 100),
        section("characteristic-1", 6, 40, 80, 100),
        *[section("speed", 6, speed_bar, v, 100) for v in (40, 80, 120, 160)],
        section("characteristic-2", 6, 40, 80, 100),
        section("fade-1", 15, 60, 100, 100),
        section("recovery", 18, 30, 80, 100),
        section("characteristic-3", 6, 40, 80, 100),
        section("fade-2", 15, 60, 100, 100),
    ]


PROCEDURES = [
    {"id": "AKM", "name": "Friction effectiveness (AK Master style)", "bench": PC, "vehicleClass": PC, "hours": 14, "minSamples": 1,
     "aliases": ["AK Master", "AK-Master", "AKM", "Wirksamkeit", "effectiveness"], "sections": akm_sections(32, 30, 40)},
    {"id": "AKM-OEMB", "name": "Friction effectiveness, OEM-B variant", "bench": PC, "vehicleClass": PC, "hours": 15, "minSamples": 1,
     "customerVariantOf": "AKM", "customer": "OEM-B", "aliases": [], "sections": akm_sections(40, 35, 30)},
    {"id": "AKM-CV", "name": "Friction effectiveness, commercial vehicles", "bench": CV, "vehicleClass": CV, "hours": 18, "minSamples": 1,
     "aliases": ["AK Master", "AK-Master", "AKM", "Wirksamkeit", "effectiveness"], "sections": akm_sections(48, 50, 60)},
    {"id": "R90", "name": "ECE R90 comparison", "bench": PC, "vehicleClass": PC, "hours": 8, "minSamples": 2,
     "aliases": ["R90", "R 90", "ECE R90"],
     "sections": [section("burnish", 32, 30, 80, 100), section("characteristic-1", 6, 40, 80, 100), section("fade-1", 10, 60, 100, 100),
                  section("characteristic-2", 6, 40, 80, 100)]},
    {"id": "FADE", "name": "Short fade", "bench": PC, "vehicleClass": PC, "hours": 5, "minSamples": 1,
     "aliases": ["fade test", "Fadingtest", "Fading-Test"],
     "sections": [section("burnish", 16, 30, 80, 100), section("characteristic-1", 3, 40, 80, 100), section("fade-1", 15, 60, 100, 100),
                  section("characteristic-2", 3, 40, 80, 100)]},
    {"id": "NVH", "name": "Brake noise matrix (SAE J2521 style)", "bench": NVH, "vehicleClass": PC, "hours": 22, "minSamples": 1,
     "aliases": ["NVH", "noise", "Geräusch", "J2521"],
     "sections": [section("burnish", 30, 30, 50, 80), *[section("noise", 40, bar, 10, t) for bar, t in ((5, 50), (15, 150), (25, 250))]]},
    {"id": "WEAR", "name": "Wear at temperature steps", "bench": PC, "vehicleClass": PC, "hours": 36, "minSamples": 1,
     "aliases": ["wear test", "Verschleiß"],
     "sections": [section("burnish", 16, 30, 80, 100), *[section(f"wear-{t}", 100, 30, 60, t) for t in (100, 200, 300)]]},
]

#: How often each procedure is asked for, per bench kind.
PROCEDURE_MIX = {PC: {"AKM": 0.5, "R90": 0.2, "FADE": 0.2, "WEAR": 0.1}, CV: {"AKM-CV": 1.0}, NVH: {"NVH": 1.0}}


@dataclass
class Truth:
    """How a formulation really behaves on an application. Only the world knows this."""

    mu0: float
    green: float
    fade_onset: float
    fade_depth: float
    speed_slope: float
    material_shift: float


@dataclass
class Catalog:
    seed: int
    benches: list[dict]
    applications: list[dict]
    formulations: list[dict]
    procedures: list[dict]
    truth: dict[tuple[str, str], Truth] = field(default_factory=dict)

    def master_data(self) -> dict:
        return {"benches": self.benches, "applications": self.applications, "formulations": self.formulations, "procedures": self.procedures}

    def procedure(self, pid: str) -> dict:
        return next(p for p in self.procedures if p["id"] == pid)

    def application(self, aid: str) -> dict:
        return next(a for a in self.applications if a["id"] == aid)

    def formulation(self, code: str) -> dict:
        return next(f for f in self.formulations if f["code"] == code)


def load_world_config() -> dict:
    return json.loads((ROOT / "config" / "world.json").read_text())


def build_catalog(seed: int, cfg: dict) -> Catalog:
    r = Rng(seed, "catalog")
    benches = []
    for kind, n in (("passengerCar", PC), ("commercialVehicle", CV), ("nvh", NVH)):
        for i in range(cfg["benches"][kind]):
            benches.append({"id": f"{ {PC: 'D', CV: 'C', NVH: 'N'}[n] }{i + 1:02d}".replace(" ", ""), "kind": n})
    apps: list[dict] = []
    fixtures = 0
    for i in range(cfg["applications"]):
        cls = PC if i < cfg["applications"] * 0.72 else CV
        vehicle = VEHICLES[cls][i % len(VEHICLES[cls])]
        axle = "front" if (i // len(VEHICLES[cls])) % 2 == 0 else "rear"
        aid = f"A{i + 1:02d}"
        if apps and r.random() < cfg["fixturesShareApplications"] and any(a["vehicleClass"] == cls for a in apps):
            fixture = r.choice([a["fixture"] for a in apps if a["vehicleClass"] == cls])
        else:
            fixtures += 1
            fixture = f"FX-{fixtures:02d}"
        if any(a["vehicle"] == vehicle and a["axle"] == axle for a in apps):
            continue
        apps.append({"id": aid, "vehicle": vehicle, "axle": axle, "fixture": fixture, "vehicleClass": cls})
    phys = cfg["physics"]
    formulations = []
    truth: dict[tuple[str, str], Truth] = {}
    used: set[int] = set()
    for i in range(cfg["formulations"]):
        fr = r.child("formulation", i)
        num = fr.randint(1000, 9899)
        while num in used:
            num += 1
        used.add(num)
        code = f"FM-{num}"
        status = fr.weighted({"releasedForTest": cfg["formulationStatus"]["releasedForTest"], "inDevelopment": cfg["formulationStatus"]["inDevelopment"],
                              "obsolete": cfg["formulationStatus"]["obsolete"]})
        cls = PC if fr.random() < 0.72 else CV
        mine = fr.sample([a for a in apps if a["vehicleClass"] == cls], fr.randint(1, 3))
        target = fr.uniform(0.36, 0.46)
        formulations.append({"code": code, "status": status, "applications": [a["id"] for a in mine], "muTargetLow": round(target, 2),
                             "muTargetHigh": round(target + 0.1, 2)})
        for a in apps:
            tr = fr.child("truth", a["id"])
            # Specified applications mostly meet the target; a share of new mixes do not, which is why the lab tests them.
            inside = a in mine and tr.random() < 0.8
            mu0 = target + 0.05 + tr.gauss(0, 0.012) if inside else target + 0.05 + tr.gauss(0, 0.06)
            truth[(code, a["id"])] = Truth(mu0=mu0, green=phys["greenRise"] * tr.uniform(0.5, 1.5), fade_onset=tr.uniform(*phys["fadeOnsetC"]),
                                           fade_depth=tr.uniform(*phys["fadeDepth"]), speed_slope=tr.uniform(*phys["speedSensitivity"]),
                                           material_shift=tr.gauss(0, phys["materialShift"]))
    procedures = [dict(p, customerVariantOf=p.get("customerVariantOf"), customer=p.get("customer")) for p in PROCEDURES]
    return Catalog(seed, benches, apps, formulations, procedures, truth)
