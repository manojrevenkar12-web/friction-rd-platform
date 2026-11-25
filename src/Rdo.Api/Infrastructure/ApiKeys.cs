using System.Security.Cryptography;
using System.Text;

namespace Rdo.Api.Infrastructure;

public sealed record Caller(string Name, string Role);

/// <summary>Who is calling comes from the API key, never from the request body.</summary>
public sealed class ApiKeys
{
    private readonly Dictionary<string, Caller> byHash = new(StringComparer.Ordinal);

    /// <param name="spec">"role:name:key,role:name:key" (RDO_API_KEYS).</param>
    public ApiKeys(string spec)
    {
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(':', 3);
            if (bits.Length != 3)
            {
                throw new FormatException("RDO_API_KEYS entries are role:name:key");
            }

            byHash[Hash(bits[2])] = new Caller(bits[1], bits[0]);
        }
    }

    public Caller? Find(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        var h = Hash(key);
        foreach (var (stored, caller) in byHash)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stored), Encoding.ASCII.GetBytes(h)))
            {
                return caller;
            }
        }

        return null;
    }

    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
