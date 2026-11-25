using System.Text.Json;
using Rdo.Core.Intake;
using Rdo.Core.Json;
using Rdo.Core.Store;

namespace Rdo.Api.Infrastructure;

/// <summary>Loads the demo's request texts (config/demo/requests.json) through the real intake path.</summary>
public static class DemoSeed
{
    private sealed record DemoRequest(string Id, string Text, string SubmittedBy);

    public static async Task LoadAsync(IServiceProvider services, string dir, string count)
    {
        var path = Path.Combine(dir, "requests.json");
        if (!File.Exists(path))
        {
            return;
        }

        var agent = services.GetRequiredService<IntakeAgent>();
        var store = services.GetRequiredService<IRdoStore>();
        var requests = JsonSerializer.Deserialize<List<DemoRequest>>(await File.ReadAllTextAsync(path).ConfigureAwait(false), JsonDefaults.Options) ?? [];
        var n = int.TryParse(count, out var c) ? c : requests.Count;
        foreach (var r in requests.Take(n))
        {
            var now = DateTimeOffset.UtcNow;
            var result = await agent.HandleAsync(r.Id, r.Text, now, CancellationToken.None).ConfigureAwait(false);
            if (result.Decision.Order is not null)
            {
                await store.TryAddOrderAsync(result.Decision.Order, CancellationToken.None).ConfigureAwait(false);
            }

            var status = result.Decision.Status switch
            {
                IntakeStatus.Accepted => "accepted",
                IntakeStatus.Rejected => "rejected",
                _ => "needsClarification",
            };
            await store.SaveRequestAsync(new StoredRequest(r.Id, r.Text, r.SubmittedBy, now, status, result.Model,
                result.Draft is null ? null : JsonSerializer.Serialize(result.Draft, JsonDefaults.Options),
                JsonSerializer.Serialize(result.Decision, JsonDefaults.Options), result.Decision.Order?.Id), CancellationToken.None).ConfigureAwait(false);
        }
    }
}
