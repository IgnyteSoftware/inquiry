using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Inquiry.Benchmarks.Contracts;

public sealed record SelectedAssetRoot(string Id, string Path);

/// <summary>
/// Content-addresses the exact asset list emitted by MSBuild for one TFM/RID build. The restore graph is
/// retained only as provenance; it is never used to infer which compiler or runtime assets were selected.
/// </summary>
public static class ResolvedDependencyManifestCollector
{
    public const string EmittedSchemaVersion = "inquiry-msbuild-selected-assets-v1";

    public static ResolvedDependencyManifest Collect(
        string selectedAssetsManifestPath,
        string projectAssetsPath,
        string provider,
        BenchmarkSourceLane lane,
        string runtimeTfm,
        string runtimeIdentifier,
        IReadOnlyList<SelectedAssetRoot> allowedRoots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedAssetsManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectAssetsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeTfm);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        ArgumentNullException.ThrowIfNull(allowedRoots);

        var selectedManifestPath = Path.GetFullPath(selectedAssetsManifestPath);
        var restoreGraphPath = Path.GetFullPath(projectAssetsPath);
        var roots = NormalizeRoots(allowedRoots);
        var lines = File.ReadAllLines(selectedManifestPath);
        if (lines.Length < 2)
            throw new InvalidDataException("The MSBuild selected-assets manifest has no selected assets.");

        var header = lines[0].Split('\t');
        if (header.Length != 5 ||
            !StringComparer.Ordinal.Equals(header[0], EmittedSchemaVersion) ||
            !StringComparer.Ordinal.Equals(header[1], provider) ||
            !StringComparer.Ordinal.Equals(header[2], lane.ToString()) ||
            !StringComparer.Ordinal.Equals(header[3], runtimeTfm) ||
            !StringComparer.Ordinal.Equals(header[4], runtimeIdentifier))
            throw new InvalidDataException("The selected-assets manifest header does not match its provider/lane/TFM/RID contract.");

        var assets = new List<ResolvedDependencyAsset>(lines.Length - 1);
        var identities = new HashSet<(ResolvedAssetKind Kind, string LogicalAssetId)>(AssetIdentityComparer.Instance);
        for (var index = 1; index < lines.Length; index++)
        {
            var fields = lines[index].Split('\t');
            if (fields.Length != 3 ||
                !Enum.TryParse<ResolvedAssetKind>(fields[0], ignoreCase: false, out var kind) ||
                !IsSafeProvenance(fields[1]))
                throw new InvalidDataException($"Selected asset line {index + 1} is malformed.");

            var physicalPath = Path.GetFullPath(fields[2]);
            if (!File.Exists(physicalPath))
                throw new FileNotFoundException($"MSBuild-selected asset does not exist: {physicalPath}");
            var root = roots.FirstOrDefault(candidate => IsWithin(candidate.Path, physicalPath))
                ?? throw new InvalidDataException($"MSBuild-selected asset escapes every approved physical root: {physicalPath}");
            EnsureNoReparsePoint(root.Path, physicalPath);
            var relative = Normalize(Path.GetRelativePath(root.Path, physicalPath));
            if (!IsSafeRelativePath(relative))
                throw new InvalidDataException($"MSBuild-selected asset has an unsafe relative identity: {relative}");
            var logicalAssetId = $"{root.Id}/{relative}";
            if (!identities.Add((kind, logicalAssetId)))
                throw new InvalidDataException($"MSBuild-selected asset identity collides case-insensitively: {kind}:{logicalAssetId}");

            ValidateRole(kind, physicalPath, provider);

            assets.Add(new ResolvedDependencyAsset(
                logicalAssetId,
                kind,
                fields[1],
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(physicalPath))).ToLowerInvariant()));
        }

        var requiredKinds = new[]
        {
            ResolvedAssetKind.CompilerReference,
            ResolvedAssetKind.Runtime,
            ResolvedAssetKind.Analyzer,
            ResolvedAssetKind.GeneratedSource,
            ResolvedAssetKind.HostAssembly,
            ResolvedAssetKind.ProductAssembly,
        };
        if (requiredKinds.Any(kind => assets.All(asset => asset.Kind != kind)))
            throw new InvalidDataException("The MSBuild-selected asset set omits a required compiler/runtime/analyzer/generated/host/product role.");

        var requiredProductNames = new[] { "Inquiry.dll", ProviderAssemblyName(provider) };
        if (requiredProductNames.Any(required => assets.All(asset => asset.Kind != ResolvedAssetKind.ProductAssembly ||
                !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(asset.LogicalAssetId), required))))
            throw new InvalidDataException("The selected product role must contain Inquiry.dll and the exact provider assembly.");
        if (assets.All(asset => asset.Kind != ResolvedAssetKind.Analyzer ||
                !Path.GetFileName(asset.LogicalAssetId).StartsWith("Inquiry.", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The selected analyzer role must contain an Inquiry provider analyzer.");

        return new ResolvedDependencyManifest(
            ResolvedDependencyManifest.RequiredSelectionRule,
            provider,
            lane,
            runtimeTfm,
            runtimeIdentifier,
            ComputeCanonicalProjectAssetsSha256(restoreGraphPath, roots),
            ComputeCanonicalSelectedAssetsSha256(header, assets),
            assets.OrderBy(static asset => asset.Kind)
                .ThenBy(static asset => asset.LogicalAssetId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static asset => asset.Provenance, StringComparer.Ordinal)
                .ToArray());
    }

    public static string ComputeCanonicalProjectAssetsSha256(
        string projectAssetsPath,
        IReadOnlyList<SelectedAssetRoot> allowedRoots)
    {
        var roots = NormalizeRoots(allowedRoots);
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.GetFullPath(projectAssetsPath)));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonicalJson(document.RootElement, writer, roots);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    public static bool IsExact(
        ResolvedDependencyManifest manifest,
        string selectedAssetsManifestPath,
        string projectAssetsPath,
        IReadOnlyList<SelectedAssetRoot> allowedRoots)
    {
        var actual = Collect(selectedAssetsManifestPath, projectAssetsPath, manifest.Provider, manifest.Lane,
            manifest.RuntimeTfm, manifest.RuntimeIdentifier, allowedRoots);
        return StringComparer.Ordinal.Equals(actual.ContentSha256, manifest.ContentSha256);
    }

    private static IReadOnlyList<SelectedAssetRoot> NormalizeRoots(IReadOnlyList<SelectedAssetRoot> roots)
    {
        if (roots.Count == 0 || roots.Any(static root => string.IsNullOrWhiteSpace(root.Id) || string.IsNullOrWhiteSpace(root.Path)))
            throw new ArgumentException("At least one named physical root is required.", nameof(roots));
        if (roots.Select(static root => root.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != roots.Count)
            throw new ArgumentException("Physical root IDs must be case-insensitively unique.", nameof(roots));
        if (roots.Any(static root => !IsSafeRootId(root.Id)))
            throw new ArgumentException("Physical root IDs must be simple portable identifiers.", nameof(roots));

        var normalized = roots.Select(root => new SelectedAssetRoot(root.Id, Path.GetFullPath(root.Path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))).ToArray();
        if (normalized.Select(static root => root.Path).Distinct(PathComparer).Count() != normalized.Length)
            throw new ArgumentException("Physical roots must identify distinct paths.", nameof(roots));
        foreach (var root in normalized)
        {
            if (!Directory.Exists(root.Path))
                throw new DirectoryNotFoundException($"Approved selected-asset root does not exist: {root.Path}");
            if ((File.GetAttributes(root.Path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Approved selected-asset root cannot be a reparse point: {root.Path}");
        }
        return normalized.OrderByDescending(static root => root.Path.Length).ToArray();
    }

    private static string ComputeCanonicalSelectedAssetsSha256(
        IReadOnlyList<string> header,
        IReadOnlyList<ResolvedDependencyAsset> assets)
    {
        var lines = new[] { string.Join('\t', header) }.Concat(assets
            .OrderBy(static asset => asset.Kind)
            .ThenBy(static asset => asset.LogicalAssetId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static asset => asset.Provenance, StringComparer.Ordinal)
            .Select(static asset => $"{asset.Kind}\t{asset.Provenance}\t{asset.LogicalAssetId}\t{asset.Sha256}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))))
            .ToLowerInvariant();
    }

    private static void WriteCanonicalJson(JsonElement element, Utf8JsonWriter writer, IReadOnlyList<SelectedAssetRoot> roots)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = element.EnumerateObject()
                    .Select(property => (Name: CanonicalizePathValue(property.Name, roots), property.Value))
                    .OrderBy(static property => property.Name, StringComparer.Ordinal)
                    .ToArray();
                if (properties.Select(static property => property.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != properties.Length)
                    throw new InvalidDataException("project.assets.json contains path keys that collide after canonical relocation.");
                foreach (var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(property.Value, writer, roots);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonicalJson(item, writer, roots);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(CanonicalizePathValue(element.GetString()!, roots));
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("project.assets.json contains an unsupported JSON token.");
        }
    }

    private static string CanonicalizePathValue(string value, IReadOnlyList<SelectedAssetRoot> roots)
    {
        if (!Path.IsPathRooted(value)) return Normalize(value);
        if (value.Length >= 32 && value.EndsWith('=') && value.Length % 4 == 0 &&
            Convert.TryFromBase64String(value, new byte[value.Length], out _))
            return value;
        var fullPath = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = roots.FirstOrDefault(candidate => IsWithinOrEqual(candidate.Path, fullPath))
            ?? throw new InvalidDataException($"project.assets.json contains an absolute path outside approved roots: {value}");
        if (PathComparer.Equals(root.Path, fullPath)) return root.Id;
        return $"{root.Id}/{Normalize(Path.GetRelativePath(root.Path, fullPath))}";
    }

    private static void ValidateRole(ResolvedAssetKind kind, string physicalPath, string provider)
    {
        var extension = Path.GetExtension(physicalPath);
        var fileName = Path.GetFileName(physicalPath);
        var valid = kind switch
        {
            ResolvedAssetKind.CompilerReference or ResolvedAssetKind.Runtime or ResolvedAssetKind.Analyzer or
                ResolvedAssetKind.HostAssembly or ResolvedAssetKind.ProductAssembly =>
                extension.Equals(".dll", StringComparison.OrdinalIgnoreCase),
            ResolvedAssetKind.Native => extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".so", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase),
            ResolvedAssetKind.GeneratedSource => extension.Equals(".cs", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
        if (!valid) throw new InvalidDataException($"Selected {kind} asset has an invalid physical role: {physicalPath}");
        if (kind == ResolvedAssetKind.HostAssembly && !fileName.StartsWith("Inquiry.Benchmarks", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected host role must be an Inquiry benchmark assembly.");
        if (kind == ResolvedAssetKind.ProductAssembly &&
            !StringComparer.OrdinalIgnoreCase.Equals(fileName, "Inquiry.dll") &&
            !StringComparer.OrdinalIgnoreCase.Equals(fileName, ProviderAssemblyName(provider)))
            throw new InvalidDataException("Only Inquiry.dll and the exact selected provider DLL may be classified as product assemblies.");
    }

    internal static string ProviderAssemblyName(string provider) => provider.ToLowerInvariant() switch
    {
        "sqlite" => "Inquiry.Sqlite.dll",
        "sqlserver" => "Inquiry.SqlServer.dll",
        "postgresql" => "Inquiry.PostgreSql.dll",
        "mysql" => "Inquiry.MySql.dll",
        "mariadb" => "Inquiry.MariaDb.dll",
        "oracle" => "Inquiry.Oracle.dll",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown benchmark provider."),
    };

    private static void EnsureNoReparsePoint(string root, string file)
    {
        FileSystemInfo? current = new FileInfo(file);
        while (current is not null && IsWithinOrEqual(root, current.FullName))
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"MSBuild-selected asset path traverses a reparse point: {file}");
            if (PathComparer.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar)))
                return;
            current = current switch
            {
                FileInfo info => info.Directory,
                DirectoryInfo info => info.Parent,
                _ => null,
            };
        }
        throw new InvalidDataException($"MSBuild-selected asset path is not contained by its approved root: {file}");
    }

    private static bool IsWithin(string root, string path)
        => IsWithinOrEqual(root, path) && !PathComparer.Equals(
            root.TrimEnd(Path.DirectorySeparatorChar), path.TrimEnd(Path.DirectorySeparatorChar));

    private static bool IsWithinOrEqual(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, PathComparison);
    }

    private static bool IsSafeRootId(string value)
        => value.Length > 0 && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool IsSafeProvenance(string value)
        => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= 200 &&
           value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':' or '/' or '+');

    private static bool IsSafeRelativePath(string value)
        => !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && !value.Contains('\\') &&
           value.Split('/').All(static segment => segment is not ("" or "." or ".."));

    private static string Normalize(string value) => value.Replace('\\', '/');

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed class AssetIdentityComparer : IEqualityComparer<(ResolvedAssetKind Kind, string LogicalAssetId)>
    {
        public static AssetIdentityComparer Instance { get; } = new();

        public bool Equals((ResolvedAssetKind Kind, string LogicalAssetId) x, (ResolvedAssetKind Kind, string LogicalAssetId) y)
            => x.Kind == y.Kind && StringComparer.OrdinalIgnoreCase.Equals(x.LogicalAssetId, y.LogicalAssetId);

        public int GetHashCode((ResolvedAssetKind Kind, string LogicalAssetId) value)
            => HashCode.Combine(value.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(value.LogicalAssetId));
    }
}
