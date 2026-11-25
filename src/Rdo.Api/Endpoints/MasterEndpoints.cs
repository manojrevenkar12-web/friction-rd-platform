using Rdo.Core.Domain;

namespace Rdo.Api.Endpoints;

public static class MasterEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/master/benches", (MasterData m) => m.Benches);
        api.MapGet("/master/applications", (MasterData m) => m.Applications.Values.OrderBy(a => a.Id, StringComparer.Ordinal));
        api.MapGet("/master/formulations", (MasterData m) => m.Formulations.Values.OrderBy(f => f.Code, StringComparer.Ordinal));
        api.MapGet("/master/procedures", (MasterData m) => m.Procedures.Values.OrderBy(p => p.Id, StringComparer.Ordinal));
    }
}
