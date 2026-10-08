using System.Security.Cryptography;
using System.Text;

namespace ApexRacers.Core;

public enum DriverReferencePurpose { Detail = 1, Comparison = 2, Follow = 3 }
public static class DriverReferences
{
    public const string CatalogId = "synthetic-scoped-driver-v1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    public static string Create() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static bool Valid(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
