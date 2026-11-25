using System.Collections.Concurrent;

namespace Rdo.Api.Infrastructure;

public sealed class ActiveRun
{
    public required string OrderId { get; init; }
    public required string BenchId { get; init; }
    public List<StopReading> Stops { get; } = [];
    public HashSet<string> Cleared { get; } = new(StringComparer.Ordinal);
    public MonitorVerdict? Stopped { get; set; }
}

/// <summary>Runs in progress. Bench state for the planning board comes from here.</summary>
public sealed class RunRegistry
{
    public ConcurrentDictionary<string, ActiveRun> Active { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, string> MountedFixture { get; } = new(StringComparer.Ordinal);
}
