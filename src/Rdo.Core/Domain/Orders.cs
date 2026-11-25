namespace Rdo.Core.Domain;

public enum Priority
{
    Exploratory = 0,
    Development = 1,
    Homologation = 2,
}

/// <summary>A structured test order: what the bench will run.</summary>
public sealed record TestOrder(
    string Id,
    string Formulation,
    string Application,
    string Procedure,
    int Samples,
    DateTimeOffset? Deadline,
    Priority Priority,
    DateTimeOffset ReadyAt);

/// <summary>A run on a bench, as stops the dyno recorded.</summary>
public sealed record BrakeStop(int Index, string Section, double SpeedKmh, double PressureBar, double InitialTempC, double Mu);

public sealed record TestRun(string OrderId, string Procedure, string Formulation, IReadOnlyList<BrakeStop> Stops);
