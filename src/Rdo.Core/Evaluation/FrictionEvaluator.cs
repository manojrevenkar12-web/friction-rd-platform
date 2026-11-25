using Rdo.Core.Domain;

namespace Rdo.Core.Evaluation;

/// <summary>
/// The platform's evaluator. Nominal µ is the mean over the characteristic sections after bedding;
/// minimum fade µ is the lowest stop in any fade section; speed sensitivity is the spread of the
/// mean µ per speed in the speed section. Every stop counts at full precision.
/// </summary>
public sealed class FrictionEvaluator(double fadeFloorShareOfTarget) : IFrictionEvaluator
{
    public string Name => "new";

    public FrictionReport Evaluate(TestRun run, Formulation formulation)
    {
        var characteristic = run.Stops.Where(s => s.Section.StartsWith("characteristic", StringComparison.Ordinal)).Select(s => s.Mu).ToList();
        var fade = run.Stops.Where(s => s.Section.StartsWith("fade", StringComparison.Ordinal)).Select(s => s.Mu).ToList();
        var afterBedding = run.Stops.Where(s => !s.Section.StartsWith("burnish", StringComparison.Ordinal)).Select(s => s.Mu).ToList();
        var speed = run.Stops.Where(s => s.Section == "speed").GroupBy(s => s.SpeedKmh).Select(g => g.Average(s => s.Mu)).ToList();
        if (characteristic.Count == 0 || fade.Count == 0)
        {
            throw new InvalidOperationException($"run {run.OrderId} has no characteristic or fade stops");
        }

        var nominal = characteristic.Average();
        var minFade = fade.Min();
        var spread = speed.Count > 1 ? speed.Max() - speed.Min() : 0;
        return Verdict(run.OrderId, nominal, minFade, afterBedding.Max(), spread, formulation, fadeFloorShareOfTarget);
    }

    internal static FrictionReport Verdict(string orderId, double nominal, double minFade, double max, double spread, Formulation f, double fadeShare)
    {
        var reasons = new List<string>();
        if (nominal < f.MuTargetLow || nominal > f.MuTargetHigh)
        {
            reasons.Add($"nominal µ {nominal:F3} outside target {f.MuTargetLow:F2}–{f.MuTargetHigh:F2}");
        }

        var floor = fadeShare * f.MuTargetLow;
        if (minFade < floor)
        {
            reasons.Add($"fade µ {minFade:F3} below {floor:F3}");
        }

        return new FrictionReport(orderId, nominal, minFade, max, spread, reasons.Count == 0, reasons);
    }
}
