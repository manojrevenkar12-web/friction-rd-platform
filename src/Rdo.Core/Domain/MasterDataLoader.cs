using System.Text.Json;
using Rdo.Core.Json;

namespace Rdo.Core.Domain;

public static class MasterDataLoader
{
    private sealed record Document(
        List<Bench> Benches,
        List<Application> Applications,
        List<Formulation> Formulations,
        List<Procedure> Procedures);

    public static MasterData FromJson(string json)
    {
        var d = JsonSerializer.Deserialize<Document>(json, JsonDefaults.Options)
            ?? throw new InvalidDataException("master data is empty");
        return new MasterData(d.Benches, d.Applications, d.Formulations, d.Procedures);
    }

    public static MasterData FromFile(string path) => FromJson(File.ReadAllText(path));
}
