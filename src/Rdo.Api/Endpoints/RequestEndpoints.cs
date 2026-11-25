using System.Text.Json;
using Rdo.Core.Intake;
using Rdo.Core.Json;
using Rdo.Core.Store;

namespace Rdo.Api.Endpoints;

public sealed record SubmitRequest(string Text);

public sealed record AnswerRequest(string Text);

public static class RequestEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        // An engineer submits a request. The Idempotency-Key makes a retried submission return the first outcome.
        api.MapPost("/requests", async (SubmitRequest body, HttpContext ctx, IntakeAgent agent, IRdoStore store, CancellationToken ct) =>
        {
            if (ctx.RequireRole("engineer") is { } denied)
            {
                return denied;
            }

            if (string.IsNullOrWhiteSpace(body.Text) || body.Text.Length > 4000)
            {
                return Results.Problem("A request needs between 1 and 4000 characters.", statusCode: StatusCodes.Status400BadRequest);
            }

            var key = ctx.Request.Headers["Idempotency-Key"].ToString();
            var id = string.IsNullOrEmpty(key) ? Guid.NewGuid().ToString("N")[..10] : Sanitize(key);
            if (await store.GetRequestAsync(id, ct).ConfigureAwait(false) is { } existing)
            {
                return Results.Ok(View(existing));
            }

            var stored = await Handle(id, body.Text, ctx, agent, store, ct).ConfigureAwait(false);
            return Results.Created($"/api/requests/{id}", View(stored));
        });

        // The engineer answers the questions; the request is read again with the answer added.
        api.MapPost("/requests/{id}/answer", async (string id, AnswerRequest body, HttpContext ctx, IntakeAgent agent, IRdoStore store, CancellationToken ct) =>
        {
            if (ctx.RequireRole("engineer") is { } denied)
            {
                return denied;
            }

            var existing = await store.GetRequestAsync(id, ct).ConfigureAwait(false);
            if (existing is null)
            {
                return Results.NotFound();
            }

            if (existing.Status != "needsClarification")
            {
                return Results.Problem($"Request {id} is {existing.Status}; only open questions can be answered.", statusCode: StatusCodes.Status409Conflict);
            }

            var stored = await Handle(id, existing.Text + "\n" + body.Text, ctx, agent, store, ct).ConfigureAwait(false);
            return Results.Ok(View(stored));
        });

        api.MapGet("/requests", async (IRdoStore store, CancellationToken ct) =>
            (await store.ListRequestsAsync(200, ct).ConfigureAwait(false)).Select(View));

        api.MapGet("/requests/{id}", async (string id, IRdoStore store, CancellationToken ct) =>
            await store.GetRequestAsync(id, ct).ConfigureAwait(false) is { } r ? Results.Ok(View(r)) : Results.NotFound());

        api.MapGet("/audit", async (IRdoStore store, CancellationToken ct) => await store.ListAuditAsync(100, ct).ConfigureAwait(false));
    }

    private static async Task<StoredRequest> Handle(string id, string text, HttpContext ctx, IntakeAgent agent, IRdoStore store, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var result = await agent.HandleAsync(id, text, now, ct).ConfigureAwait(false);
        var d = result.Decision;
        var status = d.Status switch
        {
            IntakeStatus.Accepted => "accepted",
            IntakeStatus.Rejected => "rejected",
            _ => "needsClarification",
        };
        if (d.Order is not null)
        {
            await store.TryAddOrderAsync(d.Order, ct).ConfigureAwait(false);
        }

        var stored = new StoredRequest(id, text, ctx.Caller().Name, now, status, result.Model,
            result.Draft is null ? null : JsonSerializer.Serialize(result.Draft, JsonDefaults.Options),
            JsonSerializer.Serialize(d, JsonDefaults.Options), d.Order?.Id);
        await store.SaveRequestAsync(stored, ct).ConfigureAwait(false);
        await store.AppendAuditAsync(new AuditEntry(now, ctx.Caller().Name, $"request.{status}", id, string.Join("; ", d.Problems.Concat(d.Questions))), ct).ConfigureAwait(false);
        return stored;
    }

    private static string Sanitize(string key) => new(key.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(40).ToArray());

    public static object View(StoredRequest r) => new
    {
        r.Id,
        r.Text,
        r.SubmittedBy,
        r.SubmittedAt,
        r.Status,
        r.Model,
        r.OrderId,
        Draft = r.DraftJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(r.DraftJson),
        Decision = JsonSerializer.Deserialize<JsonElement>(r.DecisionJson),
    };
}
