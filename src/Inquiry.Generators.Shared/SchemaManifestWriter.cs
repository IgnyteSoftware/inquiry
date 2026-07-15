using Inquiry.Generators.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Inquiry.Generators;

internal static class SchemaManifestWriter
{
    public const int FormatVersion = 1;
    public const int ChunkByteLimit = 12_288;

    public static string Write(SchemaManifestData manifest)
    {
        var b = new StringBuilder();
        b.Append("{\"formatVersion\":1,\"providerId\":"); String(b, manifest.ProviderId); b.Append(",\"tables\":[");
        for (var i = 0; i < manifest.Tables.Count; i++) { if (i > 0) b.Append(','); Table(b, manifest.Tables[i]); }
        b.Append("],\"providerArtifacts\":[");
        for (var i = 0; i < manifest.ProviderArtifacts.Count; i++) { if (i > 0) b.Append(','); Artifact(b, manifest.ProviderArtifacts[i]); }
        return b.Append("]}").ToString();
    }

    public static string Sha256(string json)
    {
        using var hash = SHA256.Create();
        var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(json));
        var b = new StringBuilder(64); foreach (var value in bytes) b.Append(value.ToString("x2", CultureInfo.InvariantCulture)); return b.ToString();
    }

    public static IReadOnlyList<string> Chunk(string json)
    {
        TryBuildTransport(json, int.MaxValue, out var chunks, out _);
        return chunks;
    }

    public static bool TryBuildTransport(string json, int maxChunks, out IReadOnlyList<string> chunks, out int requiredChunkCount)
    {
        var retained = new List<string>(System.Math.Min(maxChunks, 16));
        var b = new StringBuilder();
        var bytes = 0;
        requiredChunkCount = 0;
        var overflow = false;
        for (var i = 0; i < json.Length; i++)
        {
            var scalarBytes = Utf8ByteCount(json, i, out var charCount);
            if (bytes + scalarBytes > ChunkByteLimit && b.Length > 0)
            {
                requiredChunkCount++;
                if (!overflow && requiredChunkCount <= maxChunks) retained.Add(b.ToString());
                else { overflow = true; retained.Clear(); }
                b.Clear(); bytes = 0;
            }
            b.Append(json, i, charCount); bytes += scalarBytes; i += charCount - 1;
        }
        if (b.Length > 0)
        {
            requiredChunkCount++;
            if (!overflow && requiredChunkCount <= maxChunks) retained.Add(b.ToString());
            else { overflow = true; retained.Clear(); }
        }
        chunks = retained;
        return !overflow;
    }

    private static int Utf8ByteCount(string value, int index, out int charCount)
    {
        var c = value[index];
        if (c <= '\u007f') { charCount = 1; return 1; }
        if (c <= '\u07ff') { charCount = 1; return 2; }
        if (char.IsHighSurrogate(c) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
        { charCount = 2; return 4; }
        charCount = 1;
        return 3;
    }

    private static void Table(StringBuilder b, SchemaManifestTableData t)
    {
        b.Append("{\"schema\":"); NullableString(b, t.Schema); b.Append(",\"name\":"); String(b, t.Name); b.Append(",\"columns\":[");
        for (var i = 0; i < t.Columns.Count; i++) { if (i > 0) b.Append(','); Column(b, t.Columns[i]); }
        b.Append("],\"primaryKey\":"); StringsOrNull(b, t.PrimaryKey); b.Append(",\"indexes\":[");
        for (var i = 0; i < t.Indexes.Count; i++) { if (i > 0) b.Append(','); Index(b, t.Indexes[i]); }
        b.Append("],\"checks\":["); for (var i = 0; i < t.Checks.Count; i++) { if (i > 0) b.Append(','); b.Append("{\"name\":"); String(b, t.Checks[i].Name); b.Append(",\"expression\":"); String(b, t.Checks[i].Expression); b.Append('}'); }
        b.Append("],\"foreignKeys\":["); for (var i = 0; i < t.ForeignKeys.Count; i++) { if (i > 0) b.Append(','); ForeignKey(b, t.ForeignKeys[i]); } b.Append("]}");
    }

    private static void Column(StringBuilder b, SchemaManifestColumnData c)
    {
        b.Append("{\"name\":"); String(b, c.Name); b.Append(",\"storeType\":"); NullableString(b, c.StoreType); b.Append(",\"typeInference\":"); String(b, c.TypeInference);
        b.Append(",\"typeClass\":"); String(b, c.TypeClass); b.Append(",\"nullable\":").Append(c.Nullable ? "true" : "false"); b.Append(",\"primaryKeyOrdinal\":").Append(c.PrimaryKeyOrdinal?.ToString(CultureInfo.InvariantCulture) ?? "null");
        b.Append(",\"generation\":"); String(b, c.Generation); b.Append(",\"defaultExpression\":"); NullableString(b, c.DefaultExpression); b.Append(",\"computedExpression\":"); NullableString(b, c.ComputedExpression); b.Append(",\"concurrency\":"); String(b, c.Concurrency); b.Append('}');
    }

    private static void Index(StringBuilder b, SchemaManifestIndexData i) { b.Append("{\"name\":"); String(b, i.Name); b.Append(",\"unique\":").Append(i.Unique ? "true" : "false"); b.Append(",\"keyColumns\":"); Strings(b, i.KeyColumns); b.Append(",\"includeColumns\":"); Strings(b, i.IncludeColumns); b.Append('}'); }
    private static void ForeignKey(StringBuilder b, SchemaManifestForeignKeyData f) { b.Append("{\"name\":"); NullableString(b, f.Name); b.Append(",\"localColumns\":"); Strings(b, f.LocalColumns); b.Append(",\"referencedSchema\":"); NullableString(b, f.ReferencedSchema); b.Append(",\"referencedTable\":"); String(b, f.ReferencedTable); b.Append(",\"referencedColumns\":"); Strings(b, f.ReferencedColumns); b.Append(",\"onDelete\":"); String(b, f.OnDelete); b.Append(",\"onUpdate\":"); String(b, f.OnUpdate); b.Append('}'); }
    private static void Artifact(StringBuilder b, SchemaManifestArtifactData a) { b.Append("{\"schema\":"); String(b, a.Schema); b.Append(",\"name\":"); String(b, a.Name); b.Append(",\"kind\":"); String(b, a.Kind); b.Append(",\"signature\":"); String(b, a.Signature); b.Append('}'); }
    private static void StringsOrNull(StringBuilder b, IReadOnlyList<string>? values) { if (values is null) b.Append("null"); else Strings(b, values); }
    private static void Strings(StringBuilder b, IReadOnlyList<string> values) { b.Append('['); for (var i = 0; i < values.Count; i++) { if (i > 0) b.Append(','); String(b, values[i]); } b.Append(']'); }
    private static void NullableString(StringBuilder b, string? value) { if (value is null) b.Append("null"); else String(b, value); }
    private static void String(StringBuilder b, string value)
    {
        b.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c) { case '"': b.Append("\\\""); break; case '\\': b.Append("\\\\"); break; case '\b': b.Append("\\b"); break; case '\f': b.Append("\\f"); break; case '\n': b.Append("\\n"); break; case '\r': b.Append("\\r"); break; case '\t': b.Append("\\t"); break;
                default:
                    if (c < ' ' || char.IsSurrogate(c) && !(char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else { b.Append(c); if (char.IsHighSurrogate(c)) b.Append(value[++i]); }
                    break;
            }
        }
        b.Append('"');
    }
}
