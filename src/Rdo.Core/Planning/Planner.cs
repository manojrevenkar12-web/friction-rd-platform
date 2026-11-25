using Rdo.Core.Domain;

namespace Rdo.Core.Planning;

public sealed record QueuedOrder(
    string Id,
    BenchKind Bench,
    string Fixture,
    double Hours,
    DateTimeOffset ReadyAt,
    DateTimeOffset? Deadline,
    Priority Priority);

public sealed record BenchState(string Id, BenchKind Kind, string? MountedFixture, DateTimeOffset FreeAt);

public sealed record Dispatch(
    string BenchId,
    string OrderId,
    bool Changeover,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Reason);

/// <summary>Decides which queued order a free bench runs next.</summary>
public interface IDispatchPolicy
{
    string Name { get; }

    Dispatch? Choose(BenchState bench, IReadOnlyCollection<QueuedOrder> queue, DateTimeOffset now);
}

/// <summary>Today's practice: first come, first served per bench type, whatever fixture is mounted.</summary>
public sealed class FifoPolicy(PlannerSettings settings) : IDispatchPolicy
{
    public string Name => "fifo";

    public Dispatch? Choose(BenchState bench, IReadOnlyCollection<QueuedOrder> queue, DateTimeOffset now)
    {
        var pick = queue
            .Where(o => o.Bench == bench.Kind && o.ReadyAt <= now)
            .OrderBy(o => o.ReadyAt)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        return pick is null ? null : Planner.Build(bench, pick, now, settings, "first in queue");
    }
}

/// <summary>
/// Deadline-aware and fixture-aware: an order that would miss its deadline goes first (earliest
/// slack); otherwise orders that fit the mounted fixture are preferred, weighed against how long
/// everything has waited and its priority, so a changeover is made only when it is worth it.
/// </summary>
public sealed class SlackAwarePolicy(PlannerSettings settings) : IDispatchPolicy
{
    public string Name => "slack-aware";

    public Dispatch? Choose(BenchState bench, IReadOnlyCollection<QueuedOrder> queue, DateTimeOffset now)
    {
        var eligible = queue.Where(o => o.Bench == bench.Kind && o.ReadyAt <= now).ToList();
        if (eligible.Count == 0)
        {
            return null;
        }

        var urgent = eligible
            .Select(o => (Order: o, Slack: Planner.SlackHours(bench, o, now, settings)))
            .Where(x => x.Slack < settings.UrgentSlackHours)
            .OrderBy(x => x.Slack)
            .ThenBy(x => x.Order.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (urgent.Order is not null)
        {
            return Planner.Build(bench, urgent.Order, now, settings, $"urgent: {urgent.Slack:F0} h of slack");
        }

        var best = eligible
            .Select(o => (Order: o, Score: Score(bench, o, now)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Order.ReadyAt)
            .ThenBy(x => x.Order.Id, StringComparer.Ordinal)
            .First();
        var reason = Planner.NeedsChangeover(bench, best.Order) ? "worth a changeover" : "fixture already mounted";
        return Planner.Build(bench, best.Order, now, settings, reason);
    }

    private double Score(BenchState bench, QueuedOrder o, DateTimeOffset now)
    {
        var waited = (now - o.ReadyAt).TotalHours;
        var score = waited + settings.PriorityWeightHours * (int)o.Priority;
        if (Planner.NeedsChangeover(bench, o))
        {
            score -= settings.AgingHoursPerChangeover;
        }

        return score;
    }
}

public static class Planner
{
    public static IDispatchPolicy Create(string name, PlannerSettings settings) => name switch
    {
        "fifo" => new FifoPolicy(settings),
        "slack-aware" => new SlackAwarePolicy(settings),
        _ => throw new ArgumentException($"unknown dispatch policy {name}", nameof(name)),
    };

    public static bool NeedsChangeover(BenchState bench, QueuedOrder o) =>
        !string.Equals(bench.MountedFixture, o.Fixture, StringComparison.Ordinal);

    public static double SlackHours(BenchState bench, QueuedOrder o, DateTimeOffset now, PlannerSettings s)
    {
        if (o.Deadline is null)
        {
            return double.PositiveInfinity;
        }

        var finish = now.AddHours(o.Hours + (NeedsChangeover(bench, o) ? s.ChangeoverHours : 0));
        return (o.Deadline.Value - finish).TotalHours;
    }

    internal static Dispatch Build(BenchState bench, QueuedOrder o, DateTimeOffset now, PlannerSettings s, string reason)
    {
        var changeover = NeedsChangeover(bench, o);
        var start = now > bench.FreeAt ? now : bench.FreeAt;
        var end = start.AddHours(o.Hours + (changeover ? s.ChangeoverHours : 0));
        return new Dispatch(bench.Id, o.Id, changeover, start, end, reason);
    }

    /// <summary>
    /// A forward plan for the board: hand orders to benches in the order benches become free,
    /// as the policy would, until the queue is empty or the horizon is reached. Nothing is booked.
    /// </summary>
    public static IReadOnlyList<Dispatch> Schedule(
        IDispatchPolicy policy,
        IEnumerable<BenchState> benches,
        IEnumerable<QueuedOrder> queue,
        DateTimeOffset now,
        DateTimeOffset horizon)
    {
        var free = benches.ToDictionary(b => b.Id);
        var open = queue.ToList();
        var plan = new List<Dispatch>();
        var idle = new HashSet<string>(StringComparer.Ordinal);
        while (open.Count > 0)
        {
            var bench = free.Values
                .Where(b => !idle.Contains(b.Id))
                .OrderBy(b => b.FreeAt)
                .ThenBy(b => b.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (bench is null || bench.FreeAt > horizon)
            {
                break;
            }

            var at = bench.FreeAt > now ? bench.FreeAt : now;
            var d = policy.Choose(bench, open, at);
            if (d is null)
            {
                idle.Add(bench.Id);
                continue;
            }

            plan.Add(d);
            var order = open.First(o => o.Id == d.OrderId);
            open.Remove(order);
            free[bench.Id] = bench with { MountedFixture = order.Fixture, FreeAt = d.End };
            idle.Clear();
        }

        return plan;
    }
}
