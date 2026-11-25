# Repeatable tasks

## Add a check to the run monitor
1. Add it to `analytics/src/dynolab/monitor.py` with a reason an engineer can act on, and its
   thresholds to `config/platform.json` (`monitor`).
2. Add a fault to the world that the check should catch (`dynolab/world/dyno.py`), and a test in
   `analytics/tests/test_monitor.py` for catches and false stops.
3. Add an ablation without it to `config/designs.json`; run `dynolab lab dev`.

## Change the intake rules
1. Change `src/Rdo.Core/Intake/IntakeValidator.cs`; every rule gets a passing and a failing case in
   `tests/Rdo.Tests/IntakeTests.cs`.
2. Rebuild `rdo-core` and run `dynolab lab dev`: watch wrong orders and the share asked back.

## Add a SQL Server migration
1. Add `src/Rdo.SqlServer/Migrations/V00N__what.sql`. Additive only; `GO` separates batches.
2. Extend `tests/Rdo.Tests/SqlServerTests.cs`; CI runs it against SQL Server 2022.

## Switch the evaluation engine
`config/platform.json` → `evaluation.engine`: `legacy`, `shadow` (new answers, legacy compared) or
`new`. Review `/api/results` parity before switching from `shadow` to `new`.

## Run the stack
`docker compose up` (SQL Server, analytics, API with 60 demo requests), then `cd web && npm run dev`.
Without Docker: `dynolab serve`, `RDO_DEMO_SEED=60 dotnet run --project src/Rdo.Api`, `npm run dev`.

## Refresh generated files
`dynolab demo` (demo master data, requests and sample runs), `dynolab lab report` (README, REPORT.md,
console data). Screenshots: `python scripts/screenshots.py` with the API on :5080 and the console
preview on :4173.
