using System.Text.Json;

namespace Rdo.Core;

/// <summary>The platform's settings (config/platform.json). The simulated world never reads them.</summary>
public sealed record PlatformSettings
{
    public required PlannerSettings Planner { get; init; }
    public required IntakeSettings Intake { get; init; }
    public required EvaluationSettings Evaluation { get; init; }
    public required ServiceSettings Service { get; init; }

    public static PlatformSettings Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PlatformSettings>(json, Json.JsonDefaults.Options)
            ?? throw new InvalidDataException($"{path} is empty");
    }
}

public sealed record PlannerSettings
{
    /// <summary>Hours to change a bench from one fixture to another.</summary>
    public double ChangeoverHours { get; init; }

    /// <summary>An order whose slack is below this many hours is urgent and goes first.</summary>
    public double UrgentSlackHours { get; init; }

    /// <summary>Hours of waiting that count as much as one changeover, so nothing starves.</summary>
    public double AgingHoursPerChangeover { get; init; }

    /// <summary>Weight of priority classes when nothing is urgent, in hours of waiting.</summary>
    public double PriorityWeightHours { get; init; }
}

public sealed record IntakeSettings
{
    /// <summary>Days a homologation request must allow before its deadline to be planned without escalation.</summary>
    public double MinimumLeadDays { get; init; }

    public required string DefaultTimeZone { get; init; }
}

public sealed record EvaluationSettings
{
    /// <summary>Which evaluator answers requests: "legacy", "new" or "shadow" (new answers, legacy is compared).</summary>
    public required string Engine { get; init; }

    /// <summary>Differences up to this size between engines count as rounding, not a disagreement.</summary>
    public double ParityTolerance { get; init; }

    /// <summary>The lowest fade µ accepted, as a share of the formulation's lower µ target.</summary>
    public double FadeFloorShareOfTarget { get; init; }
}

public sealed record ServiceSettings
{
    public required string IntakeModel { get; init; }
    public double InputEurPerMillionTokens { get; init; }
    public double OutputEurPerMillionTokens { get; init; }
    public int RateLimitPerMinute { get; init; }
}
