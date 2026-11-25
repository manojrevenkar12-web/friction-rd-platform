using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Rdo.Api.Endpoints;
using Rdo.Api.Infrastructure;
using Rdo.Core;
using Rdo.Core.Domain;
using Rdo.Core.Evaluation;
using Rdo.Core.Intake;
using Rdo.Core.Json;
using Rdo.Core.Store;
using Rdo.Legacy;

var builder = WebApplication.CreateBuilder(args);
var root = Paths.Root();
var settings = PlatformSettings.Load(Path.Combine(root, "config", "platform.json"));
var master = MasterDataLoader.FromFile(Environment.GetEnvironmentVariable("RDO_MASTER") ?? Path.Combine(root, "config", "demo", "master-data.json"));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    foreach (var c in JsonDefaults.Options.Converters)
    {
        o.SerializerOptions.Converters.Add(c);
    }
});
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(master);
builder.Services.AddSingleton(new ApiKeys(Environment.GetEnvironmentVariable("RDO_API_KEYS") ?? "engineer:anna:engineer-key,lab:jens:lab-key"));
builder.Services.AddSingleton<RunRegistry>();
builder.Services.AddSingleton(new IntakeValidator(master, settings.Intake));
builder.Services.AddSingleton<IFrictionEvaluator>(_ => new FrictionEvaluator(settings.Evaluation.FadeFloorShareOfTarget));
builder.Services.AddSingleton(_ => new LegacyFrictionEvaluator(settings.Evaluation.FadeFloorShareOfTarget));
builder.Services.AddHttpClient<RunMonitorClient>(c =>
{
    c.BaseAddress = new Uri(Environment.GetEnvironmentVariable("RDO_ANALYTICS_URL") ?? "http://localhost:8100");
    c.Timeout = TimeSpan.FromSeconds(5);
});

var anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (!string.IsNullOrEmpty(anthropicKey))
{
    builder.Services.AddHttpClient("anthropic", c => c.Timeout = TimeSpan.FromSeconds(30));
    builder.Services.AddSingleton<IIntakeModel>(sp => new ClaudeIntakeModel(sp.GetRequiredService<IHttpClientFactory>().CreateClient("anthropic"), anthropicKey, settings.Service.IntakeModel, master));
}
else
{
    builder.Services.AddSingleton<IIntakeModel>(new KeywordIntakeModel(master));
}

builder.Services.AddSingleton<IntakeAgent>();

#if WITH_SQLSERVER
var sql = Environment.GetEnvironmentVariable("RDO_SQLSERVER");
if (!string.IsNullOrEmpty(sql))
{
    Rdo.SqlServer.Migrator.Migrate(sql);
    var sqlStore = new Rdo.SqlServer.SqlServerStore(sql);
    await sqlStore.SyncApplicationsAsync(master, CancellationToken.None).ConfigureAwait(false);
    builder.Services.AddSingleton<IRdoStore>(sqlStore);
}
else
{
    builder.Services.AddSingleton<IRdoStore, InMemoryStore>();
}
#else
builder.Services.AddSingleton<IRdoStore, InMemoryStore>();
#endif

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("per-key", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Request.Headers["X-Api-Key"].ToString(),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = settings.Service.RateLimitPerMinute, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("http://localhost:5173", "http://localhost:4173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseRateLimiter();

app.MapGet("/health", (IIntakeModel model, IRdoStore store) => Results.Ok(new
{
    ok = true,
    intakeModel = model.Name,
    store = store.GetType().Name,
    evaluation = settings.Evaluation.Engine,
    benches = master.Benches.Count,
}));

var api = app.MapGroup("/api").RequireRateLimiting("per-key").AddEndpointFilter<CallerFilter>();
MasterEndpoints.Map(api);
RequestEndpoints.Map(api);
PlanningEndpoints.Map(api);
RunEndpoints.Map(api);

if (Environment.GetEnvironmentVariable("RDO_DEMO_SEED") is { Length: > 0 } demo)
{
    await DemoSeed.LoadAsync(app.Services, Path.Combine(root, "config", "demo"), demo).ConfigureAwait(false);
}

await app.RunAsync().ConfigureAwait(false);

/// <summary>Entry point, visible to integration tests.</summary>
public partial class Program;
