using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Rdo.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public ApiFactory()
    {
        // No analytics service runs in the tests: the monitor is unavailable on purpose.
        Environment.SetEnvironmentVariable("RDO_ANALYTICS_URL", "http://127.0.0.1:9");
        Environment.SetEnvironmentVariable("RDO_API_KEYS", "engineer:anna:e-key,lab:jens:l-key");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        Environment.SetEnvironmentVariable("RDO_SQLSERVER", null);
    }
}

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient Client(string? key)
    {
        var c = factory.CreateClient();
        if (key is not null)
        {
            c.DefaultRequestHeaders.Add("X-Api-Key", key);
        }

        return c;
    }

    private static string RequestText()
    {
        var (f, a) = TestData.ReleasedPassengerCar();
        return $"Hi lab, please run AK Master on {f.Code} for the {a.Vehicle} {a.Axle} axle. 1 pad set is ready. Thanks, Anna";
    }

    [Fact]
    public async Task Calls_without_a_known_key_are_refused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client("nope").GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("l-key").PostAsJsonAsync("/api/requests", new { text = "x" })).StatusCode);
    }

    [Fact]
    public async Task A_retried_submission_returns_the_first_outcome()
    {
        var c = Client("e-key");
        c.DefaultRequestHeaders.Add("Idempotency-Key", "retry-1");
        var first = await c.PostAsJsonAsync("/api/requests", new { text = RequestText() });
        var again = await c.PostAsJsonAsync("/api/requests", new { text = RequestText() });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("accepted", a.GetProperty("status").GetString());
        var orders = await Client("l-key").GetFromJsonAsync<JsonElement>("/api/orders");
        Assert.Equal(1, orders.EnumerateArray().Count(o => o.GetProperty("id").GetString() == "TO-retry-1"));
    }

    [Fact]
    public async Task A_run_goes_on_when_the_monitor_is_down()
    {
        var e = Client("e-key");
        e.DefaultRequestHeaders.Add("Idempotency-Key", "run-1");
        var created = await (await e.PostAsJsonAsync("/api/requests", new { text = RequestText() })).Content.ReadFromJsonAsync<JsonElement>();
        var orderId = created.GetProperty("orderId").GetString();
        var lab = Client("l-key");
        Assert.True((await lab.PostAsJsonAsync($"/api/runs/{orderId}/start", new { benchId = "D01" })).IsSuccessStatusCode);
        var proc = TestData.Master.Procedures["AKM"];
        var stops = new List<object>();
        var i = 0;
        foreach (var s in proc.Sections)
        {
            for (var j = 0; j < s.Stops; j++)
            {
                stops.Add(new { index = i++, section = s.Name, speedKmh = s.SpeedKmh, pressureBar = s.PressureBar, initialTempC = s.InitialTempC, coolingSeconds = 90.0, mu = 0.42 });
            }
        }

        var verdict = await (await lab.PostAsJsonAsync($"/api/runs/{orderId}/stops", new { stops })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verdict.GetProperty("usable").GetBoolean());
        Assert.False(verdict.GetProperty("available").GetBoolean());
        var done = await lab.PostAsync($"/api/runs/{orderId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var result = await done.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("shadow", result.GetProperty("engine").GetString());
        Assert.Equal(0.42, result.GetProperty("report").GetProperty("muNominal").GetDouble(), 6);
        Assert.NotEqual(JsonValueKind.Null, result.GetProperty("parity").ValueKind);
    }

    [Fact]
    public async Task The_board_plans_open_orders_by_policy()
    {
        var res = await Client("l-key").GetFromJsonAsync<JsonElement>("/api/schedule?policy=fifo");
        Assert.Equal("fifo", res.GetProperty("policy").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Client("l-key").GetAsync("/api/schedule?policy=random")).StatusCode);
    }
}
