using Microsoft.Data.SqlClient;
using Rdo.Core.Domain;
using Rdo.Core.Store;

namespace Rdo.SqlServer;

/// <summary>The platform's store on SQL Server. Every statement is parameterised.</summary>
public sealed class SqlServerStore(string connectionString) : IRdoStore
{
    private const int DuplicateKey = 2627;

    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        var c = new SqlConnection(connectionString);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    public async Task<StoredRequest?> GetRequestAsync(string id, CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT Id, Text, SubmittedBy, SubmittedAt, Status, Model, DraftJson, DecisionJson, OrderId FROM rdo.Request WHERE Id = @id", c);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? ReadRequest(r) : null;
    }

    public async Task SaveRequestAsync(StoredRequest request, CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            MERGE rdo.Request WITH (HOLDLOCK) AS t
            USING (SELECT @id AS Id) AS s ON t.Id = s.Id
            WHEN MATCHED THEN UPDATE SET Text = @text, Status = @status, Model = @model, DraftJson = @draft, DecisionJson = @decision, OrderId = @order
            WHEN NOT MATCHED THEN INSERT (Id, Text, SubmittedBy, SubmittedAt, Status, Model, DraftJson, DecisionJson, OrderId)
                VALUES (@id, @text, @by, @at, @status, @model, @draft, @decision, @order);
            """, c);
        cmd.Parameters.AddWithValue("@id", request.Id);
        cmd.Parameters.AddWithValue("@text", request.Text);
        cmd.Parameters.AddWithValue("@by", request.SubmittedBy);
        cmd.Parameters.AddWithValue("@at", request.SubmittedAt);
        cmd.Parameters.AddWithValue("@status", request.Status);
        cmd.Parameters.AddWithValue("@model", request.Model);
        cmd.Parameters.AddWithValue("@draft", (object?)request.DraftJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@decision", request.DecisionJson);
        cmd.Parameters.AddWithValue("@order", (object?)request.OrderId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredRequest>> ListRequestsAsync(int limit, CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT TOP (@n) Id, Text, SubmittedBy, SubmittedAt, Status, Model, DraftJson, DecisionJson, OrderId FROM rdo.Request ORDER BY SubmittedAt DESC", c);
        cmd.Parameters.AddWithValue("@n", limit);
        var list = new List<StoredRequest>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(ReadRequest(r));
        }

        return list;
    }

    public async Task<bool> TryAddOrderAsync(TestOrder order, CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            INSERT INTO rdo.TestOrder (Id, Formulation, Application, ProcedureId, Samples, Deadline, Priority, ReadyAt)
            VALUES (@id, @f, @a, @p, @s, @d, @prio, @ready)
            """, c);
        cmd.Parameters.AddWithValue("@id", order.Id);
        cmd.Parameters.AddWithValue("@f", order.Formulation);
        cmd.Parameters.AddWithValue("@a", order.Application);
        cmd.Parameters.AddWithValue("@p", order.Procedure);
        cmd.Parameters.AddWithValue("@s", order.Samples);
        cmd.Parameters.AddWithValue("@d", (object?)order.Deadline ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prio", order.Priority.ToString());
        cmd.Parameters.AddWithValue("@ready", order.ReadyAt);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (SqlException e) when (e.Number == DuplicateKey)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<TestOrder>> ListOpenOrdersAsync(CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT Id, Formulation, Application, ProcedureId, Samples, Deadline, Priority, ReadyAt FROM rdo.TestOrder WHERE ClosedAt IS NULL ORDER BY ReadyAt", c);
        var list = new List<TestOrder>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new TestOrder(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetDateTimeOffset(5), Enum.Parse<Priority>(r.GetString(6), ignoreCase: true), r.GetDateTimeOffset(7)));
        }

        return list;
    }

    public async Task CloseOrderAsync(string orderId, CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("UPDATE rdo.TestOrder SET ClosedAt = SYSDATETIMEOFFSET() WHERE Id = @id AND ClosedAt IS NULL", c);
        cmd.Parameters.AddWithValue("@id", orderId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveResultAsync(StoredResult result, CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM rdo.Result WHERE OrderId = @id)
                INSERT INTO rdo.Result (OrderId, Engine, ReportJson, ParityJson, CompletedAt) VALUES (@id, @engine, @report, @parity, @at)
            """, c);
        cmd.Parameters.AddWithValue("@id", result.OrderId);
        cmd.Parameters.AddWithValue("@engine", result.Engine);
        cmd.Parameters.AddWithValue("@report", result.ReportJson);
        cmd.Parameters.AddWithValue("@parity", (object?)result.ParityJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at", result.CompletedAt);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<StoredResult?> GetResultAsync(string orderId, CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT OrderId, Engine, ReportJson, ParityJson, CompletedAt FROM rdo.Result WHERE OrderId = @id", c);
        cmd.Parameters.AddWithValue("@id", orderId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? ReadResult(r) : null;
    }

    public async Task<IReadOnlyList<StoredResult>> ListResultsAsync(int limit, CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT TOP (@n) OrderId, Engine, ReportJson, ParityJson, CompletedAt FROM rdo.Result ORDER BY CompletedAt DESC", c);
        cmd.Parameters.AddWithValue("@n", limit);
        var list = new List<StoredResult>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(ReadResult(r));
        }

        return list;
    }

    public async Task AppendAuditAsync(AuditEntry entry, CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("INSERT INTO rdo.Audit (At, Actor, Action, Subject, Detail) VALUES (@at, @actor, @action, @subject, @detail)", c);
        cmd.Parameters.AddWithValue("@at", entry.At);
        cmd.Parameters.AddWithValue("@actor", entry.Actor);
        cmd.Parameters.AddWithValue("@action", entry.Action);
        cmd.Parameters.AddWithValue("@subject", entry.Subject);
        cmd.Parameters.AddWithValue("@detail", entry.Detail.Length > 2000 ? entry.Detail[..2000] : entry.Detail);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEntry>> ListAuditAsync(int limit, CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT TOP (@n) At, Actor, Action, Subject, Detail FROM rdo.Audit ORDER BY Id DESC", c);
        cmd.Parameters.AddWithValue("@n", limit);
        var list = new List<AuditEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new AuditEntry(r.GetDateTimeOffset(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        }

        return list;
    }

    /// <summary>Keeps rdo.Application in step with the master data (the legacy bridge joins on it).</summary>
    public async Task SyncApplicationsAsync(MasterData master, CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        foreach (var a in master.Applications.Values)
        {
            await using var cmd = new SqlCommand("""
                MERGE rdo.Application AS t USING (SELECT @id AS Id) AS s ON t.Id = s.Id
                WHEN MATCHED THEN UPDATE SET Vehicle = @v, Axle = @axle, Fixture = @fx, VehicleClass = @cls
                WHEN NOT MATCHED THEN INSERT (Id, Vehicle, Axle, Fixture, VehicleClass) VALUES (@id, @v, @axle, @fx, @cls);
                """, c);
            cmd.Parameters.AddWithValue("@id", a.Id);
            cmd.Parameters.AddWithValue("@v", a.Vehicle);
            cmd.Parameters.AddWithValue("@axle", a.Axle);
            cmd.Parameters.AddWithValue("@fx", a.Fixture);
            cmd.Parameters.AddWithValue("@cls", a.VehicleClass.ToString());
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<int> ImportLegacyOpenRequestsAsync(CancellationToken ct)
    {
        await using var c = await Open(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("rdo.ImportLegacyOpenRequests", c) { CommandType = System.Data.CommandType.StoredProcedure };
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static StoredRequest ReadRequest(SqlDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetDateTimeOffset(3), r.GetString(4), r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6), r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8));

    private static StoredResult ReadResult(SqlDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetDateTimeOffset(4));
}
