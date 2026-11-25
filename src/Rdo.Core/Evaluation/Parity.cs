using Rdo.Core.Domain;

namespace Rdo.Core.Evaluation;

public enum ParityOutcome
{
    Same,
    Rounding,
    Differs,
}

public sealed record ParityResult(string OrderId, ParityOutcome Outcome, bool VerdictChanged, IReadOnlyList<string> Fields);

/// <summary>Runs both evaluators on the same run and says whether they agree (shadow mode).</summary>
public static class Parity
{
    public static ParityResult Compare(FrictionReport current, FrictionReport candidate, double tolerance)
    {
        var fields = new List<string>();
        var rounding = false;
        void Check(string name, double a, double b)
        {
            var d = Math.Abs(a - b);
            if (d > tolerance)
            {
                fields.Add($"{name}: {a:F4} vs {b:F4}");
            }
            else if (d > 1e-12)
            {
                rounding = true;
            }
        }

        Check("muNominal", current.MuNominal, candidate.MuNominal);
        Check("muMinFade", current.MuMinFade, candidate.MuMinFade);
        Check("muMax", current.MuMax, candidate.MuMax);
        Check("speedSensitivity", current.SpeedSensitivity, candidate.SpeedSensitivity);
        var outcome = fields.Count > 0 ? ParityOutcome.Differs : rounding ? ParityOutcome.Rounding : ParityOutcome.Same;
        return new ParityResult(current.OrderId, outcome, current.Pass != candidate.Pass, fields);
    }
}
