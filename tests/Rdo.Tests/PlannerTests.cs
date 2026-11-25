using Rdo.Core.Domain;
using Rdo.Core.Planning;
using Xunit;

namespace Rdo.Tests;

public class PlannerTests
{
    private static readonly DateTimeOffset T = TestData.Now;
    private readonly IDispatchPolicy slack = Planner.Create("slack-aware", TestData.Settings.Planner);
    private readonly IDispatchPolicy fifo = Planner.Create("fifo", TestData.Settings.Planner);

    private static QueuedOrder O(string id, string fixture, double waitedHours, double? dueInHours = null, Priority p = Priority.Development) =>
        new(id, BenchKind.PassengerCar, fixture, 14, T.AddHours(-waitedHours), dueInHours is null ? null : T.AddHours(dueInHours.Value), p);

    private static BenchState Bench(string? fixture) => new("D01", BenchKind.PassengerCar, fixture, T);

    [Fact]
    public void Fifo_takes_the_oldest_whatever_is_mounted()
    {
        var d = fifo.Choose(Bench("FX-02"), [O("a", "FX-01", 30), O("b", "FX-02", 10)], T)!;
        Assert.Equal("a", d.OrderId);
        Assert.True(d.Changeover);
    }

    [Fact]
    public void Slack_aware_keeps_the_mounted_fixture_when_nothing_is_urgent()
    {
        var d = slack.Choose(Bench("FX-02"), [O("a", "FX-01", 12), O("b", "FX-02", 2)], T)!;
        Assert.Equal("b", d.OrderId);
        Assert.False(d.Changeover);
    }

    [Fact]
    public void An_order_about_to_miss_its_deadline_goes_first()
    {
        var d = slack.Choose(Bench("FX-02"), [O("a", "FX-01", 1, dueInHours: 30), O("b", "FX-02", 40)], T)!;
        Assert.Equal("a", d.OrderId);
        Assert.StartsWith("urgent", d.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_waits_eventually_win_over_a_changeover()
    {
        var d = slack.Choose(Bench("FX-02"), [O("a", "FX-01", 200), O("b", "FX-02", 1)], T)!;
        Assert.Equal("a", d.OrderId);
    }

    [Fact]
    public void Orders_for_another_bench_kind_or_not_ready_are_ignored()
    {
        var cv = new QueuedOrder("cv", BenchKind.CommercialVehicle, "FX-09", 18, T.AddHours(-50), null, Priority.Homologation);
        var future = O("later", "FX-02", -5);
        Assert.Null(slack.Choose(Bench("FX-02"), [cv, future], T));
    }

    [Fact]
    public void A_schedule_never_books_a_bench_twice_and_plans_every_order()
    {
        var benches = new[] { new BenchState("D01", BenchKind.PassengerCar, null, T), new BenchState("D02", BenchKind.PassengerCar, "FX-03", T) };
        var orders = Enumerable.Range(0, 12).Select(i => O($"o{i}", $"FX-0{i % 4}", i)).ToList();
        var plan = Planner.Schedule(slack, benches, orders, T, T.AddDays(30));
        Assert.Equal(12, plan.Count);
        foreach (var g in plan.GroupBy(d => d.BenchId))
        {
            var list = g.OrderBy(d => d.Start).ToList();
            for (var i = 1; i < list.Count; i++)
            {
                Assert.True(list[i].Start >= list[i - 1].End);
            }
        }

        var fifoPlan = Planner.Schedule(fifo, benches, orders, T, T.AddDays(30));
        Assert.True(plan.Count(d => d.Changeover) <= fifoPlan.Count(d => d.Changeover));
    }
}
