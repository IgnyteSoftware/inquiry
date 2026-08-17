using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Inquiry.Benchmarks.Contracts.Fixtures;
using Json.Schema;

namespace Inquiry.Benchmarks.Contracts.Evidence;

public static class EvidenceSchema
{
    public const string Version = "inquiry-benchmark-evidence-v2";
    public const string LegacyVersion = "inquiry-benchmark-evidence-v1";
}

public static class EvidenceLimits
{
    public const int MaxShardBytes = 1024 * 1024;
    public const int MaxCheckedEvidenceBytes = 25 * 1024 * 1024;
}

public static class EvidenceJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public sealed record CheckoutEvidence(string Commit, bool IsClean, bool HasUntrackedFiles);

public sealed record EnvironmentEvidence(
    string RunnerClass,
    string OperatingSystem,
    string Kernel,
    string RuntimeTfm,
    string RuntimeIdentifier,
    string RuntimeVersion,
    string RuntimeDescription,
    string GarbageCollector,
    string CpuClass,
    string Microcode,
    string Numa,
    string TurboPolicy,
    string PowerPolicy,
    string Virtualization,
    string DockerLimits,
    string DockerStorage,
    string DockerNetwork,
    string BackgroundLoadHealth)
{
    [JsonIgnore]
    public string IdentityHash => CanonicalHash.Sha256(CanonicalHash.Join(
    [
        RunnerClass, OperatingSystem, Kernel, RuntimeTfm, RuntimeIdentifier, RuntimeVersion, RuntimeDescription, GarbageCollector, CpuClass, Microcode, Numa,
        TurboPolicy, PowerPolicy, Virtualization, DockerLimits, DockerStorage, DockerNetwork, BackgroundLoadHealth,
    ]));

    [JsonIgnore]
    public IEnumerable<string> Facets =>
    [
        RunnerClass, OperatingSystem, Kernel, RuntimeTfm, RuntimeIdentifier, RuntimeVersion, RuntimeDescription, GarbageCollector, CpuClass, Microcode, Numa,
        TurboPolicy, PowerPolicy, Virtualization, DockerLimits, DockerStorage, DockerNetwork, BackgroundLoadHealth,
    ];
}

public sealed record DatabaseEvidence(
    string? ImageDigest,
    string ServerVersion,
    string? NativeLibraryVersion,
    IReadOnlyList<string> CompileOptions,
    string ResourceTopology);

public sealed record BenchmarkDotNetMeasurement(
    int LaunchIndex,
    int IterationIndex,
    long Operations,
    double NanosecondsPerOperation);
public sealed record BenchmarkDotNetField(string Name, string Value);
public sealed record BenchmarkDotNetGcStats(
    long TotalOperations,
    long? BytesAllocatedPerOperation,
    string Provenance,
    bool MemoryDiagnoserEnabled);

public sealed record BenchmarkAggregationContract(
    string Id,
    string AuthoritativeStatistic,
    string TimingAggregation,
    string MedianAggregation,
    string AllocationStatistic,
    string RawTimingUnit,
    string ResultTimingUnit,
    string ResultAllocationUnit,
    string BenchmarkDotNetTimingUnit,
    string BenchmarkDotNetAllocationUnit,
    int DecimalPlaces,
    string Rounding);

public static class BenchmarkAggregationCatalog
{
    public const string GcStatsProvenance = "BenchmarkDotNet.Engines.GcStats.GetBytesAllocatedPerOperation(BenchmarkCase)/0.15.8/MemoryDiagnoser";
    public const string UnavailableAllocation = "NA";
    public static BenchmarkAggregationContract Required { get; } = new(
        "target-measurements-v1",
        "arithmetic-mean",
        "arithmetic-mean-all-target-measurements",
        "median-all-target-measurements",
        "gc-stats-bytes-allocated-per-operation",
        "nanoseconds-per-operation",
        "ns",
        "B/op",
        "ns",
        "B",
        3,
        "to-even");
}

public static class BenchmarkStatistics
{
    public static double Round(double value)
        => Math.Round(value, BenchmarkAggregationCatalog.Required.DecimalPlaces, MidpointRounding.ToEven);

    public static double Mean(IEnumerable<double> values)
        => Round(values.Average());

    public static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0) throw new ArgumentException("At least one measurement is required.", nameof(values));
        var middle = ordered.Length / 2;
        return Round(ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle]);
    }

    public static string FormatBenchmarkDotNet(double value, string unit)
        => $"{Round(value).ToString($"F{BenchmarkAggregationCatalog.Required.DecimalPlaces}", System.Globalization.CultureInfo.InvariantCulture)} {unit}";
}

public enum LaunchHealthStatus
{
    Healthy,
    ThermalThrottling,
    PowerThrottling,
    CpuContention,
    DockerResourceDrift,
    NoiseBudgetExceeded,
}

public sealed record LaunchHealthRuleContract(
    string Id,
    double MaximumCpuContentionPercent,
    double MaximumCoefficientOfVariation,
    string CoefficientOfVariationMethod,
    int MetricDecimalPlaces)
{
    [JsonIgnore]
    public string IdentityHash => CanonicalHash.Sha256(CanonicalHash.Join(
    [
        Id,
        MaximumCpuContentionPercent.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        MaximumCoefficientOfVariation.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        CoefficientOfVariationMethod,
        MetricDecimalPlaces.ToString(System.Globalization.CultureInfo.InvariantCulture),
    ]));
}

public static class LaunchHealthRuleCatalog
{
    public static LaunchHealthRuleContract Required { get; } = new(
        "launch-health-v1", 5.0, 0.10, "sample-standard-deviation-over-mean", 6);
}

public sealed record LaunchHealthMetrics(
    bool ThermalThrottlingDetected,
    bool PowerThrottlingDetected,
    double CpuContentionPercent,
    bool DockerResourceDriftDetected,
    double CoefficientOfVariation);

public sealed record LaunchHealthEvidence(
    int LaunchIndex,
    DateTimeOffset CollectedAtUtc,
    string RuleIdentityHash,
    LaunchHealthMetrics Metrics,
    LaunchHealthStatus Status)
{
    [JsonIgnore]
    public string IdentityHash => CanonicalHash.Sha256(CanonicalHash.Join(
    [
        LaunchIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
        CollectedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        RuleIdentityHash,
        Metrics.ThermalThrottlingDetected.ToString(),
        Metrics.PowerThrottlingDetected.ToString(),
        Metrics.CpuContentionPercent.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        Metrics.DockerResourceDriftDetected.ToString(),
        Metrics.CoefficientOfVariation.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        Status.ToString(),
    ]));

    public static LaunchHealthStatus DeriveStatus(LaunchHealthMetrics metrics, LaunchHealthRuleContract rule)
    {
        if (metrics.ThermalThrottlingDetected) return LaunchHealthStatus.ThermalThrottling;
        if (metrics.PowerThrottlingDetected) return LaunchHealthStatus.PowerThrottling;
        if (metrics.CpuContentionPercent > rule.MaximumCpuContentionPercent) return LaunchHealthStatus.CpuContention;
        if (metrics.DockerResourceDriftDetected) return LaunchHealthStatus.DockerResourceDrift;
        if (metrics.CoefficientOfVariation > rule.MaximumCoefficientOfVariation) return LaunchHealthStatus.NoiseBudgetExceeded;
        return LaunchHealthStatus.Healthy;
    }

    public static double ComputeCoefficientOfVariation(
        IEnumerable<double> values,
        LaunchHealthRuleContract rule)
    {
        var samples = values.ToArray();
        if (samples.Length < 2) throw new ArgumentException("At least two measurements are required.", nameof(values));
        var mean = samples.Average();
        if (!double.IsFinite(mean) || mean <= 0) return 0;
        var sampleVariance = samples.Sum(value => Math.Pow(value - mean, 2)) / (samples.Length - 1);
        return Math.Round(Math.Sqrt(sampleVariance) / mean, rule.MetricDecimalPlaces, MidpointRounding.ToEven);
    }
}

public static class BenchmarkDotNetFieldCatalog
{
    public static IReadOnlySet<string> ResultNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Mean", "Median", "Allocated",
    };
}

public sealed record BenchmarkDotNetEvidence(
    string Version,
    string JobId,
    int LaunchCount,
    int WarmupIterations,
    int MeasurementIterations,
    int InvocationCount,
    int UnrollFactor,
    int MinIterationTimeMilliseconds,
    double MaxRelativeError,
    bool EvaluateOverhead,
    string OutlierMode,
    BenchmarkDotNetGcStats GcStats,
    IReadOnlyList<double> RawStatistics,
    IReadOnlyList<BenchmarkDotNetMeasurement> Measurements,
    IReadOnlyList<BenchmarkDotNetField> ResultFields);

public sealed record ResultEvidence(double Value, string Unit, long? AllocatedBytes, string AllocationUnit);

public sealed record ParityEvidence(BenchmarkScenario Scenario, ParityObservation Observation)
{
    [JsonIgnore]
    public string IdentityHash => CanonicalHash.Sha256(CanonicalHash.Join([Scenario.IdentityHash, Observation.IdentityHash]));
}

public sealed record BenchmarkTargetEvidence(
    string AssemblyName,
    string TypeName,
    string MethodName,
    int Cardinality,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record BenchmarkEvidenceEnvelope(
    string SchemaVersion,
    bool Authoritative,
    string CaseId,
    BenchmarkCaseKey CaseKey,
    CheckoutEvidence Checkout,
    BenchmarkSourceIdentity Source,
    string SchemaHash,
    string DatasetHash,
    int Seed,
    string CampaignId,
    DateTimeOffset CollectedAtUtc,
    string BenchmarkConfigFileSha256,
    string BenchmarkJobContractHash,
    string DependencyHash,
    string ParityHash,
    string SqlFingerprint,
    string EnvironmentHash,
    BenchmarkTargetEvidence BenchmarkTarget,
    IReadOnlyDictionary<string, string> RuntimeCapabilities,
    ParityEvidence Parity,
    DatabaseEvidence Database,
    EnvironmentEvidence Environment,
    BenchmarkAggregationContract Aggregation,
    LaunchHealthRuleContract LaunchHealthRule,
    IReadOnlyList<LaunchHealthEvidence> LaunchHealth,
    BenchmarkDotNetEvidence BenchmarkDotNet,
    ResultEvidence Result);

public static class EvidenceValidator
{
    public static IReadOnlyList<ContractError> Validate(
        BenchmarkEvidenceEnvelope evidence,
        BenchmarkJobContract? nonAuthoritativeJobOverride = null)
    {
        var errors = new List<ContractError>();
        AddIf(evidence.SchemaVersion != EvidenceSchema.Version, "schema-version", "Evidence schema version is not supported.");
        AddIf(string.IsNullOrWhiteSpace(evidence.BenchmarkTarget.AssemblyName) ||
              string.IsNullOrWhiteSpace(evidence.BenchmarkTarget.TypeName) ||
              string.IsNullOrWhiteSpace(evidence.BenchmarkTarget.MethodName) ||
              evidence.BenchmarkTarget.Parameters.Any(static parameter =>
                  string.IsNullOrWhiteSpace(parameter.Key) || string.IsNullOrWhiteSpace(parameter.Value)),
            "benchmark-target", "Evidence must identify the exact benchmark assembly, type, method, and parameter values.");
        AddIf(evidence.BenchmarkTarget.Cardinality != evidence.CaseKey.Cardinality ||
              evidence.BenchmarkTarget.Cardinality <= 0,
            "benchmark-target", "Benchmark target cardinality must equal the positive canonical case cardinality.");
        if (evidence.BenchmarkTarget.Parameters.TryGetValue("Rows", out var rows))
        {
            AddIf(!int.TryParse(rows, System.Globalization.NumberStyles.None,
                      System.Globalization.CultureInfo.InvariantCulture, out var rowsCardinality) ||
                  rowsCardinality <= 0 || rowsCardinality != evidence.BenchmarkTarget.Cardinality,
                "benchmark-target", "Benchmark target Rows, when present, must be a positive invariant integer equal to target cardinality.");
        }
        AddIf(evidence.RuntimeCapabilities.Count == 0 || evidence.RuntimeCapabilities.Any(static capability =>
                string.IsNullOrWhiteSpace(capability.Key) || string.IsNullOrWhiteSpace(capability.Value)),
            "runtime-capability", "At least one runtime capability with a non-empty name and observed value is required.");
        AddIf(evidence.CaseId != evidence.CaseKey.StableId, "case-key", "Evidence case ID does not match the canonical case key.");
        AddIf(evidence.CaseKey.Source.IdentityHash != evidence.Source.IdentityHash, "source-identity", "Case and evidence source identities differ.");
        AddIf(!StringComparer.Ordinal.Equals(evidence.Checkout.Commit, evidence.Source.Commit) || !IsGitCommit(evidence.Source.Commit),
            "source-commit", "Checkout and source must identify the same immutable Git commit SHA.");
        AddIf(evidence.SchemaHash != NorthwindFixtureCatalog.SchemaHash, "schema-hash", "Evidence schema hash does not match the checked fixture schema.");
        FixtureManifest? fixture = null;
        if (Enum.TryParse<FixtureTier>(evidence.CaseKey.DataTier, true, out var fixtureTier))
        {
            fixture = NorthwindFixtureCatalog.For(fixtureTier);
            AddIf(evidence.Seed != fixture.Seed || !StringComparer.Ordinal.Equals(evidence.DatasetHash, fixture.IdentityHash),
                "dataset-identity", "Evidence seed and dataset hash must match the checked fixture tier.");
        }
        else
        {
            errors.Add(new("dataset-tier", "Evidence data tier is not in the checked fixture catalog."));
        }
        AddIf(!IsSha256(evidence.DatasetHash), "dataset-hash", "Dataset hash must be a lowercase SHA-256 value.");
        AddIf(!IsSha256(evidence.BenchmarkConfigFileSha256), "config-file-hash", "Benchmark config file hash must be a lowercase SHA-256 value.");
        AddIf(!IsSha256(evidence.BenchmarkJobContractHash), "job-contract-hash", "Benchmark job contract hash must be a lowercase SHA-256 value.");
        AddIf(!IsSha256(evidence.DependencyHash), "dependency-hash", "Resolved dependency evidence hash must be a lowercase SHA-256 value.");
        AddIf(!IsSha256(evidence.ParityHash) || !StringComparer.Ordinal.Equals(evidence.ParityHash, evidence.Parity.IdentityHash),
            "parity-hash", "Parity hash must identify the retained scenario and observation.");
        AddIf(!IsSha256(evidence.SqlFingerprint) ||
              !StringComparer.Ordinal.Equals(evidence.SqlFingerprint, evidence.Parity.Observation.CommandGraph.SqlFingerprint),
            "sql-fingerprint", "Evidence SQL fingerprint must identify the retained observed command graph.");
        AddIf(!IsSha256(evidence.EnvironmentHash) ||
              !StringComparer.Ordinal.Equals(evidence.EnvironmentHash, evidence.Environment.IdentityHash),
            "environment-hash", "Evidence environment hash must identify every checked environment facet.");
        AddIf(string.IsNullOrWhiteSpace(evidence.CampaignId) || evidence.CollectedAtUtc.Offset != TimeSpan.Zero,
            "campaign-evidence", "Evidence requires a campaign identity and UTC collection timestamp.");
        AddIf(evidence.Parity.Scenario.Key.StableId != evidence.CaseId,
            "parity-case", "Retained parity scenario does not match the evidence case.");
        var template = CanonicalScenarioCatalog.Templates.SingleOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.WorkloadId, evidence.CaseKey.WorkloadId));
        if (template is null)
        {
            errors.Add(new("scenario-catalog", "Evidence workload is not in the checked scenario catalog."));
        }
        else
        {
            try
            {
                var canonicalScenario = template.Materialize(evidence.CaseKey);
                AddIf(!StringComparer.Ordinal.Equals(canonicalScenario.IdentityHash, evidence.Parity.Scenario.IdentityHash),
                    "scenario-catalog", "Retained parity scenario does not match the checked provider/tier scenario contract.");
            }
            catch (ArgumentException)
            {
                errors.Add(new("scenario-catalog", "Evidence provider/tier cannot materialize a checked scenario contract."));
            }
        }
        errors.AddRange(ScenarioValidator.Validate(evidence.Parity.Scenario));
        errors.AddRange(ParityValidator.Validate(evidence.Parity.Scenario, evidence.Parity.Observation));
        AddIf(evidence.Authoritative && (!evidence.Checkout.IsClean || evidence.Checkout.HasUntrackedFiles),
            "dirty-checkout", "Authoritative evidence requires a clean checkout with no untracked files.");
        AddIf(evidence.Authoritative && !evidence.Source.ReleaseEligible,
            "source-mode", "Authoritative release evidence must use the immutable package-consumer source mode.");
        errors.AddRange(ValidateSourceArtifacts(evidence.Source, evidence.CaseKey.Provider,
            evidence.CaseKey.RuntimeTfm, evidence.CaseKey.RuntimeIdentifier));
        var configArtifact = evidence.Source.Artifacts.Where(static artifact => artifact.Role == SourceArtifactRole.BenchmarkConfigFile).ToArray();
        AddIf(configArtifact.Length != 1 || !StringComparer.Ordinal.Equals(evidence.BenchmarkConfigFileSha256, configArtifact.SingleOrDefault()?.Sha256),
            "config-source", "Benchmark config file hash must equal the exact config artifact bytes hash.");
        AddIf(!StringComparer.Ordinal.Equals(evidence.DependencyHash,
                evidence.Source.Artifacts.ComputeDependencyEvidenceHash()),
            "dependency-source", "Dependency hash must identify the exact checked resolved-dependency evidence artifact set.");
        if (evidence.Source.Mode == BenchmarkSourceMode.PackageConsumer)
        {
            AddIf(string.IsNullOrWhiteSpace(evidence.Source.BundleId) || !IsSha256(evidence.Source.BundleSha256),
                "package-identity", "Package-consumer evidence requires immutable bundle ID and SHA-256.");
        }
        else
        {
            AddIf(evidence.Source.BundleId is not null || evidence.Source.BundleSha256 is not null,
                "package-identity", "Project-reference evidence must not declare a package bundle identity.");
        }
        AddIf(evidence.Aggregation != BenchmarkAggregationCatalog.Required,
            "aggregation-contract", "Evidence does not declare the exact checked statistic, aggregation, unit, precision, and rounding contract.");
        AddIf(evidence.Environment.Facets.Any(string.IsNullOrWhiteSpace),
            "environment", "Every environment topology and health facet is required.");
        AddIf(string.IsNullOrWhiteSpace(evidence.Database.ServerVersion) ||
              string.IsNullOrWhiteSpace(evidence.Database.ResourceTopology) ||
              evidence.Database.CompileOptions.Any(string.IsNullOrWhiteSpace),
            "database-topology", "Database server version, topology, and every reported compile option must be non-empty.");
        if (evidence.CaseKey.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
        {
            AddIf(evidence.Database.ImageDigest is not null ||
                  string.IsNullOrWhiteSpace(evidence.Database.NativeLibraryVersion) || evidence.Database.CompileOptions.Count == 0,
                "database-provenance", "SQLite evidence requires native-library version and compile options.");
        }
        else
        {
            var image = DatabaseImageCatalog.Images.SingleOrDefault(candidate =>
                StringComparer.OrdinalIgnoreCase.Equals(candidate.Provider, evidence.CaseKey.Provider));
            AddIf(image is null || !StringComparer.Ordinal.Equals(evidence.Database.ImageDigest, image.Digest) ||
                  string.IsNullOrWhiteSpace(evidence.Database.ServerVersion),
                "database-provenance", "Server evidence requires the catalog-pinned image digest and reported server version.");
        }
        AddIf(nonAuthoritativeJobOverride is not null && evidence.Authoritative,
            "bdn-job", "Authoritative evidence cannot override the checked benchmark job catalog.");
        var job = nonAuthoritativeJobOverride is not null &&
                  StringComparer.Ordinal.Equals(nonAuthoritativeJobOverride.Id, evidence.BenchmarkDotNet.JobId)
            ? nonAuthoritativeJobOverride
            : BenchmarkJobCatalog.Jobs.SingleOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, evidence.BenchmarkDotNet.JobId));
        if (job is null)
        {
            errors.Add(new("bdn-job", "Evidence references an unknown benchmark job."));
        }
        else
        {
            AddIf(evidence.CaseKey.JobKind != BenchmarkJobKind.Live ||
                  !StringComparer.Ordinal.Equals(evidence.CaseKey.RuntimeTfm, job.RuntimeTfm) ||
                  !StringComparer.Ordinal.Equals(evidence.Environment.RuntimeTfm, job.RuntimeTfm) ||
                  !StringComparer.Ordinal.Equals(evidence.CaseKey.RuntimeIdentifier, evidence.Environment.RuntimeIdentifier) ||
                  !StringComparer.Ordinal.Equals(evidence.CaseKey.RuntimeIdentifier, evidence.Source.ResolvedDependencies.RuntimeIdentifier) ||
                  !StringComparer.Ordinal.Equals(evidence.BenchmarkJobContractHash, job.IdentityHash) ||
                  evidence.BenchmarkDotNet.Version != BenchmarkJobCatalog.BenchmarkDotNetVersion ||
                  evidence.BenchmarkDotNet.LaunchCount != job.LaunchCount ||
                  evidence.BenchmarkDotNet.InvocationCount != job.InvocationCount ||
                  evidence.BenchmarkDotNet.UnrollFactor != job.UnrollFactor ||
                  evidence.BenchmarkDotNet.WarmupIterations != job.WarmupIterationFloor ||
                  evidence.BenchmarkDotNet.MeasurementIterations != job.MeasurementIterationFloor ||
                  evidence.BenchmarkDotNet.MinIterationTimeMilliseconds != job.MinIterationTimeMilliseconds ||
                  evidence.BenchmarkDotNet.MaxRelativeError != job.MaxRelativeError ||
                  evidence.BenchmarkDotNet.EvaluateOverhead != job.EvaluateOverhead ||
                  evidence.BenchmarkDotNet.GcStats.MemoryDiagnoserEnabled != job.MemoryDiagnoser ||
                  !StringComparer.Ordinal.Equals(evidence.BenchmarkDotNet.OutlierMode, job.OutlierMode),
                "bdn-contract", "Evidence does not match the exact checked runtime/job/accuracy contract.");
        }
        var expectedMeasurementCount64 = (long)evidence.BenchmarkDotNet.LaunchCount * evidence.BenchmarkDotNet.MeasurementIterations;
        var expectedMeasurementCount = expectedMeasurementCount64 is > 0 and <= int.MaxValue
            ? (int)expectedMeasurementCount64
            : -1;
        var measurementCoordinates = evidence.BenchmarkDotNet.Measurements
            .Select(static measurement => (measurement.LaunchIndex, measurement.IterationIndex)).ToArray();
        var expectedCoordinates = expectedMeasurementCount < 0
            ? []
            : Enumerable.Range(0, evidence.BenchmarkDotNet.LaunchCount)
                .SelectMany(launch => Enumerable.Range(0, evidence.BenchmarkDotNet.MeasurementIterations)
                    .Select(iteration => (launch, iteration)))
                .ToArray();
        AddIf(evidence.BenchmarkDotNet.RawStatistics.Count != expectedMeasurementCount ||
              evidence.BenchmarkDotNet.RawStatistics.Any(static value => !double.IsFinite(value) || value < 0) ||
              evidence.BenchmarkDotNet.Measurements.Count != expectedMeasurementCount ||
              !evidence.BenchmarkDotNet.RawStatistics.SequenceEqual(
                  evidence.BenchmarkDotNet.Measurements.Select(static measurement => measurement.NanosecondsPerOperation)) ||
              evidence.BenchmarkDotNet.Measurements.Any(static measurement =>
                  measurement.LaunchIndex < 0 || measurement.IterationIndex < 0 || measurement.Operations <= 0 ||
                  !double.IsFinite(measurement.NanosecondsPerOperation) || measurement.NanosecondsPerOperation < 0) ||
              evidence.BenchmarkDotNet.Measurements.Any(measurement =>
                  measurement.LaunchIndex >= evidence.BenchmarkDotNet.LaunchCount ||
                  measurement.IterationIndex >= evidence.BenchmarkDotNet.MeasurementIterations) ||
              measurementCoordinates.Distinct().Count() != expectedMeasurementCount ||
              !measurementCoordinates.SequenceEqual(expectedCoordinates) ||
              evidence.BenchmarkDotNet.ResultFields.Count == 0 ||
              evidence.BenchmarkDotNet.ResultFields.Any(static field =>
                  string.IsNullOrWhiteSpace(field.Name) || string.IsNullOrWhiteSpace(field.Value)),
            "bdn-fields", "Evidence must retain exact measurements and non-empty canonical BDN result fields.");
        var resultNames = evidence.BenchmarkDotNet.ResultFields.Select(static field => field.Name).ToArray();
        AddIf(resultNames.Length != BenchmarkDotNetFieldCatalog.ResultNames.Count ||
              resultNames.Distinct(StringComparer.Ordinal).Count() != resultNames.Length ||
              !resultNames.ToHashSet(StringComparer.Ordinal).SetEquals(BenchmarkDotNetFieldCatalog.ResultNames),
            "bdn-field-name", "BDN result fields must contain exactly Mean, Median, and Allocated; unvalidated exported fields are forbidden.");
        errors.AddRange(ValidateComputedStatistics(evidence));
        var healthIndices = evidence.LaunchHealth.Select(static health => health.LaunchIndex).ToArray();
        AddIf(evidence.LaunchHealthRule != LaunchHealthRuleCatalog.Required,
            "launch-health-rule", "Launch health thresholds and deterministic derivation rule must match the checked version.");
        AddIf(evidence.LaunchHealth.Count != evidence.BenchmarkDotNet.LaunchCount ||
              !healthIndices.SequenceEqual(Enumerable.Range(0, evidence.BenchmarkDotNet.LaunchCount)) ||
              healthIndices.Distinct().Count() != evidence.BenchmarkDotNet.LaunchCount ||
              evidence.LaunchHealth.Any(static health => health.CollectedAtUtc.Offset != TimeSpan.Zero ||
                  !double.IsFinite(health.Metrics.CpuContentionPercent) || health.Metrics.CpuContentionPercent < 0 ||
                  !double.IsFinite(health.Metrics.CoefficientOfVariation) || health.Metrics.CoefficientOfVariation < 0) ||
              evidence.LaunchHealth.Any(health => health.CollectedAtUtc > evidence.CollectedAtUtc ||
                  !StringComparer.Ordinal.Equals(health.RuleIdentityHash, evidence.LaunchHealthRule.IdentityHash) ||
                  health.Status != LaunchHealthEvidence.DeriveStatus(health.Metrics, evidence.LaunchHealthRule)),
            "launch-health", "Evidence must retain one ordered UTC launch audit whose status is deterministically derived from checked sanitized metrics and rule identity.");
        var expectedHealthMeasurements = job?.MeasurementIterationFloor ?? -1;
        AddIf(evidence.LaunchHealth.Any(health =>
        {
            var launchMeasurements = evidence.BenchmarkDotNet.Measurements
                .Where(measurement => measurement.LaunchIndex == health.LaunchIndex).ToArray();
            return launchMeasurements.Length != expectedHealthMeasurements || launchMeasurements.Length < 2 ||
                   health.Metrics.CoefficientOfVariation != LaunchHealthEvidence.ComputeCoefficientOfVariation(
                       launchMeasurements.Select(static measurement => measurement.NanosecondsPerOperation),
                       evidence.LaunchHealthRule);
        }), "launch-health-metrics",
            "Each launch coefficient of variation must be recomputed from its exact retained target measurements using the checked rule.");
        return errors;

        void AddIf(bool condition, string code, string message)
        {
            if (condition) errors.Add(new(code, message));
        }
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsGitCommit(string? value)
        => value is { Length: 40 or 64 } && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static IReadOnlyList<ContractError> ValidateComputedStatistics(BenchmarkEvidenceEnvelope evidence)
    {
        var errors = new List<ContractError>();
        if (evidence.BenchmarkDotNet.Measurements.Count == 0)
        {
            errors.Add(new("result-statistics", "At least one raw measurement is required to recompute authoritative statistics."));
            return errors;
        }

        var timingMean = BenchmarkStatistics.Mean(evidence.BenchmarkDotNet.Measurements
            .Select(static measurement => measurement.NanosecondsPerOperation));
        var timingMedian = BenchmarkStatistics.Median(evidence.BenchmarkDotNet.Measurements
            .Select(static measurement => measurement.NanosecondsPerOperation));
        var gcStats = evidence.BenchmarkDotNet.GcStats;
        var allocation = gcStats.BytesAllocatedPerOperation;
        if (gcStats.TotalOperations <= 0 || allocation < 0 || !gcStats.MemoryDiagnoserEnabled ||
            !StringComparer.Ordinal.Equals(gcStats.Provenance, BenchmarkAggregationCatalog.GcStatsProvenance))
            errors.Add(new("allocation-provenance",
                "Allocation must retain BDN 0.15.8 GcStats.TotalOperations and nullable GetBytesAllocatedPerOperation(BenchmarkCase) output with MemoryDiagnoser provenance."));
        if (evidence.CaseKey.MetricFamily == MetricFamily.Allocation && allocation is null)
            errors.Add(new("allocation-unavailable", "Allocation benchmark evidence cannot use an unavailable BDN allocation result."));
        var requiredNames = new[] { "Mean", "Median", "Allocated" };
        var fields = evidence.BenchmarkDotNet.ResultFields;
        var mandatoryUnique = requiredNames.All(name => fields.Count(field => field.Name == name) == 1) &&
                              fields.Select(static field => field.Name).Distinct(StringComparer.Ordinal).Count() == fields.Count;
        if (!mandatoryUnique)
        {
            errors.Add(new("result-fields", "BDN Mean, Median, and Allocated fields are mandatory and every result field name must be unique."));
            return errors;
        }

        var contract = BenchmarkAggregationCatalog.Required;
        var fieldsMatch = MatchesBenchmarkDotNetField(fields.Single(static field => field.Name == "Mean").Value,
                              timingMean, contract.BenchmarkDotNetTimingUnit, contract.DecimalPlaces) &&
                          MatchesBenchmarkDotNetField(fields.Single(static field => field.Name == "Median").Value,
                              timingMedian, contract.BenchmarkDotNetTimingUnit, contract.DecimalPlaces) &&
                          MatchesNullableAllocationField(fields.Single(static field => field.Name == "Allocated").Value,
                              allocation, contract.BenchmarkDotNetAllocationUnit, contract.DecimalPlaces);
        if (!fieldsMatch)
            errors.Add(new("result-fields", "BDN Mean, Median, and Allocated must exactly match recomputed raw measurements and checked display units/rounding."));
        if (!double.IsFinite(evidence.Result.Value) ||
            evidence.Result.Value != timingMean || evidence.Result.AllocatedBytes != allocation ||
            !StringComparer.Ordinal.Equals(evidence.Result.Unit, contract.ResultTimingUnit) ||
            !StringComparer.Ordinal.Equals(evidence.Result.AllocationUnit, contract.ResultAllocationUnit))
            errors.Add(new("result-statistics", "Result evidence must equal recomputed authoritative timing/allocation statistics and checked units."));
        return errors;
    }

    private static bool MatchesNullableAllocationField(string value, long? expected, string unit, int decimalPlaces)
        => expected is null
            ? StringComparer.Ordinal.Equals(value, BenchmarkAggregationCatalog.UnavailableAllocation)
            : MatchesBenchmarkDotNetField(value, expected.Value, unit, decimalPlaces);

    private static bool MatchesBenchmarkDotNetField(
        string value,
        double expected,
        string expectedUnit,
        int decimalPlaces)
    {
        var separator = value.LastIndexOf(' ');
        if (separator <= 0 || separator == value.Length - 1 ||
            !StringComparer.Ordinal.Equals(value[(separator + 1)..], expectedUnit))
            return false;
        var number = value[..separator];
        var decimalPoint = number.IndexOf('.');
        if (decimalPoint <= 0 || number.Length - decimalPoint - 1 != decimalPlaces ||
            number[(decimalPoint + 1)..].Any(static character => !char.IsAsciiDigit(character)) ||
            !double.TryParse(number, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return false;
        var rounded = Math.Round(expected, decimalPlaces, MidpointRounding.ToEven);
        return parsed == rounded && StringComparer.Ordinal.Equals(number,
            rounded.ToString($"F{decimalPlaces}", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static IReadOnlyList<ContractError> ValidateSourceArtifacts(
        BenchmarkSourceIdentity source,
        string provider,
        string runtimeTfm,
        string runtimeIdentifier)
    {
        var errors = new List<ContractError>();
        var duplicate = source.Artifacts.GroupBy(
                static artifact => $"{(int)artifact.Role}:{artifact.RelativeArtifactId}",
                StringComparer.OrdinalIgnoreCase)
            .Any(static group => group.Count() != 1);
        if (duplicate) errors.Add(new("source-artifact-identity", "Source artifact role/id pairs must be unique."));
        if (source.Artifacts.Any(static artifact => !IsSafeRelativeArtifactId(artifact.RelativeArtifactId) ||
                !IsSha256(artifact.Sha256)))
            errors.Add(new("source-artifact", "Every source artifact needs a safe relative ID and lowercase SHA-256."));

        SourceArtifactManifest? manifest = null;
        try
        {
            manifest = SourceArtifactManifestCatalog.GetRequired(provider, source.Mode, runtimeTfm, runtimeIdentifier);
        }
        catch (ArgumentOutOfRangeException)
        {
            errors.Add(new("source-artifact-manifest", "Source provider/lane/TFM has no checked artifact manifest."));
        }
        if (manifest is not null)
        {
            var selectedArtifacts = source.ResolvedDependencies.FromSelectedAssets();
            var expected = manifest.ExpectedArtifacts.Select(static artifact =>
                    (artifact.Role, artifact.RelativeArtifactId))
                .Concat(selectedArtifacts.Select(static artifact => (artifact.Role, artifact.RelativeArtifactId)))
                .OrderBy(static artifact => artifact.Role)
                .ThenBy(static artifact => artifact.RelativeArtifactId, StringComparer.Ordinal).ToArray();
            var actual = source.Artifacts.Select(static artifact =>
                (artifact.Role, artifact.RelativeArtifactId)).OrderBy(static artifact => artifact.Role)
                .ThenBy(static artifact => artifact.RelativeArtifactId, StringComparer.Ordinal).ToArray();
            if (!StringComparer.Ordinal.Equals(source.ArtifactManifestHash, manifest.IdentityHash) ||
                !actual.SequenceEqual(expected))
                errors.Add(new("source-artifact-manifest",
                    "Source artifacts must exactly equal the checked provider/lane/TFM manifest; extras, omissions, and substitutions are forbidden."));
            var actualSelectedArtifacts = source.Artifacts
                .Where(static artifact => artifact.Role is SourceArtifactRole.RuntimeAssembly or
                    SourceArtifactRole.AnalyzerAssembly or SourceArtifactRole.GeneratedSource)
                .OrderBy(static artifact => artifact.Role)
                .ThenBy(static artifact => artifact.RelativeArtifactId, StringComparer.Ordinal)
                .ToArray();
            if (!actualSelectedArtifacts.SequenceEqual(selectedArtifacts))
                errors.Add(new("selected-asset-binding",
                    "Runtime/product/provider, analyzer, and generated source claims must retain the exact logical IDs and physical hashes emitted by the selected-assets manifest."));
            errors.AddRange(ValidateResolvedDependencies(source, manifest));
        }
        return errors;
    }

    private static IReadOnlyList<ContractError> ValidateResolvedDependencies(
        BenchmarkSourceIdentity source,
        SourceArtifactManifest sourceManifest)
    {
        var errors = new List<ContractError>();
        var resolved = source.ResolvedDependencies;
        var projectAssets = source.Artifacts.Where(static artifact =>
            artifact.Role == SourceArtifactRole.DependencyArtifact).ToArray();
        var manifestArtifact = source.Artifacts.Where(static artifact =>
            artifact.Role == SourceArtifactRole.ResolvedDependencyManifest).ToArray();
        var selectedAssets = source.Artifacts.Where(static artifact =>
            artifact.Role == SourceArtifactRole.SelectedAssetsManifest).ToArray();
        var duplicateAssets = resolved.Assets.GroupBy(
                static asset => $"{(int)asset.Kind}:{asset.LogicalAssetId}",
                StringComparer.OrdinalIgnoreCase)
            .Any(static group => group.Count() != 1);
        var invalidAsset = resolved.Assets.Any(static asset =>
            !IsSafeRelativeArtifactId(asset.LogicalAssetId) || string.IsNullOrWhiteSpace(asset.Provenance) ||
            asset.Provenance != asset.Provenance.Trim() || asset.Provenance.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_' or ':' or '/' or '+')) ||
            !IsSha256(asset.Sha256) ||
            asset.Kind switch
            {
                ResolvedAssetKind.CompilerReference or ResolvedAssetKind.Runtime or ResolvedAssetKind.Analyzer or
                    ResolvedAssetKind.HostAssembly or ResolvedAssetKind.ProductAssembly =>
                    !asset.LogicalAssetId.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
                ResolvedAssetKind.Native =>
                    !asset.LogicalAssetId.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                    !asset.LogicalAssetId.EndsWith(".so", StringComparison.OrdinalIgnoreCase) &&
                    !asset.LogicalAssetId.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) &&
                    !asset.LogicalAssetId.EndsWith(".a", StringComparison.OrdinalIgnoreCase),
                ResolvedAssetKind.GeneratedSource =>
                    !asset.LogicalAssetId.EndsWith(".cs", StringComparison.OrdinalIgnoreCase),
                _ => true,
            });
        var requiredKinds = new[]
        {
            ResolvedAssetKind.CompilerReference,
            ResolvedAssetKind.Runtime,
            ResolvedAssetKind.Analyzer,
            ResolvedAssetKind.GeneratedSource,
            ResolvedAssetKind.HostAssembly,
            ResolvedAssetKind.ProductAssembly,
        };
        var providerAssembly = ResolvedDependencyManifestCollector.ProviderAssemblyName(sourceManifest.Provider);
        var providerAnalyzerAssembly = ResolvedDependencyManifestCollector.ProviderAnalyzerAssemblyName(sourceManifest.Provider);
        var invalidPhysicalRole =
            requiredKinds.Any(kind => resolved.Assets.All(asset => asset.Kind != kind)) ||
            resolved.Assets.Any(asset => asset.Kind == ResolvedAssetKind.HostAssembly &&
                !Path.GetFileName(asset.LogicalAssetId).StartsWith("Inquiry.Benchmarks", StringComparison.OrdinalIgnoreCase)) ||
            resolved.Assets.Any(asset => asset.Kind == ResolvedAssetKind.ProductAssembly &&
                !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(asset.LogicalAssetId), "Inquiry.dll") &&
                !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(asset.LogicalAssetId), providerAssembly)) ||
            resolved.Assets.All(asset => asset.Kind != ResolvedAssetKind.ProductAssembly ||
                !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(asset.LogicalAssetId), "Inquiry.dll")) ||
            resolved.Assets.All(asset => asset.Kind != ResolvedAssetKind.ProductAssembly ||
                !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(asset.LogicalAssetId), providerAssembly)) ||
            resolved.Assets.All(asset => asset.Kind != ResolvedAssetKind.Analyzer ||
                !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(asset.LogicalAssetId), providerAnalyzerAssembly));
        if (!StringComparer.Ordinal.Equals(resolved.SelectionRuleId, ResolvedDependencyManifest.RequiredSelectionRule) ||
            !StringComparer.Ordinal.Equals(resolved.SelectionRuleId, sourceManifest.ResolvedDependencyScope) ||
            !StringComparer.Ordinal.Equals(resolved.Provider, sourceManifest.Provider) ||
            resolved.Lane != sourceManifest.Lane ||
            !StringComparer.Ordinal.Equals(resolved.RuntimeTfm, sourceManifest.RuntimeTfm) ||
            !StringComparer.Ordinal.Equals(resolved.RuntimeIdentifier, sourceManifest.RuntimeIdentifier) ||
            resolved.Assets.Count == 0 || duplicateAssets || invalidAsset || invalidPhysicalRole ||
            projectAssets.Length != 1 ||
            !StringComparer.Ordinal.Equals(resolved.ProjectAssetsSha256, projectAssets.SingleOrDefault()?.Sha256) ||
            selectedAssets.Length != 1 ||
            !StringComparer.Ordinal.Equals(resolved.SelectedAssetsManifestSha256, selectedAssets.SingleOrDefault()?.Sha256) ||
            manifestArtifact.Length != 1 ||
            !StringComparer.Ordinal.Equals(resolved.ContentSha256, manifestArtifact.SingleOrDefault()?.Sha256))
            errors.Add(new("resolved-dependency-manifest",
                "Resolved dependency evidence must be the canonical provider/lane/TFM/RID MSBuild-selected compiler/runtime/native/analyzer/generated/host/product manifest with physical content hashes; project.assets.json is provenance only."));
        return errors;
    }

    private static bool IsSafeRelativeArtifactId(string? value)
        => !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
           !value.StartsWith("/", StringComparison.Ordinal) && !value.Contains('\\') &&
           !value.Contains(':') && !value.Contains('?') && !value.Contains('#') &&
           value.Split('/').All(static segment => segment is not ("" or "." or "..") &&
               segment.All(static character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or '+'));

}

public static partial class EvidenceHygieneValidator
{
    public static IReadOnlyList<ContractError> Validate(byte[] json)
    {
        var errors = new List<ContractError>();
        if (json.Length > EvidenceLimits.MaxShardBytes)
            errors.Add(new("shard-size", $"Checked evidence shard exceeds {EvidenceLimits.MaxShardBytes} bytes."));

        try
        {
            using var document = JsonDocument.Parse(json);
            Visit(document.RootElement, "$", errors);
        }
        catch (JsonException)
        {
            errors.Add(new("invalid-json", "Evidence shard is not valid JSON."));
        }

        return errors;
    }

    public static IReadOnlyList<ContractError> ValidateTotalBytes(long totalBytes)
        => totalBytes > EvidenceLimits.MaxCheckedEvidenceBytes
            ? [new("total-size", $"Checked evidence exceeds {EvidenceLimits.MaxCheckedEvidenceBytes} bytes.")]
            : [];

    private static void Visit(JsonElement element, string path, List<ContractError> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var forbiddenKind = ClassifyPropertyName(property.Name);
                // Rows is the single checked BenchmarkDotNet cardinality parameter. Only its
                // row-classification is exempt; sensitive and payload aliases remain forbidden.
                if (path == "$.benchmarkTarget.parameters" && property.Name == "Rows" && forbiddenKind == "row")
                    forbiddenKind = null;
                if (forbiddenKind is not null)
                {
                    var code = forbiddenKind == "row" ? "row-data" : "forbidden-field";
                    errors.Add(new(code, $"Forbidden evidence field at {path}.{property.Name}."));
                }
                ValidateString(property.Name, path + ".<property-name>", errors);
                Visit(property.Value, path + "." + property.Name, errors);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray()) Visit(item, $"{path}[{index++}]", errors);
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString() ?? string.Empty;
            ValidateString(value, path, errors);
        }
    }

    private static void ValidateString(string value, string path, List<ContractError> errors)
    {
        if (ConnectionStringPattern().IsMatch(value) || CredentialUrlPattern().IsMatch(value))
            errors.Add(new("secret-leak", $"Potential credential or connection string at {path}."));
        if (UnixAbsolutePathPattern().IsMatch(value) || WindowsAbsolutePathPattern().IsMatch(value))
            errors.Add(new("absolute-path", $"Absolute filesystem path at {path}."));
        if (UnsafeExceptionPattern().IsMatch(value))
            errors.Add(new("unsafe-exception", $"Unsanitized exception text at {path}."));
    }

    private static string? ClassifyPropertyName(string name)
    {
        var compact = string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        var separated = CamelBoundaryPattern().Replace(name, "$1 $2");
        var tokens = NonIdentifierPattern().Split(separated)
            .Where(static token => token.Length != 0)
            .Select(static token => token.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        if (tokens.Overlaps(["row", "rows", "record", "records", "entity", "entities", "payload"]) ||
            compact.Contains("rowdata", StringComparison.Ordinal) || compact.Contains("rowpayload", StringComparison.Ordinal))
            return "row";
        if (tokens.Overlaps(["user", "username", "userid", "host", "hostname", "machine", "machinename"]) ||
            compact.Contains("password", StringComparison.Ordinal) || compact.Contains("passwd", StringComparison.Ordinal) ||
            compact.Contains("pwd", StringComparison.Ordinal) || compact.Contains("secret", StringComparison.Ordinal) ||
            compact.Contains("credential", StringComparison.Ordinal) || compact.Contains("token", StringComparison.Ordinal) ||
            compact.Contains("apikey", StringComparison.Ordinal) || compact.Contains("connectionstring", StringComparison.Ordinal) ||
            compact.Contains("username", StringComparison.Ordinal) || compact.Contains("userid", StringComparison.Ordinal) ||
            compact.Contains("hostname", StringComparison.Ordinal) || compact.Contains("machinename", StringComparison.Ordinal) ||
            compact.Contains("stacktrace", StringComparison.Ordinal) || compact.Contains("exception", StringComparison.Ordinal))
            return "sensitive";
        return null;
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex CamelBoundaryPattern();

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NonIdentifierPattern();

    [GeneratedRegex(@"(?i)(?:password|passwd|pwd|user(?:\s*id|name)?|uid|host(?:name)?|server|account\s*key|api\s*key)\s*=")]
    private static partial Regex ConnectionStringPattern();

    [GeneratedRegex(@"(?i)^[a-z][a-z0-9+.-]*://[^/@\s:]+:[^/@\s]+@")]
    private static partial Regex CredentialUrlPattern();

    [GeneratedRegex("(?i)(?:^|[\\s\\\"'=({\\[])/(?!/)[^/\\s\\\"'<>)}\\]]+(?:/[^/\\s\\\"'<>)}\\]]+)+")]
    private static partial Regex UnixAbsolutePathPattern();

    [GeneratedRegex("(?i)(?:^|[\\s\\\"'=])(?:[A-Z]:[\\\\/]|\\\\\\\\[^\\\\/\\s]+[\\\\/][^\\\\/\\s]+)")]
    private static partial Regex WindowsAbsolutePathPattern();

    [GeneratedRegex(@"(?i)(?:\b[A-Za-z][A-Za-z0-9.]+Exception\b|\bstack trace\b|\bat\s+[^\r\n]+\s+in\s+[^\r\n]+:line\s+\d+)")]
    private static partial Regex UnsafeExceptionPattern();
}

public sealed record ArtifactValidationResult<T>(T? Artifact, IReadOnlyList<ContractError> Errors)
    where T : class
{
    public bool IsValid => Artifact is not null && Errors.Count == 0;
}

public static class CheckedArtifactSchemas
{
    private static readonly Lazy<JsonSchema> EvidenceSchema = new(() => Load("benchmark-evidence-v2.schema.json"));
    private static readonly Lazy<JsonSchema> BaselineSchema = new(() => Load("checked-baseline.schema.json"));
    private static readonly Lazy<JsonSchema> SelectedStrategySchema = new(() => Load("selected-batch-strategy-v1.schema.json"));

    public static JsonSchema Evidence => EvidenceSchema.Value;
    public static JsonSchema Baseline => BaselineSchema.Value;
    public static JsonSchema SelectedStrategy => SelectedStrategySchema.Value;

    private static JsonSchema Load(string fileName)
    {
        var assembly = typeof(CheckedArtifactSchemas).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing embedded checked schema '{fileName}'.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}

public sealed record EvidenceArtifactValidationContext(
    string ArtifactRoot,
    IReadOnlyList<SelectedAssetRoot> SelectedAssetRoots);

public static class EvidenceArtifactValidator
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static ArtifactValidationResult<BenchmarkEvidenceEnvelope> Validate(
        byte[] json,
        EvidenceArtifactValidationContext? filesystem = null)
    {
        var errors = new List<ContractError>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new(null, [new("invalid-json", "Evidence artifact is not valid JSON.")]);
        }

        using (document)
        {
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion) &&
                schemaVersion.ValueKind == JsonValueKind.String &&
                !StringComparer.Ordinal.Equals(schemaVersion.GetString(), EvidenceSchema.Version))
            {
                var message = StringComparer.Ordinal.Equals(schemaVersion.GetString(), EvidenceSchema.LegacyVersion)
                    ? "Benchmark evidence schema v1 is unsupported; regenerate the artifact using v2."
                    : "Benchmark evidence schema version is not supported.";
                return new(null, [new("schema-version", message)]);
            }

            var schemaResult = CheckedArtifactSchemas.Evidence.Evaluate(
                document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!schemaResult.IsValid)
                errors.Add(new("json-schema", "Evidence artifact does not match the closed checked schema."));
            errors.AddRange(EvidenceHygieneValidator.Validate(json));
            if (errors.Count != 0) return new(null, errors);

            BenchmarkEvidenceEnvelope? artifact;
            try
            {
                artifact = document.RootElement.Deserialize<BenchmarkEvidenceEnvelope>(EvidenceJson.Options);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return new(null, [new("deserialize", "Evidence artifact could not be deserialized after schema validation.")]);
            }

            if (artifact is null) return new(null, [new("deserialize", "Evidence artifact deserialized to null.")]);
            errors.AddRange(EvidenceValidator.Validate(artifact));
            if (errors.Count != 0) return new(artifact, errors);
            if (filesystem is null)
            {
                errors.Add(new("filesystem-context",
                    "Evidence acceptance requires a physical artifact root and approved selected-asset roots."));
                return new(artifact, errors);
            }
            errors.AddRange(ValidatePhysicalArtifacts(artifact, filesystem));
            return new(artifact, errors);
        }
    }

    private static IReadOnlyList<ContractError> ValidatePhysicalArtifacts(
        BenchmarkEvidenceEnvelope evidence,
        EvidenceArtifactValidationContext context)
    {
        try
        {
            var artifactRoot = Path.GetFullPath(context.ArtifactRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(artifactRoot) || IsReparsePoint(artifactRoot))
                return [new("artifact-filesystem", "The physical artifact root must be an existing non-reparse directory.")];

            var paths = evidence.Source.Artifacts.ToDictionary(
                static artifact => artifact,
                artifact => ResolveContainedFile(artifactRoot, artifact.RelativeArtifactId));
            var projectAssets = paths.Single(static item => item.Key.Role == SourceArtifactRole.DependencyArtifact);
            var selectedAssets = paths.Single(static item => item.Key.Role == SourceArtifactRole.SelectedAssetsManifest);
            var resolvedManifest = paths.Single(static item => item.Key.Role == SourceArtifactRole.ResolvedDependencyManifest);

            var physicalManifest = JsonSerializer.Deserialize<ResolvedDependencyManifest>(
                File.ReadAllBytes(resolvedManifest.Value), EvidenceJson.Options)
                ?? throw new InvalidDataException("The resolved dependency manifest deserialized to null.");
            if (!File.ReadAllBytes(resolvedManifest.Value).AsSpan().SequenceEqual(physicalManifest.ToCanonicalJsonBytes()) ||
                !StringComparer.Ordinal.Equals(physicalManifest.ContentSha256, resolvedManifest.Key.Sha256) ||
                !StringComparer.Ordinal.Equals(physicalManifest.ContentSha256, evidence.Source.ResolvedDependencies.ContentSha256) ||
                !StringComparer.Ordinal.Equals(
                    ResolvedDependencyManifestCollector.ComputeCanonicalProjectAssetsSha256(
                        projectAssets.Value, context.SelectedAssetRoots), projectAssets.Key.Sha256) ||
                !ResolvedDependencyManifestCollector.IsExact(
                    physicalManifest, selectedAssets.Value, projectAssets.Value, context.SelectedAssetRoots) ||
                !StringComparer.Ordinal.Equals(physicalManifest.ProjectAssetsSha256, projectAssets.Key.Sha256) ||
                !StringComparer.Ordinal.Equals(physicalManifest.SelectedAssetsManifestSha256, selectedAssets.Key.Sha256))
                return [new("artifact-content", "Physical selected assets and the canonical resolved dependency manifest do not exactly match checked evidence.")];

            foreach (var (artifact, path) in paths.Where(static item => item.Key.Role is not (
                         SourceArtifactRole.DependencyArtifact or SourceArtifactRole.SelectedAssetsManifest or
                         SourceArtifactRole.ResolvedDependencyManifest)))
            {
                var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
                    .ToLowerInvariant();
                if (!StringComparer.Ordinal.Equals(actual, artifact.Sha256))
                    return [new("artifact-content", $"Physical artifact content does not match {artifact.RelativeArtifactId}.")];
            }
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          JsonException or ArgumentException or InvalidOperationException)
        {
            return [new("artifact-filesystem", $"Physical artifact validation failed: {exception.Message}")];
        }
    }

    private static string ResolveContainedFile(string root, string relativeArtifactId)
    {
        var path = Path.GetFullPath(Path.Combine(root,
            relativeArtifactId.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(path))
            throw new InvalidDataException($"Artifact is missing or escapes its physical root: {relativeArtifactId}");
        var current = new FileInfo(path) as FileSystemInfo;
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Artifact path traverses a reparse point: {relativeArtifactId}");
            if (PathComparer.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), root))
                return path;
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
        }
        throw new InvalidDataException($"Artifact is not contained by its physical root: {relativeArtifactId}");
    }

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

public sealed record InvalidSample(int Index, LaunchHealthStatus Status, string HealthEvidenceHash);
public sealed record BaselineLaunchSample(int LaunchIndex, double Median);
public sealed record BaselineApproval(string Id, string Commit, IReadOnlyList<string> Reviewers);

public sealed record CheckedBaselineVector(
    string CampaignId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int ExpectedSampleCount,
    string CaseId,
    string EnvironmentIdentity,
    string FamilyIdentity,
    int FamilyOrder,
    IReadOnlyList<BaselineLaunchSample> LaunchSamples,
    IReadOnlyList<InvalidSample> InvalidSamples,
    IReadOnlyList<string> FamilyMembers,
    double? RelativeBudget,
    double? AbsoluteBudget,
    string ComparatorVersion,
    int BootstrapSeed,
    BaselineApproval Approval);

public static class BaselineFamilyIdentity
{
    public const string ContractVersion = "baseline-family-v1";

    public static string Compute(IReadOnlyList<string> orderedMembers)
        => CanonicalHash.Sha256(CanonicalHash.Join(new[] { ContractVersion }.Concat(orderedMembers)));
}

public static class BaselineValidator
{
    public static IReadOnlyList<ContractError> Validate(
        CheckedBaselineVector baseline,
        string? expectedCaseId = null,
        string? expectedEnvironmentIdentity = null,
        int? expectedLaunchCount = null)
    {
        var errors = new List<ContractError>();
        AddIf(string.IsNullOrWhiteSpace(baseline.CampaignId) ||
              baseline.WindowStartUtc.Offset != TimeSpan.Zero || baseline.WindowEndUtc.Offset != TimeSpan.Zero ||
              baseline.WindowStartUtc >= baseline.WindowEndUtc,
            "baseline-campaign", "Baseline must identify a campaign and a non-empty UTC collection window.");
        AddIf(baseline.ExpectedSampleCount <= 0,
            "baseline-sample-count", "Baseline expected sample count must be positive.");
        AddIf(baseline.LaunchSamples.Count == 0 || baseline.LaunchSamples.Any(static sample =>
                !double.IsFinite(sample.Median) || sample.Median < 0),
            "baseline-vector", "Baseline must retain a non-empty exact vector of finite launch/window medians.");
        AddIf(baseline.RelativeBudget is null || baseline.AbsoluteBudget is null || baseline.RelativeBudget < 0 || baseline.AbsoluteBudget < 0,
            "baseline-budget", "Baseline must retain explicit non-negative relative and absolute budgets.");
        AddIf(baseline.InvalidSamples.Any(static sample => sample.Status == LaunchHealthStatus.Healthy ||
                !IsSha256(sample.HealthEvidenceHash)),
            "invalid-sample-health", "Every excluded sample must identify a non-healthy audited launch-health record.");
        var retainedIndices = baseline.LaunchSamples.Select(static sample => sample.LaunchIndex).ToArray();
        var excludedIndices = baseline.InvalidSamples.Select(static sample => sample.Index).ToArray();
        var allIndices = retainedIndices.Concat(excludedIndices).ToArray();
        AddIf(retainedIndices.Distinct().Count() != retainedIndices.Length ||
              excludedIndices.Distinct().Count() != excludedIndices.Length ||
              retainedIndices.Intersect(excludedIndices).Any() ||
              allIndices.Any(index => index < 0 || index >= baseline.ExpectedSampleCount) ||
              allIndices.Length != baseline.ExpectedSampleCount ||
              !allIndices.Order().SequenceEqual(Enumerable.Range(0, baseline.ExpectedSampleCount)),
            "baseline-sample-coverage", "Retained and excluded samples must uniquely and exactly cover every expected launch index.");
        var familyMembers = baseline.FamilyMembers;
        AddIf(familyMembers.Count == 0 ||
              familyMembers.Any(static member => !IsSha256(member)) ||
              familyMembers.Distinct(StringComparer.Ordinal).Count() != familyMembers.Count ||
              !familyMembers.SequenceEqual(familyMembers.Order(StringComparer.Ordinal)) ||
              baseline.FamilyOrder < 0 || baseline.FamilyOrder >= familyMembers.Count ||
              (baseline.FamilyOrder >= 0 && baseline.FamilyOrder < familyMembers.Count &&
               !StringComparer.Ordinal.Equals(familyMembers[baseline.FamilyOrder], baseline.CaseId)) ||
              !StringComparer.Ordinal.Equals(baseline.FamilyIdentity, BaselineFamilyIdentity.Compute(familyMembers)),
            "baseline-family", "Baseline family identity must hash the exact unique ordinal-sorted case membership, and familyOrder must point to this case ID.");
        AddIf(baseline.EnvironmentIdentity is not { Length: 64 } ||
              baseline.EnvironmentIdentity.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')),
            "baseline-environment", "Baseline environment identity must be a canonical SHA-256 hash.");
        AddIf(string.IsNullOrWhiteSpace(baseline.ComparatorVersion) || baseline.BootstrapSeed == 0,
            "baseline-comparator", "Baseline must retain comparator version and deterministic bootstrap seed.");
        AddIf(string.IsNullOrWhiteSpace(baseline.Approval.Id) || baseline.Approval.Id != baseline.Approval.Id.Trim() ||
              !IsGitCommit(baseline.Approval.Commit) ||
              baseline.Approval.Reviewers.Any(static reviewer => string.IsNullOrWhiteSpace(reviewer) || reviewer != reviewer.Trim()) ||
              baseline.Approval.Reviewers.Distinct(StringComparer.Ordinal).Count() < 2,
            "baseline-approval", "Checked baseline requires a trimmed approval identity, an exact lowercase Git SHA, and two nonblank distinct reviewers.");
        AddIf(expectedCaseId is not null && !StringComparer.Ordinal.Equals(baseline.CaseId, expectedCaseId),
            "baseline-case", "Baseline case identity does not match the evidence comparison case.");
        AddIf(expectedEnvironmentIdentity is not null && !StringComparer.Ordinal.Equals(baseline.EnvironmentIdentity, expectedEnvironmentIdentity),
            "baseline-environment", "Baseline environment identity does not match the canonical evidence environment hash.");
        AddIf(expectedLaunchCount is not null && baseline.ExpectedSampleCount != expectedLaunchCount,
            "baseline-sample-count", "Baseline expected sample count does not match the retained launch evidence.");
        return errors;

        void AddIf(bool condition, string code, string message)
        {
            if (condition) errors.Add(new(code, message));
        }
    }

    public static IReadOnlyList<ContractError> Validate(
        CheckedBaselineVector baseline,
        BenchmarkEvidenceEnvelope evidence)
    {
        var errors = new List<ContractError>(Validate(
            baseline, evidence.CaseId, evidence.EnvironmentHash, evidence.BenchmarkDotNet.LaunchCount));
        if (!StringComparer.Ordinal.Equals(baseline.CampaignId, evidence.CampaignId) ||
            evidence.CollectedAtUtc < baseline.WindowStartUtc || evidence.CollectedAtUtc > baseline.WindowEndUtc)
            errors.Add(new("baseline-campaign-evidence",
                "Baseline campaign and UTC window must contain the bound evidence collection."));
        if (!StringComparer.Ordinal.Equals(baseline.Approval.Commit, evidence.Checkout.Commit) ||
            !StringComparer.Ordinal.Equals(baseline.Approval.Commit, evidence.Source.Commit))
            errors.Add(new("baseline-approval", "Baseline approval must commit to the exact evidence checkout and source Git SHA."));
        if (evidence.LaunchHealth.Any(health =>
                health.CollectedAtUtc < baseline.WindowStartUtc || health.CollectedAtUtc > baseline.WindowEndUtc))
            errors.Add(new("baseline-health-window", "Every launch health audit must fall inside the baseline collection window."));
        var job = BenchmarkJobCatalog.Jobs.SingleOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, evidence.BenchmarkDotNet.JobId));
        var expectedIterations = job?.MeasurementIterationFloor ?? -1;

        foreach (var sample in baseline.LaunchSamples)
        {
            var measurements = evidence.BenchmarkDotNet.Measurements
                .Where(measurement => measurement.LaunchIndex == sample.LaunchIndex).ToArray();
            if (measurements.Length == 0 || measurements.Length != expectedIterations ||
                sample.Median != BenchmarkStatistics.Median(measurements.Select(static measurement => measurement.NanosecondsPerOperation)))
                errors.Add(new("baseline-launch-median",
                    "Every retained launch median must be recomputed exactly from its complete raw target measurements."));
            var health = evidence.LaunchHealth.Where(item => item.LaunchIndex == sample.LaunchIndex).ToArray();
            if (health.Length != 1 || health[0].Status != LaunchHealthStatus.Healthy)
                errors.Add(new("baseline-health-contradiction",
                    "A retained launch must have exactly one healthy audited launch-health record."));
        }

        foreach (var sample in baseline.InvalidSamples)
        {
            var health = evidence.LaunchHealth.Where(item => item.LaunchIndex == sample.Index).ToArray();
            if (health.Length != 1 || health[0].Status == LaunchHealthStatus.Healthy ||
                health[0].Status != sample.Status ||
                !StringComparer.Ordinal.Equals(health[0].IdentityHash, sample.HealthEvidenceHash))
                errors.Add(new("baseline-exclusion-evidence",
                    "Every excluded launch must bind the exact matching non-healthy launch-health audit."));
        }
        return errors;
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsGitCommit(string? value)
        => value is { Length: 40 or 64 } && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public static class BaselineArtifactValidator
{
    public static ArtifactValidationResult<CheckedBaselineVector> Validate(
        byte[] json,
        BenchmarkEvidenceEnvelope evidence)
    {
        var errors = new List<ContractError>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new(null, [new("invalid-json", "Baseline artifact is not valid JSON.")]);
        }

        using (document)
        {
            var schemaResult = CheckedArtifactSchemas.Baseline.Evaluate(
                document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!schemaResult.IsValid)
                errors.Add(new("json-schema", "Baseline artifact does not match the closed checked schema."));
            errors.AddRange(EvidenceHygieneValidator.Validate(json));
            if (errors.Count != 0) return new(null, errors);

            CheckedBaselineVector? artifact;
            try
            {
                artifact = document.RootElement.Deserialize<CheckedBaselineVector>(EvidenceJson.Options);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return new(null, [new("deserialize", "Baseline artifact could not be deserialized after schema validation.")]);
            }
            if (artifact is null) return new(null, [new("deserialize", "Baseline artifact deserialized to null.")]);
            errors.AddRange(EvidenceValidator.Validate(evidence));
            errors.AddRange(BaselineValidator.Validate(artifact, evidence));
            return new(artifact, errors);
        }
    }
}
