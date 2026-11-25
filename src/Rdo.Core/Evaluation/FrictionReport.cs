using Rdo.Core.Domain;

namespace Rdo.Core.Evaluation;

/// <summary>The friction figures an engineer reads from an effectiveness test, and the verdict against the formulation's target.</summary>
public sealed record FrictionReport(
    string OrderId,
    double MuNominal,
    double MuMinFade,
    double MuMax,
    double SpeedSensitivity,
    bool Pass,
    IReadOnlyList<string> Reasons);

public interface IFrictionEvaluator
{
    string Name { get; }

    FrictionReport Evaluate(TestRun run, Formulation formulation);
}
