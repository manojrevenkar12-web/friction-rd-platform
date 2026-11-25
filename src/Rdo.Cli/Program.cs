// rdo-core: the platform's decision code behind a JSON-lines protocol on stdin/stdout.
// The Python lab drives the real planner, intake validator and evaluators through it, so what is
// measured is what the service runs. One JSON request per line in, one JSON response per line out.
using System.Text.Json;
using System.Text.Json.Nodes;
using Rdo.Core;
using Rdo.Core.Domain;
using Rdo.Core.Evaluation;
using Rdo.Core.Intake;
using Rdo.Core.Json;
using Rdo.Core.Planning;
using Rdo.Legacy;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: rdo-core <master-data.json> <platform.json>");
    return 2;
}

var master = MasterDataLoader.FromFile(args[0]);
var settings = PlatformSettings.Load(args[1]);
var validator = new IntakeValidator(master, settings.Intake);
var evaluators = new Dictionary<string, IFrictionEvaluator>
{
    ["new"] = new FrictionEvaluator(settings.Evaluation.FadeFloorShareOfTarget),
    ["legacy"] = new LegacyFrictionEvaluator(settings.Evaluation.FadeFloorShareOfTarget),
};
var policies = new Dictionary<string, IDispatchPolicy>
{
    ["fifo"] = Planner.Create("fifo", settings.Planner),
    ["slack-aware"] = Planner.Create("slack-aware", settings.Planner),
};
var o = JsonDefaults.Options;

string? line;
while ((line = Console.ReadLine()) is not null)
{
    if (line.Length == 0)
    {
        continue;
    }

    JsonNode? reply;
    try
    {
        var req = JsonNode.Parse(line)!.AsObject();
        reply = req["cmd"]!.GetValue<string>() switch
        {
            "dispatch" => JsonSerializer.SerializeToNode(policies[req["policy"]!.GetValue<string>()].Choose(
                req["bench"].Deserialize<BenchState>(o)!,
                req["queue"].Deserialize<List<QueuedOrder>>(o)!,
                req["now"].Deserialize<DateTimeOffset>(o)), o),
            "validate" => JsonSerializer.SerializeToNode(validator.Validate(
                req["text"]!.GetValue<string>(),
                req["draft"].Deserialize<OrderDraft>(o)!,
                req["orderId"]!.GetValue<string>(),
                req["now"].Deserialize<DateTimeOffset>(o)), o),
            "evaluate" => Evaluate(req),
            "ping" => new JsonObject { ["ok"] = true },
            var c => throw new InvalidOperationException($"unknown command {c}"),
        };
    }
    catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or NullReferenceException or ArgumentException)
    {
        reply = new JsonObject { ["error"] = e.Message };
    }

    Console.Out.WriteLine(reply?.ToJsonString(o) ?? "null");
    Console.Out.Flush();
}

return 0;

JsonNode Evaluate(JsonObject req)
{
    var run = req["run"].Deserialize<TestRun>(o)!;
    var formulation = master.Formulations[run.Formulation];
    var current = evaluators["legacy"].Evaluate(run, formulation);
    var candidate = evaluators["new"].Evaluate(run, formulation);
    return new JsonObject
    {
        ["legacy"] = JsonSerializer.SerializeToNode(current, o),
        ["new"] = JsonSerializer.SerializeToNode(candidate, o),
        ["parity"] = JsonSerializer.SerializeToNode(Parity.Compare(current, candidate, settings.Evaluation.ParityTolerance), o),
    };
}
