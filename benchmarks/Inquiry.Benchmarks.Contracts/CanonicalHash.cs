using System.Security.Cryptography;
using System.Text;

namespace Inquiry.Benchmarks.Contracts;

internal static class CanonicalHash
{
    public static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string Join(IEnumerable<string> values)
        => string.Join("\u001f", values.Select(Escape));

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\u001f", "\\u001f", StringComparison.Ordinal);
}
