using System;
using System.Globalization;
using System.Text;

namespace Inquiry.Oracle.Shared;

/// <summary>
/// Produces Oracle-safe bind identifiers from arbitrary logical parameter names. The compact fixed
/// form deliberately leaves five characters of Oracle's conservative 30-byte identifier budget for
/// runtime suffixes used by IN-list expansion.
/// </summary>
internal static class OracleBindName
{
    private const string Prefix = "iq1$";
    private const int StemLength = 6;
    private const int HashDigits = 14;
    private const int EncodedLength = 25;

    public static string Encode(string logicalName)
    {
        if (string.IsNullOrEmpty(logicalName))
            throw new ArgumentException("Oracle bind names cannot be null or empty.", nameof(logicalName));

        var builder = new StringBuilder(EncodedLength);
        builder.Append(Prefix);

        var stemCount = 0;
        for (var i = 0; i < logicalName.Length && stemCount < StemLength; i++)
        {
            var c = logicalName[i];
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                builder.Append(c);
                stemCount++;
            }
        }

        while (stemCount++ < StemLength)
            builder.Append('x');

        builder.Append('$');
        builder.Append(Hash(logicalName).ToString("x16", CultureInfo.InvariantCulture), 16 - HashDigits, HashDigits);
        return builder.ToString();
    }

    public static bool IsEncoded(string name)
    {
        if (IsBatchName(name)) return true;

        // IN expansion appends up to five decimal digits to the fixed encoded base.
        if (name.Length < EncodedLength || name.Length > EncodedLength + 5
            || !name.StartsWith(Prefix, StringComparison.Ordinal)
            || name[Prefix.Length + StemLength] != '$')
        {
            return false;
        }

        for (var i = Prefix.Length + StemLength + 1; i < EncodedLength; i++)
        {
            var c = name[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                return false;
        }

        for (var i = EncodedLength; i < name.Length; i++)
        {
            if (name[i] < '0' || name[i] > '9')
                return false;
        }

        return true;
    }

    private static bool IsBatchName(string name)
    {
        const string batchPrefix = "iq1$b";
        if (name.Length <= batchPrefix.Length + 2 || name.Length > 30
            || !name.StartsWith(batchPrefix, StringComparison.Ordinal))
            return false;

        var separator = name.IndexOf('_', batchPrefix.Length);
        if (separator <= batchPrefix.Length || separator == name.Length - 1) return false;
        for (var i = batchPrefix.Length; i < name.Length; i++)
        {
            if (i == separator) continue;
            if (name[i] < '0' || name[i] > '9') return false;
        }
        return true;
    }

    // Deterministic FNV-1a over UTF-16 code units. A 56-bit suffix is ample collision resistance for
    // the small parameter sets in one command while preserving Oracle's suffix budget.
    private static ulong Hash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            hash ^= (byte)c;
            hash *= prime;
            hash ^= (byte)(c >> 8);
            hash *= prime;
        }
        return hash;
    }
}
