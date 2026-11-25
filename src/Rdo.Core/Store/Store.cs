using Rdo.Core.Domain;

namespace Rdo.Core.Store;

public sealed record StoredRequest(
    string Id,
    string Text,
    string SubmittedBy,
    DateTimeOffset SubmittedAt,
    string Status,
    string Model,
    string? DraftJson,
    string DecisionJson,
    string? OrderId);

public sealed record StoredResult(string OrderId, string Engine, string ReportJson, string? ParityJson, DateTimeOffset CompletedAt);

public sealed record AuditEntry(DateTimeOffset At, string Actor, string Action, string Subject, string Detail);

/// <summary>Where the platform keeps requests, orders and results. SQL Server in production; memory in tests and the demo.</summary>
public interface IRdoStore
{
    Task<StoredRequest?> GetRequestAsync(string id, CancellationToken ct);

    Task SaveRequestAsync(StoredRequest request, CancellationToken ct);

    Task<IReadOnlyList<StoredRequest>> ListRequestsAsync(int limit, CancellationToken ct);

    /// <summary>Adds the order once. Returns false if an order with this id already exists.</summary>
    Task<bool> TryAddOrderAsync(TestOrder order, CancellationToken ct);

    Task<IReadOnlyList<TestOrder>> ListOpenOrdersAsync(CancellationToken ct);

    Task CloseOrderAsync(string orderId, CancellationToken ct);

    Task SaveResultAsync(StoredResult result, CancellationToken ct);

    Task<StoredResult?> GetResultAsync(string orderId, CancellationToken ct);

    Task<IReadOnlyList<StoredResult>> ListResultsAsync(int limit, CancellationToken ct);

    Task AppendAuditAsync(AuditEntry entry, CancellationToken ct);

    Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct);
}

public sealed class InMemoryStore : IRdoStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, StoredRequest> requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TestOrder> orders = new(StringComparer.Ordinal);
    private readonly HashSet<string> closed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoredResult> results = new(StringComparer.Ordinal);
    private readonly List<AuditEntry> audit = [];

    public Task<StoredRequest?> GetRequestAsync(string id, CancellationToken ct)
    {
        lock (gate)
        {
            return Task.FromResult(requests.GetValueOrDefault(id));
        }
    }

    public Task SaveRequestAsync(StoredRequest request, CancellationToken ct)
    {
        lock (gate)
        {
            requests[request.Id] = request;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredRequest>> ListRequestsAsync(int limit, CancellationToken ct)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<StoredRequest>>(requests.Values.OrderByDescending(r => r.SubmittedAt).Take(limit).ToList());
        }
    }

    public Task<bool> TryAddOrderAsync(TestOrder order, CancellationToken ct)
    {
        lock (gate)
        {
            return Task.FromResult(orders.TryAdd(order.Id, order));
        }
    }

    public Task<IReadOnlyList<TestOrder>> ListOpenOrdersAsync(CancellationToken ct)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<TestOrder>>(orders.Values.Where(o => !closed.Contains(o.Id)).OrderBy(o => o.ReadyAt).ToList());
        }
    }

    public Task CloseOrderAsync(string orderId, CancellationToken ct)
    {
        lock (gate)
        {
            closed.Add(orderId);
        }

        return Task.CompletedTask;
    }

    public Task SaveResultAsync(StoredResult result, CancellationToken ct)
    {
        lock (gate)
        {
            results[result.OrderId] = result;
        }

        return Task.CompletedTask;
    }

    public Task<StoredResult?> GetResultAsync(string orderId, CancellationToken ct)
    {
        lock (gate)
        {
            return Task.FromResult(results.GetValueOrDefault(orderId));
        }
    }

    public Task<IReadOnlyList<StoredResult>> ListResultsAsync(int limit, CancellationToken ct)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<StoredResult>>(results.Values.OrderByDescending(r => r.CompletedAt).Take(limit).ToList());
        }
    }

    public Task AppendAuditAsync(AuditEntry entry, CancellationToken ct)
    {
        lock (gate)
        {
            audit.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<AuditEntry>>(audit.AsEnumerable().Reverse().Take(limit).ToList());
        }
    }
}
