using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inquiry.Entities;

[InquiryTable("PackageSmokeWidget")]
internal sealed class PackageSmokeWidget
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 80)]
    public required string Name { get; set; }
}

internal static class Program
{
    private const string MetadataPrefix = "Inquiry.SchemaManifest.";

    public static int Main(string[] args)
    {
        if (args.Length != 1) throw new InvalidDataException("Expected the provider id as the sole argument.");
        var expectedProviderId = args[0];
        var assemblyPath = typeof(Program).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        var values = ReadManifestMetadata(metadata);
        var chunkCount = int.Parse(values[MetadataPrefix + "ChunkCount"], System.Globalization.CultureInfo.InvariantCulture);
        var json = string.Concat(Enumerable.Range(0, chunkCount)
            .Select(index => values[$"{MetadataPrefix}Chunk.{index:D4}"]));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        using var document = JsonDocument.Parse(json);

        AssertEqual("1", values[MetadataPrefix + "FormatVersion"], "metadata format version");
        AssertEqual(expectedProviderId, document.RootElement.GetProperty("providerId").GetString(), "manifest provider id");
        AssertEqual(Inquiry.Generated.InquiryGeneratedSchema.SchemaManifestChunkCount, chunkCount, "chunk count");
        AssertEqual(Inquiry.Generated.InquiryGeneratedSchema.SchemaManifestJson, json, "manifest JSON");
        AssertEqual(Inquiry.Generated.InquiryGeneratedSchema.SchemaManifestSha256, hash, "generated manifest hash");
        AssertEqual(values[MetadataPrefix + "Sha256"], hash, "metadata manifest hash");
        AssertEqual(3 + chunkCount, values.Count, "manifest metadata attribute count");

        Console.WriteLine($"Packed {expectedProviderId} provider manifest smoke test passed ({chunkCount} chunk(s), SHA-256 {hash}).");
        return 0;
    }

    private static Dictionary<string, string> ReadManifestMetadata(MetadataReader reader)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (!IsAssemblyMetadataAttribute(reader, attribute.Constructor)) continue;

            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) throw new InvalidDataException("Invalid custom-attribute prolog.");
            var key = blob.ReadSerializedString();
            var value = blob.ReadSerializedString();
            if (key is null || value is null || !key.StartsWith(MetadataPrefix, StringComparison.Ordinal)) continue;
            if (!result.TryAdd(key, value)) throw new InvalidDataException($"Duplicate manifest metadata key '{key}'.");
        }

        return result;
    }

    private static bool IsAssemblyMetadataAttribute(MetadataReader reader, EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference) return false;
        var member = reader.GetMemberReference((MemberReferenceHandle)constructor);
        if (member.Parent.Kind != HandleKind.TypeReference) return false;
        var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
        return reader.StringComparer.Equals(type.Namespace, "System.Reflection")
            && reader.StringComparer.Equals(type.Name, "AssemblyMetadataAttribute");
    }

    private static void AssertEqual<T>(T expected, T actual, string subject)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidDataException($"Mismatched {subject}. Expected '{expected}', actual '{actual}'.");
    }
}
