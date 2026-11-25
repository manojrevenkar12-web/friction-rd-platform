namespace Rdo.Api.Infrastructure;

public static class Paths
{
    /// <summary>The repository root (holds config/): RDO_ROOT, or the nearest parent with config/platform.json.</summary>
    public static string Root()
    {
        var env = Environment.GetEnvironmentVariable("RDO_ROOT");
        if (!string.IsNullOrEmpty(env))
        {
            return env;
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "config", "platform.json")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new FileNotFoundException("config/platform.json not found; set RDO_ROOT");
    }
}
