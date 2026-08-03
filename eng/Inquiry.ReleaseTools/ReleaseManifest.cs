using System.Text.Json.Serialization;

namespace Inquiry.ReleaseTools;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleaseManifest(
    [property: JsonRequired] string SchemaVersion,
    [property: JsonRequired] string PackageVersion,
    [property: JsonRequired] string Tag,
    [property: JsonRequired] IReadOnlyList<ReleasePackage> Packages,
    [property: JsonRequired] ReleaseAssets Assets);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleasePackage(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Project,
    [property: JsonRequired] IReadOnlyDictionary<string, string> Dependencies,
    [property: JsonRequired] IReadOnlyList<string> LibTfms,
    // Analyzer symbols are EMBEDDED in these assemblies (DebugType=embedded), never loose PDBs:
    // nuget.org symbol validation rejects a snupkg PDB with no matching lib/ DLL, and analyzer
    // DLLs live under analyzers/dotnet/cs. VerifyNupkg checks the embedded PDB and its SourceLink.
    [property: JsonRequired] IReadOnlyList<string> Analyzers,
    [property: JsonRequired] IReadOnlyList<string> FrameworkReferences,
    // Dependencies the SDK prunes from specific TFM groups because a declared framework
    // reference supplies them (e.g. Microsoft.Extensions.* on net10.0 via AspNetCore.App).
    IReadOnlyDictionary<string, IReadOnlyList<string>>? PrunedDependencies = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleaseAssets(
    [property: JsonRequired] string LicenseExpression,
    [property: JsonRequired] string Readme,
    [property: JsonRequired] string Icon,
    [property: JsonRequired] string RepositoryUrl,
    [property: JsonRequired] string RepositoryBranch,
    [property: JsonRequired] bool RequireSymbols,
    [property: JsonRequired] bool RequireSourceLink);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReleaseManifest))]
[JsonSerializable(typeof(CiRequiredContract))]
internal sealed partial class ReleaseJsonContext : JsonSerializerContext;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CiRequiredContract(
    [property: JsonRequired] string SchemaVersion,
    [property: JsonRequired] IReadOnlyList<CiRequiredJob> RequiredJobs);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CiRequiredJob(
    [property: JsonRequired] string Job,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Matrix);
