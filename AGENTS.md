# Working in this repository

## Fast feedback
- C#: `dotnet build Rdo.sln -c Release` and `dotnet test Rdo.sln` (SQL Server tests run when
  `RDO_TEST_SQLSERVER` is set; CI starts SQL Server 2022). In a sandbox without nuget.org:
  `dotnet build src/Rdo.Api --configfile nuget.offline.config -p:OfflineSandbox=true` builds
  everything but the SQL Server adapter and the tests.
- Python: `pip install -e "analytics[dev]"`, then `ruff check analytics`, `mypy analytics/src`,
  `pytest -q analytics` (a few seconds).
- Console: `cd web && npm test && npm run build`.
- The lab: `dotnet build src/Rdo.Cli -c Release`, then `dynolab lab dev` (about 4 minutes).

## Invariants
1. **The truth stays in the world.** Only `dynolab.world` knows true friction behaviour, faults and
   what a request really means. The platform's code never imports it; the lab grades against it.
2. **The world does not read the platform's settings.** `dynolab.world` never opens
   `config/platform.json`, `designs.json` or `gates.json` (a test checks this).
3. **Same luck for every design.** Every draw is keyed by what it is for (`dynolab.rng.Rng`).
4. **The lab drives the real core.** The planner, intake validator and evaluators are C# and the lab
   calls them through `rdo-core`; there is no Python copy of them.
5. **The model reads and code decides.** A draft becomes an order only through `IntakeValidator`.
   Unclear requests are asked back, never guessed.
6. **The monitor never blocks a bench.** If the analytics service is down, runs continue and are
   reviewed afterwards.
7. **Migrations are additive.** No migration alters or drops a legacy table; the legacy application
   keeps working through the bridge view.
8. **Who is calling comes from the API key**, never from a request body.
9. **Settings live in `config/`.** No thresholds, hours or prices in code.
10. **Reports are generated.** `reports/*.json`, `REPORT.md`, the README results block and
    `web/public/data/*.json` are written by `dynolab`; do not edit them by hand.

## The held-out rule
Held-out seeds (`config/seeds.json`) are run once, with `dynolab lab heldout`, after designs and
gates are frozen. The run writes `reports/heldout.lock` and the command refuses to run again. Do not
delete the lock. If the design changes afterwards, report dev results only and say the held-out
numbers are for the earlier design.
