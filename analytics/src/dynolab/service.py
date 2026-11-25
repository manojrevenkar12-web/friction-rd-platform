"""The dyno analytics service. The C# platform posts a run's stops (as they arrive, or all at once)
with the procedure that was ordered; the service says whether the run can still be used, and if not,
at which stop and why."""

from __future__ import annotations

import json

from fastapi import FastAPI
from pydantic import BaseModel, Field

from dynolab.monitor import check_run
from dynolab.paths import ROOT

SETTINGS = json.loads((ROOT / "config" / "platform.json").read_text())["monitor"]
CHECKS = {"procedure", "temperature", "cooling", "muJump", "characteristic"}

app = FastAPI(title="Dyno analytics", version="1.0.0")


class Section(BaseModel):
    name: str
    stops: int = Field(ge=1)
    pressureBar: float
    speedKmh: float
    initialTempC: float


class ProcedureIn(BaseModel):
    id: str
    sections: list[Section]


class StopIn(BaseModel):
    index: int
    section: str
    speedKmh: float
    pressureBar: float
    initialTempC: float
    coolingSeconds: float
    mu: float


class CheckIn(BaseModel):
    procedure: ProcedureIn
    stops: list[StopIn]
    cleared: list[str] = Field(default_factory=list, description="checks an engineer has cleared for this order")


class CheckOut(BaseModel):
    usable: bool
    stopAt: int | None
    check: str | None
    reason: str | None
    stopsChecked: int


@app.get("/health")
def health() -> dict:
    return {"ok": True, "checks": sorted(CHECKS)}


@app.post("/v1/runs/check", response_model=CheckOut)
def check(body: CheckIn) -> CheckOut:
    v = check_run([s.model_dump() for s in body.stops], body.procedure.model_dump(), SETTINGS, CHECKS - set(body.cleared))
    return CheckOut(usable=v.stop_at is None, stopAt=v.stop_at, check=v.check, reason=v.reason, stopsChecked=len(body.stops))
