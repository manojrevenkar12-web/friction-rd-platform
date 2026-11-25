using Rdo.Core.Domain;
using Rdo.Core.Evaluation;

namespace Rdo.Legacy;

/// <summary>
/// The evaluation as the legacy desktop application computes it, ported line by line so the new
/// evaluator can be checked against it (characterization). Two behaviours are kept on purpose:
/// each stop's µ is rounded to two decimals before averaging, and the fade loop stops one stop early.
/// The parity report lists where they change a result; neither is copied into the new evaluator.
/// </summary>
public sealed class LegacyFrictionEvaluator(double fadeFloorShareOfTarget) : IFrictionEvaluator
{
    public string Name => "legacy";

    public FrictionReport Evaluate(TestRun run, Formulation formulation)
    {
        double sum = 0;
        var n = 0;
        double minFade = 9.99;
        double max = 0;
        var fadeStops = new List<double>();
        var bySpeed = new Dictionary<double, (double Sum, int N)>();
        foreach (var s in run.Stops)
        {
            var mu = Math.Round(s.Mu, 2, MidpointRounding.AwayFromZero);
            if (s.Section.StartsWith("characteristic", StringComparison.Ordinal))
            {
                sum += mu;
                n++;
            }

            if (s.Section.StartsWith("fade", StringComparison.Ordinal))
            {
                fadeStops.Add(s.Mu);
            }

            if (!s.Section.StartsWith("burnish", StringComparison.Ordinal) && s.Mu > max)
            {
                max = s.Mu;
            }

            if (s.Section == "speed")
            {
                bySpeed.TryGetValue(s.SpeedKmh, out var acc);
                bySpeed[s.SpeedKmh] = (acc.Sum + mu, acc.N + 1);
            }
        }

        for (var i = 0; i < fadeStops.Count - 1; i++)
        {
            if (fadeStops[i] < minFade)
            {
                minFade = fadeStops[i];
            }
        }

        if (n == 0 || fadeStops.Count == 0)
        {
            throw new InvalidOperationException($"run {run.OrderId} has no characteristic or fade stops");
        }

        var means = bySpeed.Values.Select(v => v.Sum / v.N).ToList();
        var spread = means.Count > 1 ? means.Max() - means.Min() : 0;
        return FrictionEvaluator.Verdict(run.OrderId, sum / n, minFade, max, spread, formulation, fadeFloorShareOfTarget);
    }
}
