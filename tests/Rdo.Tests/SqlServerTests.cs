using Microsoft.Data.SqlClient;
using Rdo.Core.Domain;
using Rdo.Core.Store;
using Rdo.SqlServer;
using Xunit;

namespace Rdo.Tests;

/// <summary>Runs only when RDO_TEST_SQLSERVER points at a SQL Server (CI starts one).</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RDO_TEST_SQLSERVER")))
        {
            Skip = "set RDO_TEST_SQLSERVER to run against SQL Server";
        }
    }
}

public class SqlServerTests
{
    private static string Cs => Environment.GetEnvironmentVariable("RDO_TEST_SQLSERVER")!;

    [SqlServerFact]
    public async Task Migrations_apply_once_and_the_store_round_trips()
    {
        Migrator.Migrate(Cs);
        Assert.Empty(Migrator.Migrate(Cs));
        var store = new SqlServerStore(Cs);
        await store.SyncApplicationsAsync(TestData.Master, CancellationToken.None);
        var (f, a) = TestData.ReleasedPassengerCar();
        var id = $"TO-{Guid.NewGuid():N}"[..20];
        var order = new TestOrder(id, f.Code, a.Id, "AKM", 2, TestData.Now.AddDays(30), Priority.Homologation, TestData.Now);
        Assert.True(await store.TryAddOrderAsync(order, CancellationToken.None));
        Assert.False(await store.TryAddOrderAsync(order, CancellationToken.None));
        Assert.Contains(await store.ListOpenOrdersAsync(CancellationToken.None), o => o.Id == id && o.Priority == Priority.Homologation);
        await store.SaveRequestAsync(new StoredRequest(id, "text", "anna", TestData.Now, "accepted", "m", null, "{}", id), CancellationToken.None);
        Assert.Equal("anna", (await store.GetRequestAsync(id, CancellationToken.None))!.SubmittedBy);
        await store.SaveResultAsync(new StoredResult(id, "new", "{}", null, TestData.Now), CancellationToken.None);
        await store.SaveResultAsync(new StoredResult(id, "legacy", "{}", null, TestData.Now), CancellationToken.None);
        Assert.Equal("new", (await store.GetResultAsync(id, CancellationToken.None))!.Engine);
        await store.CloseOrderAsync(id, CancellationToken.None);
        Assert.DoesNotContain(await store.ListOpenOrdersAsync(CancellationToken.None), o => o.Id == id);
    }

    [SqlServerFact]
    public async Task The_legacy_application_sees_platform_orders_and_its_open_requests_are_imported_once()
    {
        Migrator.Migrate(Cs);
        var store = new SqlServerStore(Cs);
        await store.SyncApplicationsAsync(TestData.Master, CancellationToken.None);
        var (f, a) = TestData.ReleasedPassengerCar();
        await using var c = new SqlConnection(Cs);
        await c.OpenAsync();
        await using (var ins = new SqlCommand("INSERT INTO dbo.TestRequest (Mix, Vehicle, Axle, TestCode, Samples, CreatedBy) VALUES (@m, @v, @x, 'AKM', 1, 'legacy')", c))
        {
            ins.Parameters.AddWithValue("@m", f.Code);
            ins.Parameters.AddWithValue("@v", a.Vehicle);
            ins.Parameters.AddWithValue("@x", a.Axle == "front" ? "F" : "R");
            await ins.ExecuteNonQueryAsync();
        }

        Assert.True(await store.ImportLegacyOpenRequestsAsync(CancellationToken.None) >= 1);
        Assert.Equal(0, await store.ImportLegacyOpenRequestsAsync(CancellationToken.None));
        await using var view = new SqlCommand("SELECT COUNT(*) FROM dbo.vPlatformOrdersAsLegacy WHERE PlatformOrderId LIKE 'LEG-%' AND Vehicle = @v", c);
        view.Parameters.AddWithValue("@v", a.Vehicle);
        Assert.True((int)(await view.ExecuteScalarAsync())! >= 1);
    }
}
