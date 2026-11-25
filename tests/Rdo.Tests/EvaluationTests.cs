using Rdo.Core.Domain;
using Rdo.Core.Evaluation;
using Rdo.Legacy;
using Xunit;

namespace Rdo.Tests;

public class EvaluationTests
{
    private static readonly Formulation F = new("FM-1000", FormulationStatus.ReleasedForTest, ["A01"], 0.38, 0.48);
    private readonly FrictionEvaluator current = new(0.7);
    private readonly LegacyFrictionEvaluator legacy = new(0.7);

    private static TestRun Run(double lastFadeMu, double characteristic = 0.43)
    {
        var stops = new List<BrakeStop>();
        var i = 0;
        void Add(string section, int n, Func<int, double> mu, double speed = 80)
        {
            for (var j = 0; j < n; j++)
            {
                stops.Add(new BrakeStop(i++, section, speed, 40, 100, mu(j)));
            }
        }

        Add("burnish", 4, j => 0.38 + 0.01 * j);
        Add("characteristic-1", 6, _ => characteristic);
        Add("speed", 3, _ => 0.44, 40);
        Add("speed", 3, _ => 0.42, 160);
        Add("fade-1", 5, j => j == 4 ? lastFadeMu : 0.40 - 0.01 * j);
        Add("characteristic-2", 6, _ => characteristic);
        return new TestRun("TO-1", "AKM", F.Code, stops);
    }

    [Fact]
    public void The_new_evaluator_counts_every_fade_stop()
    {
        var r = current.Evaluate(Run(lastFadeMu: 0.20), F);
        Assert.Equal(0.20, r.MuMinFade, 6);
        Assert.False(r.Pass);
        Assert.Contains(r.Reasons, x => x.StartsWith("fade", StringComparison.Ordinal));
    }

    [Fact]
    public void The_legacy_fade_loop_misses_the_last_stop_and_parity_says_so()
    {
        var run = Run(lastFadeMu: 0.20);
        var old = legacy.Evaluate(run, F);
        var now = current.Evaluate(run, F);
        Assert.True(old.Pass);
        var p = Parity.Compare(old, now, 0.005);
        Assert.Equal(ParityOutcome.Differs, p.Outcome);
        Assert.True(p.VerdictChanged);
        Assert.Contains(p.Fields, f => f.StartsWith("muMinFade", StringComparison.Ordinal));
    }

    [Fact]
    public void Rounding_differences_are_reported_as_rounding()
    {
        var run = Run(lastFadeMu: 0.39, characteristic: 0.4349);
        var p = Parity.Compare(legacy.Evaluate(run, F), current.Evaluate(run, F), 0.005);
        Assert.Equal(ParityOutcome.Rounding, p.Outcome);
        Assert.False(p.VerdictChanged);
    }

    [Fact]
    public void Speed_sensitivity_is_the_spread_of_mean_mu_per_speed() =>
        Assert.Equal(0.02, current.Evaluate(Run(0.39), F).SpeedSensitivity, 6);

    [Fact]
    public void A_run_without_fade_stops_is_refused() =>
        Assert.Throws<InvalidOperationException>(() => current.Evaluate(new TestRun("x", "AKM", F.Code, [new BrakeStop(0, "characteristic-1", 80, 40, 100, 0.4)]), F));
}
