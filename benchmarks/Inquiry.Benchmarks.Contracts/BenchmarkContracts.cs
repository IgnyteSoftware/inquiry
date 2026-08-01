namespace Inquiry.Benchmarks.Contracts;

public enum BufferingMode { Buffered, Streaming }
public enum ConnectionLifecycle { PerOperation, Retained }
public enum PoolingMode { Pooled, Unpooled }
public enum PreparationMode { Unprepared, Prepared }
public enum TemperatureMode { Warm, Cold }
public enum TrackingMode { NotApplicable, Tracked, Untracked }
public enum CompilationMode { NotApplicable, Compiled, Uncompiled }
public enum ApiStyle { ExactOverhead, Idiomatic, Micro }
public enum TransactionMode { None, Committed, ExistingRollback }
public enum BenchmarkJobKind { InProcess, Live, ColdProcess, Load }
public enum MetricFamily { Latency, Throughput, Allocation }
public enum BenchmarkSourceMode { ProjectReference, PackageConsumer }
public enum BenchmarkSourceLane { DeveloperProject, ReleaseCandidatePackage }
public enum SourceArtifactRole { Package, RuntimeAssembly, AnalyzerAssembly, GeneratedSource, BenchmarkConfigFile, PackageLockFile, DependencyArtifact, SelectedAssetsManifest, ResolvedDependencyManifest }
public enum ResolvedAssetKind { CompilerReference, Runtime, Native, Analyzer, GeneratedSource, HostAssembly, ProductAssembly }
public enum TransactionOutcome { None, Committed, RolledBack }
public enum MutationEffect { None, Read, Insert, Update, Delete, BulkWrite }
public enum CommandOperation { Select, Insert, Update, Delete, Upsert, Procedure }

public sealed record ContractError(string Code, string Message);

public sealed record SourceArtifact(SourceArtifactRole Role, string RelativeArtifactId, string Sha256);
public sealed record SourceArtifactExpectation(SourceArtifactRole Role, string RelativeArtifactId);
public sealed record ResolvedDependencyAsset(
    string LogicalAssetId,
    ResolvedAssetKind Kind,
    string Provenance,
    string Sha256);

public sealed record ResolvedDependencyManifest(
    string SelectionRuleId,
    string Provider,
    BenchmarkSourceLane Lane,
    string RuntimeTfm,
    string RuntimeIdentifier,
    string ProjectAssetsSha256,
    string SelectedAssetsManifestSha256,
    IReadOnlyList<ResolvedDependencyAsset> Assets)
{
    public const string RequiredSelectionRule = "msbuild-selected-compiler-runtime-native-analyzer-generated-host-product-assets-v1";

    [System.Text.Json.Serialization.JsonIgnore]
    public string CanonicalContent => System.Text.Encoding.UTF8.GetString(ToCanonicalJsonBytes());

    public byte[] ToCanonicalJsonBytes()
    {
        var canonical = this with { Assets = Assets
            .OrderBy(static asset => asset.Kind)
            .ThenBy(static asset => asset.LogicalAssetId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static asset => asset.Provenance, StringComparer.Ordinal)
            .ToArray() };
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(
            System.Text.Json.JsonNamingPolicy.CamelCase));
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(canonical, options);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string ContentSha256 => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(ToCanonicalJsonBytes())).ToLowerInvariant();
}

public sealed record SourceArtifactManifest(
    string Id,
    string Provider,
    BenchmarkSourceLane Lane,
    string RuntimeTfm,
    string RuntimeIdentifier,
    string ResolvedDependencyScope,
    IReadOnlyList<SourceArtifactExpectation> ExpectedArtifacts)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string IdentityHash => CanonicalHash.Sha256(CanonicalHash.Join(
        new[] { Id, Provider, Lane.ToString(), RuntimeTfm, RuntimeIdentifier, ResolvedDependencyScope }.Concat(ExpectedArtifacts
            .OrderBy(static artifact => artifact.Role)
            .ThenBy(static artifact => artifact.RelativeArtifactId, StringComparer.Ordinal)
            .Select(static artifact => $"{artifact.Role}:{artifact.RelativeArtifactId}"))));
}

public static class SourceArtifactManifestCatalog
{
    public static IReadOnlyList<string> PackageIds { get; } =
    [
        "Inquiry", "Inquiry.Sqlite", "Inquiry.SqlServer", "Inquiry.PostgreSql", "Inquiry.MySql",
        "Inquiry.MariaDb", "Inquiry.Oracle", "Inquiry.Interceptors", "Inquiry.Testing",
    ];

    public static IReadOnlyList<string> Providers { get; } =
        ["sqlite", "sqlserver", "postgresql", "mysql", "mariadb", "oracle"];

    public static IReadOnlyList<string> RuntimeIdentifiers { get; } = ["linux-x64", "win-x64"];

    public static IReadOnlyList<SourceArtifactManifest> Manifests { get; } =
        Providers.SelectMany(provider => RuntimeIdentifiers.SelectMany(rid =>
            new[] { "net8.0", "net10.0" }.Select(runtimeTfm =>
                Create(provider, runtimeTfm, rid)))).ToArray();

    public const string ReleaseRuntimeIdentifier = "linux-x64";

    public static SourceArtifactManifest GetRequired(
        string provider,
        BenchmarkSourceMode mode,
        string runtimeTfm,
        string runtimeIdentifier = ReleaseRuntimeIdentifier)
    {
        if (mode != BenchmarkSourceMode.ProjectReference)
            throw new ArgumentOutOfRangeException(nameof(mode), mode,
                "Package-consumer source artifact manifests require a trusted RC package producer and are not wired.");

        var lane = LaneFor(mode);
        return Manifests.SingleOrDefault(manifest =>
                   StringComparer.Ordinal.Equals(manifest.Provider, provider) && manifest.Lane == lane &&
                   StringComparer.Ordinal.Equals(manifest.RuntimeTfm, runtimeTfm) &&
                   StringComparer.Ordinal.Equals(manifest.RuntimeIdentifier, runtimeIdentifier))
               ?? throw new ArgumentOutOfRangeException(nameof(provider), provider,
                   $"No checked source artifact manifest exists for {provider}/{lane}/{runtimeTfm}.");
    }

    public static BenchmarkSourceLane LaneFor(BenchmarkSourceMode mode) => mode switch
    {
        BenchmarkSourceMode.ProjectReference => BenchmarkSourceLane.DeveloperProject,
        BenchmarkSourceMode.PackageConsumer => BenchmarkSourceLane.ReleaseCandidatePackage,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown source mode."),
    };

    private static SourceArtifactManifest Create(string provider, string runtimeTfm, string runtimeIdentifier = ReleaseRuntimeIdentifier)
    {
        const BenchmarkSourceLane lane = BenchmarkSourceLane.DeveloperProject;
        if (!Providers.Contains(provider, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider.");
        var target = $"{runtimeTfm}/{runtimeIdentifier}";
        SourceArtifactExpectation[] artifacts =
        [
            new(SourceArtifactRole.BenchmarkConfigFile, $"config/{provider}/{target}/benchmark.config.json"),
            new(SourceArtifactRole.PackageLockFile, $"restore/{provider}/{target}/packages.lock.json"),
            new(SourceArtifactRole.DependencyArtifact, $"restore/{provider}/{target}/project.assets.json"),
            new(SourceArtifactRole.SelectedAssetsManifest, $"restore/{provider}/{target}/selected-assets.tsv"),
            new(SourceArtifactRole.ResolvedDependencyManifest, $"restore/{provider}/{target}/resolved-assets.manifest"),
        ];
        return new($"source-artifacts-v1/{provider}/{lane}/{target}", provider, lane, runtimeTfm, runtimeIdentifier,
            ResolvedDependencyManifest.RequiredSelectionRule, artifacts);
    }
}

public static class SourceArtifactCollection
{
    public static IReadOnlyList<SourceArtifact> FromSelectedAssets(this ResolvedDependencyManifest resolvedDependencies)
        => resolvedDependencies.Assets
            .Where(static asset => asset.Kind is ResolvedAssetKind.ProductAssembly or
                ResolvedAssetKind.Analyzer or ResolvedAssetKind.GeneratedSource)
            .Select(static asset => new SourceArtifact(asset.Kind switch
            {
                ResolvedAssetKind.ProductAssembly => SourceArtifactRole.RuntimeAssembly,
                ResolvedAssetKind.Analyzer => SourceArtifactRole.AnalyzerAssembly,
                ResolvedAssetKind.GeneratedSource => SourceArtifactRole.GeneratedSource,
                _ => throw new InvalidOperationException("Unsupported selected source artifact role."),
            }, asset.LogicalAssetId, asset.Sha256))
            .OrderBy(static artifact => artifact.Role)
            .ThenBy(static artifact => artifact.RelativeArtifactId, StringComparer.Ordinal)
            .ToArray();

    public static SourceArtifact GetSingle(this IReadOnlyList<SourceArtifact> artifacts, SourceArtifactRole role)
        => artifacts.Single(artifact => artifact.Role == role);

    public static string ComputeRoleHash(this IReadOnlyList<SourceArtifact> artifacts, SourceArtifactRole role)
        => CanonicalHash.Sha256(CanonicalHash.Join(artifacts.Where(artifact => artifact.Role == role)
            .OrderBy(static artifact => artifact.RelativeArtifactId, StringComparer.Ordinal)
            .Select(static artifact => $"{artifact.RelativeArtifactId}={artifact.Sha256}")));

    public static string ComputeDependencyEvidenceHash(this IReadOnlyList<SourceArtifact> artifacts)
        => CanonicalHash.Sha256(CanonicalHash.Join(artifacts.Where(static artifact =>
                artifact.Role is SourceArtifactRole.DependencyArtifact or SourceArtifactRole.SelectedAssetsManifest or SourceArtifactRole.ResolvedDependencyManifest)
            .OrderBy(static artifact => artifact.Role)
            .ThenBy(static artifact => artifact.RelativeArtifactId, StringComparer.Ordinal)
            .Select(static artifact => $"{artifact.Role}:{artifact.RelativeArtifactId}={artifact.Sha256}")));
}

public sealed record BenchmarkSourceIdentity(
    BenchmarkSourceMode Mode,
    string Commit,
    string? BundleId,
    string? BundleSha256,
    string ArtifactManifestHash,
    ResolvedDependencyManifest ResolvedDependencies,
    IReadOnlyList<SourceArtifact> Artifacts)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ReleaseEligible => Mode == BenchmarkSourceMode.PackageConsumer;

    [System.Text.Json.Serialization.JsonIgnore]
    public string IdentityHash => CanonicalHash.Sha256(CanonicalHash.Join(
        new[]
        {
            Mode.ToString(), Commit, BundleId ?? string.Empty, BundleSha256 ?? string.Empty,
            ArtifactManifestHash, ResolvedDependencies.ContentSha256,
        }.Concat(Artifacts.OrderBy(static artifact => artifact.Role)
            .ThenBy(static artifact => artifact.RelativeArtifactId, StringComparer.Ordinal)
            .Select(static artifact => $"{artifact.Role}:{artifact.RelativeArtifactId}={artifact.Sha256}"))));

    public static BenchmarkSourceIdentity Project(
        string commit,
        string artifactManifestHash,
        ResolvedDependencyManifest resolvedDependencies,
        IReadOnlyList<SourceArtifact> artifacts)
        => new(BenchmarkSourceMode.ProjectReference, commit, null, null, artifactManifestHash, resolvedDependencies, artifacts);

    public static BenchmarkSourceIdentity Package(
        string bundleId,
        string bundleSha256,
        string artifactManifestHash,
        ResolvedDependencyManifest resolvedDependencies,
        IReadOnlyList<SourceArtifact> artifacts,
        string commit)
        => new(BenchmarkSourceMode.PackageConsumer, commit, bundleId, bundleSha256, artifactManifestHash, resolvedDependencies, artifacts);
}

public sealed record BenchmarkCaseKey(
    string ContractVersion,
    string WorkloadId,
    string Provider,
    string OperationSemantics,
    string DataTier,
    int Cardinality,
    BufferingMode Buffering,
    ConnectionLifecycle ConnectionLifecycle,
    PoolingMode Pooling,
    PreparationMode Preparation,
    TemperatureMode Temperature,
    TrackingMode Tracking,
    CompilationMode Compilation,
    ApiStyle ApiStyle,
    string TimedBoundaryHash,
    TransactionMode Transaction,
    string Competitor,
    int CompetitorMajor,
    string RuntimeTfm,
    string RuntimeIdentifier,
    BenchmarkJobKind JobKind,
    MetricFamily MetricFamily,
    BenchmarkSourceIdentity Source)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string StableId => CanonicalHash.Sha256(CanonicalHash.Join(
    [
        ContractVersion,
        WorkloadId,
        Provider,
        OperationSemantics,
        DataTier,
        Cardinality.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Buffering.ToString(),
        ConnectionLifecycle.ToString(),
        Pooling.ToString(),
        Preparation.ToString(),
        Temperature.ToString(),
        Tracking.ToString(),
        Compilation.ToString(),
        ApiStyle.ToString(),
        TimedBoundaryHash,
        Transaction.ToString(),
        Competitor,
        CompetitorMajor.ToString(System.Globalization.CultureInfo.InvariantCulture),
        RuntimeTfm,
        RuntimeIdentifier,
        JobKind.ToString(),
        MetricFamily.ToString(),
        Source.Mode.ToString(),
    ]));

    [System.Text.Json.Serialization.JsonIgnore]
    public string RunIdentityHash => CanonicalHash.Sha256(CanonicalHash.Join([StableId, Source.IdentityHash]));
}

public sealed record TimedPathContract(
    bool IncludesDependencyResolution,
    bool IncludesConnectionCreation,
    bool IncludesConnectionOpen,
    bool IncludesConnectionClose,
    bool IncludesCommandConstruction,
    bool IncludesParameterBinding,
    bool IncludesProjection,
    bool IncludesDuplicateRead,
    bool IncludesMaterializationOrEnumeration,
    bool IncludesPoolingPolicy,
    bool IncludesPreparationPolicy,
    bool IncludesTransactionBegin,
    bool IncludesTransactionCommit)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string IdentityHash => CanonicalHash.Sha256(CanonicalHash.Join(
    [
        IncludesDependencyResolution.ToString(), IncludesConnectionCreation.ToString(),
        IncludesConnectionOpen.ToString(), IncludesConnectionClose.ToString(),
        IncludesCommandConstruction.ToString(), IncludesParameterBinding.ToString(),
        IncludesProjection.ToString(), IncludesDuplicateRead.ToString(),
        IncludesMaterializationOrEnumeration.ToString(), IncludesPoolingPolicy.ToString(),
        IncludesPreparationPolicy.ToString(), IncludesTransactionBegin.ToString(), IncludesTransactionCommit.ToString(),
    ]));
}

public sealed record ExpectedResultContract(
    int Count,
    string Checksum,
    string? ErrorClass,
    TransactionOutcome TransactionOutcome,
    int CommandCount);

public sealed record ProjectedValueContract(string Name, string ClrType, string DatabaseType, int Ordinal, bool Nullable);

public sealed record ScenarioDataContract(
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyList<ProjectedValueContract> Projection,
    string NullSemantics,
    string DuplicateSemantics);

public sealed record MutationResetContract(bool MutatesData, bool ResetOutsideTimedPath)
{
    public static MutationResetContract None { get; } = new(false, false);
}

public sealed record BenchmarkScenario(
    BenchmarkCaseKey Key,
    bool ComparisonEligible,
    bool LoadEligible,
    TimedPathContract TimedPath,
    ScenarioDataContract Data,
    ExpectedResultContract Expected,
    CommandGraph ApprovedCommandGraph,
    MutationResetContract MutationReset)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string IdentityHash => CanonicalHash.Sha256(CanonicalHash.Join(
        new[]
        {
            Key.StableId,
            ComparisonEligible.ToString(),
            LoadEligible.ToString(),
            TimedPath.IdentityHash,
            Data.NullSemantics,
            Data.DuplicateSemantics,
            Expected.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Expected.Checksum,
            Expected.ErrorClass ?? string.Empty,
            Expected.TransactionOutcome.ToString(),
            Expected.CommandCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ApprovedCommandGraph.SemanticHash,
            ApprovedCommandGraph.SqlFingerprint,
            MutationReset.MutatesData.ToString(),
            MutationReset.ResetOutsideTimedPath.ToString(),
        }
        .Concat(Data.Inputs.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"input:{pair.Key}={pair.Value}"))
        .Concat(Data.Projection.OrderBy(static value => value.Ordinal)
            .Select(static value => $"projection:{value.Ordinal}:{value.Name}:{value.ClrType}:{value.DatabaseType}:{value.Nullable}"))));
}

public static class ScenarioValidator
{
    public static IReadOnlyList<ContractError> Validate(
        BenchmarkScenario scenario,
        bool resultHasCompetitor = false,
        bool isLoadResult = false)
    {
        var errors = new List<ContractError>();
        if (!scenario.ComparisonEligible && resultHasCompetitor)
            errors.Add(new("comparison-ineligible", "Competitor results are forbidden for a comparison-ineligible scenario."));
        if (!scenario.LoadEligible && isLoadResult)
            errors.Add(new("load-ineligible", "Load results are forbidden for a load-ineligible scenario."));
        if (scenario.LoadEligible &&
            (!scenario.TimedPath.IncludesCommandConstruction ||
             !scenario.TimedPath.IncludesParameterBinding ||
             !scenario.TimedPath.IncludesMaterializationOrEnumeration))
            errors.Add(new("incomplete-load-boundary", "Load-eligible scenarios must time the complete request-shaped command boundary."));
        if (scenario.Key.Cardinality <= 0)
            errors.Add(new("scenario-cardinality", "Scenario cardinality must be positive."));
        if (string.IsNullOrWhiteSpace(scenario.Key.OperationSemantics) ||
            string.IsNullOrWhiteSpace(scenario.Key.Provider) ||
            string.IsNullOrWhiteSpace(scenario.Key.Competitor) || scenario.Key.CompetitorMajor <= 0)
            errors.Add(new("scenario-dimensions", "Scenario semantic/provider/competitor dimensions must be explicit."));
        if (scenario.Key.ContractVersion != "1" ||
            scenario.Key.JobKind != BenchmarkJobKind.Live ||
            scenario.Key.RuntimeTfm is not ("net8.0" or "net10.0"))
            errors.Add(new("scenario-dimensions", "Scenario contract version, runtime, and job kind must match the checked release contract."));
        var template = CanonicalScenarioCatalog.Templates.SingleOrDefault(candidate => candidate.WorkloadId == scenario.Key.WorkloadId);
        if (template is null || !template.Dimensions.Matches(scenario.Key))
            errors.Add(new("scenario-dimensions", "Case key does not match the checked semantic dimensions for its workload template."));
        var expectedOutcome = scenario.Key.Transaction switch
        {
            TransactionMode.None => TransactionOutcome.None,
            TransactionMode.Committed => TransactionOutcome.Committed,
            TransactionMode.ExistingRollback => TransactionOutcome.RolledBack,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), "Unknown transaction mode."),
        };
        if (scenario.Expected.TransactionOutcome != expectedOutcome ||
            scenario.ApprovedCommandGraph.Commands.Any(command => command.TransactionOutcome != expectedOutcome))
            errors.Add(new("transaction-mode", "Case, expected result, and every command node must declare the same transaction outcome."));
        var mutatesData = scenario.ApprovedCommandGraph.Commands.Any(static command =>
            command.Mutation is MutationEffect.Insert or MutationEffect.Update or MutationEffect.Delete or MutationEffect.BulkWrite);
        if (scenario.MutationReset.MutatesData != mutatesData)
            errors.Add(new("mutation-contract", "Mutation reset metadata must match the approved command effects."));
        if (mutatesData && expectedOutcome == TransactionOutcome.Committed && !scenario.MutationReset.ResetOutsideTimedPath)
            errors.Add(new("mutation-leakage", "Committed mutations require an out-of-band reset contract."));
        var includesOwnedTransaction = scenario.TimedPath.IncludesTransactionBegin && scenario.TimedPath.IncludesTransactionCommit;
        if ((scenario.Key.Transaction == TransactionMode.Committed) != includesOwnedTransaction ||
            (scenario.Key.Transaction != TransactionMode.Committed &&
             (scenario.TimedPath.IncludesTransactionBegin || scenario.TimedPath.IncludesTransactionCommit)))
            errors.Add(new("transaction-boundary", "Timed transaction begin/commit flags must match the case transaction mode."));
        if (scenario.Key.Buffering == BufferingMode.Streaming && !scenario.TimedPath.IncludesMaterializationOrEnumeration)
            errors.Add(new("stream-not-consumed", "Streaming scenarios must consume and checksum the stream inside the timed path."));
        if (scenario.Expected.CommandCount != scenario.ApprovedCommandGraph.Commands.Count)
            errors.Add(new("command-count-contract", "Expected command count does not match the approved command graph."));
        if (scenario.ApprovedCommandGraph.Commands.Count != scenario.ApprovedCommandGraph.SqlStatements.Count)
            errors.Add(new("command-sql-count", "Every semantic command node requires exactly one ordered SQL statement."));
        if (scenario.Key.TimedBoundaryHash != scenario.TimedPath.IdentityHash)
            errors.Add(new("timed-boundary-key", "Case key does not identify the scenario's exact timed boundary."));
        return errors;
    }
}

public sealed record ScenarioDimensions(
    string OperationSemantics,
    int Cardinality,
    BufferingMode Buffering,
    ConnectionLifecycle ConnectionLifecycle,
    PoolingMode Pooling,
    PreparationMode Preparation,
    TemperatureMode Temperature,
    TrackingMode Tracking,
    CompilationMode Compilation,
    ApiStyle ApiStyle,
    TransactionMode Transaction,
    MetricFamily MetricFamily)
{
    public bool Matches(BenchmarkCaseKey key)
        => key.OperationSemantics == OperationSemantics && key.Cardinality == Cardinality &&
           key.Buffering == Buffering && key.ConnectionLifecycle == ConnectionLifecycle &&
           key.Pooling == Pooling && key.Preparation == Preparation && key.Temperature == Temperature &&
           key.Tracking == Tracking && key.Compilation == Compilation && key.ApiStyle == ApiStyle &&
           key.Transaction == Transaction && key.MetricFamily == MetricFamily;
}

public sealed record ScenarioTemplate(
    string WorkloadId,
    ScenarioDimensions Dimensions,
    bool ComparisonEligible,
    bool LoadEligible,
    TimedPathContract TimedPath,
    ScenarioDataContract Data,
    ExpectedResultContract Expected,
    CommandGraph ApprovedCommandGraph,
    MutationResetContract MutationReset)
{
    public BenchmarkScenario Materialize(BenchmarkCaseKey key)
    {
        if (!StringComparer.Ordinal.Equals(key.WorkloadId, WorkloadId))
            throw new ArgumentException("Case key workload does not match the scenario template.", nameof(key));
        if (!Dimensions.Matches(key))
            throw new ArgumentException("Case key semantic dimensions do not match the checked scenario template.", nameof(key));
        if (!Enum.TryParse<Fixtures.FixtureTier>(key.DataTier, true, out _))
            throw new ArgumentException("Case key data tier is not in the canonical fixture catalog.", nameof(key));

        var graph = ApprovedCommandGraph;
        if (graph.Commands.Count != 0)
        {
            graph = graph with { SqlStatements = [ResolveProviderSql(key.Provider)] };
        }

        return new BenchmarkScenario(key, ComparisonEligible, LoadEligible, TimedPath, Data, Expected, graph, MutationReset);
    }

    private static string ResolveProviderSql(string provider) => provider.ToLowerInvariant() switch
    {
        "sqlite" or "postgresql" or "mysql" or "mariadb" =>
            "SELECT CustomerID, CompanyName FROM Customers WHERE CustomerID = @customerId LIMIT 2",
        "sqlserver" =>
            "SELECT TOP (2) CustomerID, CompanyName FROM Customers WHERE CustomerID = @customerId",
        "oracle" =>
            "SELECT CustomerID, CompanyName FROM Customers WHERE CustomerID = :customerId FETCH FIRST 2 ROWS ONLY",
        _ => throw new ArgumentException($"Provider '{provider}' has no approved SQL fingerprint.", nameof(provider)),
    };
}

public static class CanonicalScenarioCatalog
{
    private static readonly TimedPathContract ExactReadBoundary = new(
        IncludesDependencyResolution: true,
        IncludesConnectionCreation: true,
        IncludesConnectionOpen: true,
        IncludesConnectionClose: true,
        IncludesCommandConstruction: true,
        IncludesParameterBinding: true,
        IncludesProjection: true,
        IncludesDuplicateRead: true,
        IncludesMaterializationOrEnumeration: true,
        IncludesPoolingPolicy: true,
        IncludesPreparationPolicy: true,
        IncludesTransactionBegin: false,
        IncludesTransactionCommit: false);

    public static IReadOnlyList<ScenarioTemplate> Templates { get; } =
    [
        new(
            "customer.by-key",
            new("exact-single-with-duplicate-check", 1, BufferingMode.Buffered, ConnectionLifecycle.PerOperation,
                PoolingMode.Pooled, PreparationMode.Unprepared, TemperatureMode.Warm, TrackingMode.NotApplicable,
                CompilationMode.NotApplicable, ApiStyle.ExactOverhead, TransactionMode.None, MetricFamily.Latency),
            ComparisonEligible: true,
            LoadEligible: true,
            ExactReadBoundary,
            new ScenarioDataContract(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["customerId"] = "00001" },
                [
                    new("CustomerID", "System.String", "varchar(5)", 0, false),
                    new("CompanyName", "System.String", "varchar(40)", 1, false),
                ],
                "database-null maps to nullable CLR values; no sentinel substitution",
                "read at most two rows and fail on duplicates"),
            new ExpectedResultContract(
                1,
                CanonicalHash.Sha256(CanonicalHash.Join(["00001", "Company 000001"])),
                null,
                TransactionOutcome.None,
                1),
            new CommandGraph(
                [new CommandNode(CommandOperation.Select, ["Customers"], [], ["CustomerID = @customerId"], [], 2,
                    ["CustomerID", "CompanyName"], ["String(5)"], ["@customerId"], MutationEffect.None, TransactionOutcome.None)],
                "SELECT CustomerID, CompanyName FROM Customers WHERE CustomerID = @customerId LIMIT 2"),
            MutationResetContract.None),
        new(
            "inquiry.parameter-binding",
            new("bind-eight-parameters", 8, BufferingMode.Buffered, ConnectionLifecycle.Retained,
                PoolingMode.Pooled, PreparationMode.Unprepared, TemperatureMode.Warm, TrackingMode.NotApplicable,
                CompilationMode.NotApplicable, ApiStyle.Micro, TransactionMode.None, MetricFamily.Latency),
            ComparisonEligible: false,
            LoadEligible: false,
            ExactReadBoundary with
            {
                IncludesDependencyResolution = false,
                IncludesConnectionCreation = false,
                IncludesConnectionOpen = false,
                IncludesConnectionClose = false,
                IncludesProjection = false,
                IncludesDuplicateRead = false,
                IncludesMaterializationOrEnumeration = false,
            },
            new ScenarioDataContract(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["parameterCount"] = "8" },
                [], "not applicable", "not applicable"),
            new ExpectedResultContract(8, "binder-checksum-v1", null, TransactionOutcome.None, 0),
            new CommandGraph([], string.Empty),
            MutationResetContract.None),
    ];
}

public sealed record BenchmarkJobContract(
    string Id,
    string RuntimeTfm,
    int LaunchCount,
    int InvocationCount,
    int UnrollFactor,
    int WarmupIterationFloor,
    int MeasurementIterationFloor,
    string ArtifactRoot,
    bool FullJsonExport,
    bool MemoryDiagnoser,
    string ResetCadence,
    string PoolWarmup,
    string ConnectionWarmup,
    PreparationMode Preparation,
    string SetupScope,
    int MinIterationTimeMilliseconds,
    double MaxRelativeError,
    bool EvaluateOverhead,
    string OutlierMode)
{
    public string IdentityHash => CanonicalHash.Sha256(CanonicalHash.Join(
    [
        Id, RuntimeTfm,
        LaunchCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        InvocationCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        UnrollFactor.ToString(System.Globalization.CultureInfo.InvariantCulture),
        WarmupIterationFloor.ToString(System.Globalization.CultureInfo.InvariantCulture),
        MeasurementIterationFloor.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ArtifactRoot, FullJsonExport.ToString(), MemoryDiagnoser.ToString(), ResetCadence, PoolWarmup, ConnectionWarmup,
        Preparation.ToString(), SetupScope,
        MinIterationTimeMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        MaxRelativeError.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        EvaluateOverhead.ToString(), OutlierMode,
    ]));
}

public static class BenchmarkJobCatalog
{
    public const string BenchmarkDotNetVersion = "0.15.8";

    public static IReadOnlyList<BenchmarkJobContract> Jobs { get; } =
    [
        new("net8-live-v1", "net8.0", 16, 1, 1, 3, 15, "artifacts/benchmarks/net8", true, true,
            "scenario-declared", "once-per-launch", "once-per-launch", PreparationMode.Unprepared, "iteration-or-launch-declared",
            100, 0.02, true, "dont-remove"),
        new("net10-live-v1", "net10.0", 16, 1, 1, 3, 15, "artifacts/benchmarks/net10", true, true,
            "scenario-declared", "once-per-launch", "once-per-launch", PreparationMode.Unprepared, "iteration-or-launch-declared",
            100, 0.02, true, "dont-remove"),
    ];

    public static BenchmarkJobContract GetRequired(string id)
        => Jobs.SingleOrDefault(job => StringComparer.Ordinal.Equals(job.Id, id))
           ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown benchmark job contract.");
}
