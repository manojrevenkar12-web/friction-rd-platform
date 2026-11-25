using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Rdo.SqlServer;

/// <summary>
/// Applies the embedded Migrations/V###__name.sql scripts in order, each once, each in a
/// transaction, and records them in dbo.__SchemaMigrations. Running it twice changes nothing.
/// </summary>
public static partial class Migrator
{
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();

    public static IReadOnlyList<string> Migrate(string connectionString)
    {
        using var conn = new SqlConnection(connectionString);
        conn.Open();
        Exec(conn, null, """
            IF OBJECT_ID(N'dbo.__SchemaMigrations', N'U') IS NULL
                CREATE TABLE dbo.__SchemaMigrations (Id NVARCHAR(200) NOT NULL PRIMARY KEY, AppliedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
            """);
        var applied = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = new SqlCommand("SELECT Id FROM dbo.__SchemaMigrations", conn))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                applied.Add(r.GetString(0));
            }
        }

        var asm = typeof(Migrator).Assembly;
        var done = new List<string>();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".sql", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var id = name[(name.IndexOf("Migrations.", StringComparison.Ordinal) + "Migrations.".Length)..];
            if (applied.Contains(id))
            {
                continue;
            }

            var sql = Read(asm, name);
            using var tx = conn.BeginTransaction();
            foreach (var batch in BatchSeparator().Split(sql).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                Exec(conn, tx, batch);
            }

            using (var mark = new SqlCommand("INSERT INTO dbo.__SchemaMigrations (Id) VALUES (@id)", conn, tx))
            {
                mark.Parameters.AddWithValue("@id", id);
                mark.ExecuteNonQuery();
            }

            tx.Commit();
            done.Add(id);
        }

        return done;
    }

    private static string Read(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(s);
        return reader.ReadToEnd();
    }

    private static void Exec(SqlConnection conn, SqlTransaction? tx, string sql)
    {
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.ExecuteNonQuery();
    }
}
