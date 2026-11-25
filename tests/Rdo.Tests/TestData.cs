using Rdo.Core;
using Rdo.Core.Domain;

namespace Rdo.Tests;

internal static class TestData
{
    public static readonly string Root = FindRoot();

    public static MasterData Master { get; } = MasterDataLoader.FromFile(Path.Combine(Root, "config", "demo", "master-data.json"));

    public static PlatformSettings Settings { get; } = PlatformSettings.Load(Path.Combine(Root, "config", "platform.json"));

    public static readonly DateTimeOffset Now = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "config", "platform.json")))
            {
                return d.FullName;
            }
        }

        throw new FileNotFoundException("config/platform.json");
    }

    /// <summary>A released formulation with a passenger-car application, and that application.</summary>
    public static (Formulation F, Application A) ReleasedPassengerCar()
    {
        foreach (var f in Master.Formulations.Values.Where(f => f.Status == FormulationStatus.ReleasedForTest))
        {
            foreach (var id in f.Applications)
            {
                var a = Master.Applications[id];
                if (a.VehicleClass == BenchKind.PassengerCar)
                {
                    return (f, a);
                }
            }
        }

        throw new InvalidOperationException("no released passenger-car formulation in the demo master data");
    }
}
