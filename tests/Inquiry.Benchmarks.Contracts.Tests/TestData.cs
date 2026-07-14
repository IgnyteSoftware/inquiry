using Inquiry.Benchmarks.Contracts;
using Inquiry.Benchmarks.Contracts.Evidence;
using Inquiry.Benchmarks.Contracts.Fixtures;

namespace Inquiry.Benchmarks.Contracts.Tests;

internal static class TestData
{
    public const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string RuntimeIdentifier = "linux-x64";

    public static TimedPathContract TimedPath() => new(true, true, true, true, true, true, true, true, true, true, true, false, false);

    public static IReadOnlyList<SourceArtifact> Artifacts(
        BenchmarkSourceMode mode,
        string provider = "sqlite",
        string runtimeTfm = "net8.0")
    {
        var manifest = Manifest(mode, provider, runtimeTfm);
        var resolved = ResolvedDependencies(mode, provider, runtimeTfm);
        var fixedArtifacts = manifest.ExpectedArtifacts.Select((artifact, index) => new SourceArtifact(
            artifact.Role,
            artifact.RelativeArtifactId,
            artifact.Role == SourceArtifactRole.ResolvedDependencyManifest
                ? resolved.ContentSha256
                : new string("0123456789abcdef"[index % 16], 64)));
        return fixedArtifacts.Concat(resolved.FromSelectedAssets()).ToArray();
    }

    public static ResolvedDependencyManifest ResolvedDependencies(
        BenchmarkSourceMode mode,
        string provider = "sqlite",
        string runtimeTfm = "net8.0")
    {
        var sourceManifest = Manifest(mode, provider, runtimeTfm);
        var projectAssetsIndex = sourceManifest.ExpectedArtifacts.ToList().FindIndex(
            static artifact => artifact.Role == SourceArtifactRole.DependencyArtifact);
        var selectedAssetsIndex = sourceManifest.ExpectedArtifacts.ToList().FindIndex(
            static artifact => artifact.Role == SourceArtifactRole.SelectedAssetsManifest);
        var projectAssetsSha = new string("0123456789abcdef"[projectAssetsIndex % 16], 64);
        var selectedAssetsSha = new string("0123456789abcdef"[selectedAssetsIndex % 16], 64);
        var assets = new List<ResolvedDependencyAsset>
        {
            new("dotnet/packs/Microsoft.NETCore.App.Ref/ref/System.Runtime.dll", ResolvedAssetKind.CompilerReference, "compiler:framework", new string('a', 64)),
            new("nuget/benchmarkdotnet/0.15.8/lib/net8.0/BenchmarkDotNet.dll", ResolvedAssetKind.Runtime, "runtime:BenchmarkDotNet", new string('b', 64)),
            new($"repo/analyzers/Inquiry.{ProviderSuffix(provider)}.Analyzer.dll", ResolvedAssetKind.Analyzer, $"analyzer:Inquiry.{ProviderSuffix(provider)}.Analyzer", new string('c', 64)),
            new("repo/analyzers/Inquiry.Generators.Shared.dll", ResolvedAssetKind.Analyzer, "analyzer:Inquiry.Generators.Shared", new string('5', 64)),
            new("repo/generated/SelectedAssetProbe.InquiryStore.g.cs", ResolvedAssetKind.GeneratedSource, "compiler-generated", new string('6', 64)),
            new("repo/bin/Inquiry.Benchmarks.Tests.dll", ResolvedAssetKind.HostAssembly, "benchmark-host", new string('d', 64)),
            new("repo/bin/Inquiry.dll", ResolvedAssetKind.ProductAssembly, "inquiry-product", new string('e', 64)),
            new($"repo/bin/Inquiry.{ProviderSuffix(provider)}.dll", ResolvedAssetKind.ProductAssembly, "inquiry-product", new string('8', 64)),
            ProviderResolvedAsset(provider),
        };
        if (provider == "sqlite")
            assets.Add(new("nuget/sqlitepclraw.lib.e_sqlite3/3.50.3/runtimes/linux-x64/native/libe_sqlite3.so",
                ResolvedAssetKind.Native, "native:SQLitePCLRaw.lib.e_sqlite3", new string('f', 64)));
        return new(
            ResolvedDependencyManifest.RequiredSelectionRule,
            provider,
            SourceArtifactManifestCatalog.LaneFor(mode),
            runtimeTfm,
            RuntimeIdentifier,
            projectAssetsSha,
            selectedAssetsSha,
            assets);
    }

    private static string ProviderSuffix(string provider) => provider switch
    {
        "sqlite" => "Sqlite",
        "sqlserver" => "SqlServer",
        "postgresql" => "PostgreSql",
        "mysql" => "MySql",
        "mariadb" => "MariaDb",
        "oracle" => "Oracle",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
    };

    private static ResolvedDependencyAsset ProviderResolvedAsset(string provider) => provider switch
    {
        "sqlite" => new("nuget/microsoft.data.sqlite/10.0.9/lib/net8.0/Microsoft.Data.Sqlite.dll", ResolvedAssetKind.Runtime, "runtime:Microsoft.Data.Sqlite", new string('7', 64)),
        "sqlserver" => new("nuget/microsoft.data.sqlclient/7.0.1/lib/net8.0/Microsoft.Data.SqlClient.dll", ResolvedAssetKind.Runtime, "runtime:Microsoft.Data.SqlClient", new string('7', 64)),
        "postgresql" => new("nuget/npgsql/10.0.3/lib/net8.0/Npgsql.dll", ResolvedAssetKind.Runtime, "runtime:Npgsql", new string('7', 64)),
        "mysql" or "mariadb" => new("nuget/mysqlconnector/2.6.1/lib/net8.0/MySqlConnector.dll", ResolvedAssetKind.Runtime, "runtime:MySqlConnector", new string('7', 64)),
        "oracle" => new("nuget/oracle.manageddataaccess.core/23.26.200/lib/net8.0/Oracle.ManagedDataAccess.dll", ResolvedAssetKind.Runtime, "runtime:Oracle.ManagedDataAccess.Core", new string('7', 64)),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
    };

    public static BenchmarkSourceIdentity ProjectSource(string provider = "sqlite", string runtimeTfm = "net8.0")
    {
        var manifest = SourceArtifactManifestCatalog.GetRequired(provider, BenchmarkSourceMode.ProjectReference, runtimeTfm);
        return BenchmarkSourceIdentity.Project(Commit, manifest.IdentityHash,
            ResolvedDependencies(BenchmarkSourceMode.ProjectReference, provider, runtimeTfm),
            Artifacts(BenchmarkSourceMode.ProjectReference, provider, runtimeTfm));
    }

    public static BenchmarkSourceIdentity PackageSource(string provider = "sqlite", string runtimeTfm = "net8.0")
    {
        var manifest = Manifest(BenchmarkSourceMode.PackageConsumer, provider, runtimeTfm);
        return BenchmarkSourceIdentity.Package("rc-bundle", new string('9', 64), manifest.IdentityHash,
            ResolvedDependencies(BenchmarkSourceMode.PackageConsumer, provider, runtimeTfm),
            Artifacts(BenchmarkSourceMode.PackageConsumer, provider, runtimeTfm), Commit);
    }

    public static BenchmarkCaseKey CaseKey(
        BenchmarkSourceIdentity? source = null,
        string provider = "sqlite",
        string runtimeTfm = "net8.0") => new(
        "1", "customer.by-key", provider, "exact-single-with-duplicate-check", "tiny", 1,
        BufferingMode.Buffered, ConnectionLifecycle.PerOperation, PoolingMode.Pooled,
        PreparationMode.Unprepared, TemperatureMode.Warm, TrackingMode.NotApplicable,
        CompilationMode.NotApplicable, ApiStyle.ExactOverhead, TimedPath().IdentityHash,
        TransactionMode.None, "inquiry", 1, runtimeTfm, RuntimeIdentifier,
        BenchmarkJobKind.Live, MetricFamily.Latency, source ?? ProjectSource(provider, runtimeTfm));

    public static CommandGraph Graph() => new(
        [new CommandNode(
            CommandOperation.Select,
            ["Customers"],
            [],
            ["CustomerID = @id"],
            [],
            2,
            ["CustomerID", "CompanyName"],
            ["String(5)"],
            ["@id"],
            MutationEffect.None,
            TransactionOutcome.None)],
        "SELECT CustomerID, CompanyName FROM Customers WHERE CustomerID = @id LIMIT 2");

    public static BenchmarkScenario Scenario(BenchmarkCaseKey? key = null) =>
        CanonicalScenarioCatalog.Templates.Single(static template => template.WorkloadId == "customer.by-key")
            .Materialize(key ?? CaseKey());

    public static ParityObservation Observation() => new(
        1, Scenario().Expected.Checksum, null, TransactionOutcome.None, 1, BufferingMode.Buffered,
        ConnectionLifecycle.PerOperation, PoolingMode.Pooled, PreparationMode.Unprepared,
        Scenario().TimedPath, Scenario().ApprovedCommandGraph);

    public static BenchmarkEvidenceEnvelope Envelope(
        BenchmarkSourceIdentity? source = null,
        string provider = "sqlite",
        string runtimeTfm = "net8.0")
    {
        source ??= ProjectSource(provider, runtimeTfm);
        var key = CaseKey(source, provider, runtimeTfm);
        var fixture = NorthwindFixtureCatalog.For(FixtureTier.Tiny);
        var job = BenchmarkJobCatalog.GetRequired(runtimeTfm == "net8.0" ? "net8-live-v1" : "net10-live-v1");
        var scenario = Scenario(key);
        var observation = new ParityObservation(
            scenario.Expected.Count, scenario.Expected.Checksum, scenario.Expected.ErrorClass,
            scenario.Expected.TransactionOutcome, scenario.Expected.CommandCount, key.Buffering,
            key.ConnectionLifecycle, key.Pooling, key.Preparation, scenario.TimedPath, scenario.ApprovedCommandGraph);
        var parity = new ParityEvidence(scenario, observation);
        var environment = new EnvironmentEvidence(
            "inquiry-benchmark-v1", "linux", "6.8.0", runtimeTfm, RuntimeIdentifier,
            runtimeTfm == "net8.0" ? "8.0.28" : "10.0.9",
            runtimeTfm == "net8.0" ? ".NET 8.0.28" : ".NET 10.0.9", "workstation-gc", "x64-v1",
            "0x123", "single-node", "disabled", "fixed", "bare-metal", "none", "local", "loopback", "healthy");
        var measurements = Enumerable.Range(0, 16)
            .SelectMany(launch => Enumerable.Range(0, 15)
                .Select(iteration => new BenchmarkDotNetMeasurement(launch, iteration, 1, 123.0 + launch + iteration)))
            .ToArray();
        var healthStart = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var collectedAt = healthStart.AddMinutes(30);
        var health = Enumerable.Range(0, 16).Select(launch => new LaunchHealthEvidence(
            launch,
            healthStart.AddMinutes(launch),
            LaunchHealthRuleCatalog.Required.IdentityHash,
            new LaunchHealthMetrics(false, false, launch == 4 ? 7.5 : 1.0, false,
                LaunchHealthEvidence.ComputeCoefficientOfVariation(
                    measurements.Where(measurement => measurement.LaunchIndex == launch)
                        .Select(static measurement => measurement.NanosecondsPerOperation),
                    LaunchHealthRuleCatalog.Required)),
            launch == 4 ? LaunchHealthStatus.CpuContention : LaunchHealthStatus.Healthy)).ToArray();
        return new BenchmarkEvidenceEnvelope(
            EvidenceSchema.Version,
            Authoritative: false,
            key.StableId,
            key,
            new CheckoutEvidence(Commit, true, false),
            source,
            NorthwindFixtureCatalog.SchemaHash,
            fixture.IdentityHash,
            fixture.Seed,
            "release-1.0.0-rc1",
            collectedAt,
            source.Artifacts.GetSingle(SourceArtifactRole.BenchmarkConfigFile).Sha256,
            job.IdentityHash,
            source.Artifacts.ComputeDependencyEvidenceHash(),
            parity.IdentityHash,
            observation.CommandGraph.SqlFingerprint,
            environment.IdentityHash,
            parity,
            provider == "sqlite"
                ? new DatabaseEvidence(null, "SQLite 3.50.3", "3.50.3", ["ENABLE_FTS5", "THREADSAFE=1"], "in-process")
                : new DatabaseEvidence(DatabaseImageCatalog.GetRequired(provider).Digest, "checked-server-version", null, [], "single-container"),
            environment,
            BenchmarkAggregationCatalog.Required,
            LaunchHealthRuleCatalog.Required,
            health,
            new BenchmarkDotNetEvidence(
                "0.15.8", job.Id, 16, 3, 15, 1, 1, 100, 0.02, true, "dont-remove",
                new BenchmarkDotNetGcStats(15, 64, BenchmarkAggregationCatalog.GcStatsProvenance, true),
                measurements.Select(static measurement => measurement.NanosecondsPerOperation).ToArray(),
                measurements,
                [new("Mean", "137.500 ns"), new("Median", "137.500 ns"), new("Allocated", "64.000 B")]),
            new ResultEvidence(137.5, "ns", 64, "B/op"));
    }

    public static CheckedBaselineVector Baseline()
    {
        var evidence = Envelope();
        var excludedHealth = evidence.LaunchHealth.Single(static health => health.LaunchIndex == 4);
        string[] familyMembers = [evidence.CaseId];
        return new(
            evidence.CampaignId,
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero),
            16,
            evidence.CaseId,
            evidence.EnvironmentHash,
            BaselineFamilyIdentity.Compute(familyMembers),
            0,
            Enumerable.Range(0, 16).Where(static index => index != 4)
                .Select(index => new BaselineLaunchSample(index, BenchmarkStatistics.Median(
                    evidence.BenchmarkDotNet.Measurements.Where(measurement => measurement.LaunchIndex == index)
                        .Select(static measurement => measurement.NanosecondsPerOperation)))).ToArray(),
            [new InvalidSample(4, excludedHealth.Status, excludedHealth.IdentityHash)],
            familyMembers,
            RelativeBudget: 0.05,
            AbsoluteBudget: 5,
            ComparatorVersion: "1",
            BootstrapSeed: 872026,
            Approval: new BaselineApproval("baseline-1", Commit, ["reviewer-a", "reviewer-b"]));
    }

    private static SourceArtifactManifest Manifest(BenchmarkSourceMode mode, string provider, string runtimeTfm)
    {
        if (mode == BenchmarkSourceMode.ProjectReference)
            return SourceArtifactManifestCatalog.GetRequired(provider, mode, runtimeTfm);

        var project = SourceArtifactManifestCatalog.GetRequired(provider, BenchmarkSourceMode.ProjectReference, runtimeTfm);
        var packages = SourceArtifactManifestCatalog.PackageIds.Select(static id =>
            new SourceArtifactExpectation(SourceArtifactRole.Package, $"packages/{id}.nupkg"));
        return project with
        {
            Id = $"unproduced-package-candidate/{provider}/{runtimeTfm}/{RuntimeIdentifier}",
            Lane = BenchmarkSourceLane.ReleaseCandidatePackage,
            ExpectedArtifacts = packages.Concat(project.ExpectedArtifacts).ToArray(),
        };
    }
}
