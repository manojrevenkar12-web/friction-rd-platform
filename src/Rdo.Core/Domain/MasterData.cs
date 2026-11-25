namespace Rdo.Core.Domain;

/// <summary>Kind of dynamometer a procedure needs.</summary>
public enum BenchKind
{
    PassengerCar,
    CommercialVehicle,
    Nvh,
}

/// <summary>Lifecycle of a friction formulation (a brake-pad mix).</summary>
public enum FormulationStatus
{
    InDevelopment,
    ReleasedForTest,
    Obsolete,
}

public sealed record Bench(string Id, BenchKind Kind);

/// <summary>A vehicle brake application: the caliper and disc a pad is made for, mounted on a dyno with a fixture.
/// <see cref="VehicleClass"/> is PassengerCar or CommercialVehicle.</summary>
public sealed record Application(string Id, string Vehicle, string Axle, string Fixture, BenchKind VehicleClass);

public sealed record Formulation(
    string Code,
    FormulationStatus Status,
    IReadOnlyList<string> Applications,
    double MuTargetLow,
    double MuTargetHigh);

/// <summary>A section of a test procedure, for example the fade section of an AK Master style test.</summary>
public sealed record ProcedureSection(string Name, int Stops, double PressureBar, double SpeedKmh, double InitialTempC);

public sealed record Procedure(
    string Id,
    string Name,
    BenchKind Bench,
    BenchKind VehicleClass,
    double Hours,
    int MinSamples,
    string? CustomerVariantOf,
    string? Customer,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<ProcedureSection> Sections)
{
    public int TotalStops => Sections.Sum(s => s.Stops);
}

/// <summary>Everything the lab's master data holds. Loaded once; immutable.</summary>
public sealed class MasterData
{
    public MasterData(
        IReadOnlyList<Bench> benches,
        IReadOnlyList<Application> applications,
        IReadOnlyList<Formulation> formulations,
        IReadOnlyList<Procedure> procedures)
    {
        Benches = benches;
        Applications = applications.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        Formulations = formulations.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);
        Procedures = procedures.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<Bench> Benches { get; }
    public IReadOnlyDictionary<string, Application> Applications { get; }
    public IReadOnlyDictionary<string, Formulation> Formulations { get; }
    public IReadOnlyDictionary<string, Procedure> Procedures { get; }
}
