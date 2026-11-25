using System.Net.Http.Json;
using System.Text.Json;
using Rdo.Core.Domain;
using Rdo.Core.Json;

namespace Rdo.Api.Infrastructure;

public sealed record StopReading(int Index, string Section, double SpeedKmh, double PressureBar, double InitialTempC, double CoolingSeconds, double Mu);

public sealed record MonitorVerdict(bool Usable, int? StopAt, string? Check, string? Reason, bool Available);

/// <summary>
/// Calls the Python dyno analytics service. If it cannot be reached, the run goes on and the verdict
/// says the monitor was unavailable: an outage of the monitor never stops a bench.
/// </summary>
public sealed partial class RunMonitorClient(HttpClient http, ILogger<RunMonitorClient> log)
{
    public async Task<MonitorVerdict> CheckAsync(Procedure procedure, IReadOnlyList<StopReading> stops, IEnumerable<string> cleared, CancellationToken ct)
    {
        try
        {
            var body = new { procedure = new { id = procedure.Id, sections = procedure.Sections }, stops, cleared };
            using var res = await http.PostAsJsonAsync("/v1/runs/check", body, JsonDefaults.Options, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var v = await res.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Options, ct).ConfigureAwait(false);
            return new MonitorVerdict(
                v.GetProperty("usable").GetBoolean(),
                v.GetProperty("stopAt").ValueKind == JsonValueKind.Number ? v.GetProperty("stopAt").GetInt32() : null,
                v.GetProperty("check").GetString(),
                v.GetProperty("reason").GetString(),
                true);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            MonitorUnavailable(log, e, procedure.Id);
            return new MonitorVerdict(true, null, null, "the run monitor was unavailable; this run is reviewed after it completes", false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run monitor unavailable; run on {Procedure} continues unchecked")]
    private static partial void MonitorUnavailable(ILogger logger, Exception error, string procedure);
}
