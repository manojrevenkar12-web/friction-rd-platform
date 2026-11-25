using System.Text.Json;
using Rdo.Api.Infrastructure;
using Rdo.Core;
using Rdo.Core.Domain;
using Rdo.Core.Evaluation;
using Rdo.Core.Json;
using Rdo.Core.Store;
using Rdo.Legacy;

namespace Rdo.Api.Endpoints;

public sealed record StartRun(string BenchId);

public sealed record StopBatch(IReadOnlyList<StopReading> Stops);

public sealed record ClearCheck(string Check, string Note);

public sealed record ReplayRun(string ProcedureId, IReadOnlyList<StopReading> Stops);

public static class RunEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/runs/{orderId}/start", async (string orderId, StartRun body, HttpContext ctx, IRdoStore store, MasterData master, RunRegistry runs, CancellationToken ct) =>
        {
            if (ctx.RequireRole("lab") is { } denied)
            {
                return denied;
            }

            var order = (await store.ListOpenOrdersAsync(ct).ConfigureAwait(false)).FirstOrDefault(o => o.Id == orderId);
            if (order is null)
            {
                return Results.NotFound();
            }

            var bench = master.Benches.FirstOrDefault(b => b.Id == body.BenchId);
            if (bench is null || bench.Kind != master.Procedures[order.Procedure].Bench)
            {
                return Results.Problem($"{order.Procedure} does not run on bench {body.BenchId}.", statusCode: StatusCodes.Status400BadRequest);
            }

            var run = runs.Active.GetOrAdd(orderId, _ => new ActiveRun { OrderId = orderId, BenchId = bench.Id });
            runs.MountedFixture[bench.Id] = master.Applications[order.Application].Fixture;
            await store.AppendAuditAsync(new AuditEntry(DateTimeOffset.UtcNow, ctx.Caller().Name, "run.start", orderId, bench.Id), ct).ConfigureAwait(false);
            return Results.Ok(new { run.OrderId, run.BenchId });
        });

        // The bench posts stops as it records them; the monitor answers whether the run is still usable.
        api.MapPost("/runs/{orderId}/stops", async (string orderId, StopBatch body, HttpContext ctx, IRdoStore store, MasterData master, RunRegistry runs, RunMonitorClient monitor, CancellationToken ct) =>
        {
            if (ctx.RequireRole("lab") is { } denied)
            {
                return denied;
            }

            if (!runs.Active.TryGetValue(orderId, out var run))
            {
                return Results.NotFound();
            }

            if (run.Stopped is not null)
            {
                return Results.Ok(run.Stopped);
            }

            var order = (await store.ListOpenOrdersAsync(ct).ConfigureAwait(false)).First(o => o.Id == orderId);
            lock (run)
            {
                foreach (var s in body.Stops.Where(s => s.Index >= run.Stops.Count))
                {
                    run.Stops.Add(s);
                }
            }

            var verdict = await monitor.CheckAsync(master.Procedures[order.Procedure], run.Stops, run.Cleared, ct).ConfigureAwait(false);
            if (!verdict.Usable)
            {
                run.Stopped = verdict;
                await store.AppendAuditAsync(new AuditEntry(DateTimeOffset.UtcNow, "monitor", "run.stopped", orderId, verdict.Reason ?? string.Empty), ct).ConfigureAwait(false);
            }

            return Results.Ok(verdict);
        });

        // After checking the bench, an engineer may clear a check for this order and restart.
        api.MapPost("/runs/{orderId}/clear", async (string orderId, ClearCheck body, HttpContext ctx, IRdoStore store, RunRegistry runs, CancellationToken ct) =>
        {
            if (ctx.RequireRole("lab") is { } denied)
            {
                return denied;
            }

            if (!runs.Active.TryGetValue(orderId, out var run))
            {
                return Results.NotFound();
            }

            run.Cleared.Add(body.Check);
            run.Stops.Clear();
            run.Stopped = null;
            await store.AppendAuditAsync(new AuditEntry(DateTimeOffset.UtcNow, ctx.Caller().Name, "run.cleared", orderId, $"{body.Check}: {body.Note}"), ct).ConfigureAwait(false);
            return Results.Ok(new { orderId, cleared = run.Cleared });
        });

        api.MapPost("/runs/{orderId}/complete", async (string orderId, HttpContext ctx, IRdoStore store, MasterData master, RunRegistry runs,
            PlatformSettings settings, IFrictionEvaluator evaluator, LegacyFrictionEvaluator legacy, CancellationToken ct) =>
        {
            if (ctx.RequireRole("lab") is { } denied)
            {
                return denied;
            }

            if (await store.GetResultAsync(orderId, ct).ConfigureAwait(false) is { } done)
            {
                return Results.Ok(ResultView(done));
            }

            if (!runs.Active.TryGetValue(orderId, out var run))
            {
                return Results.NotFound();
            }

            if (run.Stopped is not null)
            {
                return Results.Problem($"The run was stopped: {run.Stopped.Reason}", statusCode: StatusCodes.Status409Conflict);
            }

            var order = (await store.ListOpenOrdersAsync(ct).ConfigureAwait(false)).First(o => o.Id == orderId);
            var proc = master.Procedures[order.Procedure];
            if (run.Stops.Count != proc.TotalStops)
            {
                return Results.Problem($"{run.Stops.Count} of {proc.TotalStops} stops recorded.", statusCode: StatusCodes.Status409Conflict);
            }

            var testRun = new TestRun(orderId, order.Procedure, order.Formulation,
                run.Stops.Select(s => new BrakeStop(s.Index, s.Section, s.SpeedKmh, s.PressureBar, s.InitialTempC, s.Mu)).ToList());
            var formulation = master.Formulations[order.Formulation];
            var engine = settings.Evaluation.Engine;
            FrictionReport report;
            ParityResult? parity = null;
            if (!proc.Sections.Any(s => s.Name.StartsWith("fade", StringComparison.Ordinal)))
            {
                report = new FrictionReport(orderId, 0, 0, 0, 0, true, ["no friction evaluation for this procedure; the run is complete"]);
            }
            else if (engine == "legacy")
            {
                report = legacy.Evaluate(testRun, formulation);
            }
            else
            {
                report = evaluator.Evaluate(testRun, formulation);
                if (engine == "shadow")
                {
                    parity = Parity.Compare(legacy.Evaluate(testRun, formulation), report, settings.Evaluation.ParityTolerance);
                }
            }

            var result = new StoredResult(orderId, engine, JsonSerializer.Serialize(report, JsonDefaults.Options),
                parity is null ? null : JsonSerializer.Serialize(parity, JsonDefaults.Options), DateTimeOffset.UtcNow);
            await store.SaveResultAsync(result, ct).ConfigureAwait(false);
            await store.CloseOrderAsync(orderId, ct).ConfigureAwait(false);
            runs.Active.TryRemove(orderId, out _);
            await store.AppendAuditAsync(new AuditEntry(DateTimeOffset.UtcNow, ctx.Caller().Name, "run.completed", orderId, report.Pass ? "pass" : string.Join("; ", report.Reasons)), ct).ConfigureAwait(false);
            return Results.Ok(ResultView(result));
        });

        // Replays a recorded run through the monitor (for review and for the console), without a bench.
        api.MapPost("/monitor/replay", async (ReplayRun body, MasterData master, RunMonitorClient monitor, CancellationToken ct) =>
        {
            if (!master.Procedures.TryGetValue(body.ProcedureId, out var proc))
            {
                return Results.Problem($"Unknown procedure {body.ProcedureId}.", statusCode: StatusCodes.Status400BadRequest);
            }

            var verdict = await monitor.CheckAsync(proc, body.Stops, [], ct).ConfigureAwait(false);
            return Results.Ok(new { verdict, procedure = proc });
        });

        api.MapGet("/runs/active", (RunRegistry runs) => runs.Active.Values.Select(r => new { r.OrderId, r.BenchId, stops = r.Stops.Count, r.Stopped }));

        api.MapGet("/results", async (IRdoStore store, CancellationToken ct) => (await store.ListResultsAsync(200, ct).ConfigureAwait(false)).Select(ResultView));

        api.MapGet("/results/{orderId}", async (string orderId, IRdoStore store, CancellationToken ct) =>
            await store.GetResultAsync(orderId, ct).ConfigureAwait(false) is { } r ? Results.Ok(ResultView(r)) : Results.NotFound());
    }

    private static object ResultView(StoredResult r) => new
    {
        r.OrderId,
        r.Engine,
        r.CompletedAt,
        Report = JsonSerializer.Deserialize<JsonElement>(r.ReportJson),
        Parity = r.ParityJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(r.ParityJson),
    };
}
