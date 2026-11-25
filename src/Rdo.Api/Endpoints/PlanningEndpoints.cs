using Rdo.Api.Infrastructure;
using Rdo.Core;
using Rdo.Core.Domain;
using Rdo.Core.Planning;
using Rdo.Core.Store;

namespace Rdo.Api.Endpoints;

public static class PlanningEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/orders", async (IRdoStore store, CancellationToken ct) => await store.ListOpenOrdersAsync(ct).ConfigureAwait(false));

        // The board: how the open orders would run on the benches, by the chosen policy. Nothing is booked.
        api.MapGet("/schedule", async (string? policy, int? hours, IRdoStore store, MasterData master, PlatformSettings settings, RunRegistry runs, CancellationToken ct) =>
        {
            var name = policy ?? "slack-aware";
            if (name is not ("slack-aware" or "fifo"))
            {
                return Results.Problem("policy is slack-aware or fifo", statusCode: StatusCodes.Status400BadRequest);
            }

            var now = DateTimeOffset.UtcNow;
            var open = await store.ListOpenOrdersAsync(ct).ConfigureAwait(false);
            var queued = open.Where(o => !runs.Active.ContainsKey(o.Id)).Select(o =>
            {
                var p = master.Procedures[o.Procedure];
                return new QueuedOrder(o.Id, p.Bench, master.Applications[o.Application].Fixture, p.Hours, o.ReadyAt < now ? now : o.ReadyAt, o.Deadline, o.Priority);
            }).ToList();
            var benches = master.Benches.Select(b => new BenchState(b.Id, b.Kind, runs.MountedFixture.GetValueOrDefault(b.Id), now)).ToList();
            var plan = Planner.Schedule(Planner.Create(name, settings.Planner), benches, queued, now, now.AddHours(hours ?? 24 * 14));
            var late = plan.Count(d => open.First(o => o.Id == d.OrderId).Deadline is { } due && d.End > due);
            return Results.Ok(new
            {
                policy = name,
                generatedAt = now,
                changeovers = plan.Count(d => d.Changeover),
                late,
                unplanned = queued.Count - plan.Count,
                dispatches = plan,
            });
        });
    }
}
