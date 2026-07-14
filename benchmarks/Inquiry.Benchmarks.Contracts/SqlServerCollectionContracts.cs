using System.Text.Json;
using Inquiry.Benchmarks.Contracts.Evidence;
using Inquiry.Benchmarks.Contracts.Fixtures;
using Json.Schema;

namespace Inquiry.Benchmarks.Contracts;

public enum SqlServerCollectionTransport { Tvp, OpenJson, ScalarExpansion }

public sealed record SqlServerCollectionScenario(
    SqlServerCollectionTransport Transport,
    int Cardinality,
    BufferingMode Buffering,
    ConnectionLifecycle ConnectionLifecycle,
    PoolingMode Pooling,
    PreparationMode Preparation,
    TemperatureMode Temperature,
    TransactionMode Transaction,
    int ProjectedColumnCount,
    int CommandCount);

public static class SqlServerCollectionScenarioCatalog
{
    public static IReadOnlyList<int> Cardinalities { get; } = [1, 10, 100, 1_000];

    public static IReadOnlyList<SqlServerCollectionScenario> Scenarios { get; } =
        Enum.GetValues<SqlServerCollectionTransport>()
            .SelectMany(transport => Cardinalities.Select(cardinality => new SqlServerCollectionScenario(
                transport, cardinality, BufferingMode.Buffered, ConnectionLifecycle.PerOperation,
                PoolingMode.Pooled, PreparationMode.Unprepared, TemperatureMode.Warm,
                TransactionMode.None, ProjectedColumnCount: 10, CommandCount: 1)))
            .ToArray();

    public static IReadOnlyList<ContractError> Validate(SqlServerCollectionScenario scenario)
    {
        var errors = new List<ContractError>();
        if (!Cardinalities.Contains(scenario.Cardinality))
            errors.Add(new("collection-cardinality", "Collection cardinality must be one of 1, 10, 100, or 1000."));
        if (scenario.Buffering != BufferingMode.Buffered ||
            scenario.ConnectionLifecycle != ConnectionLifecycle.PerOperation ||
            scenario.Pooling != PoolingMode.Pooled ||
            scenario.Preparation != PreparationMode.Unprepared ||
            scenario.Temperature != TemperatureMode.Warm ||
            scenario.Transaction != TransactionMode.None ||
            scenario.ProjectedColumnCount != 10 || scenario.CommandCount != 1)
            errors.Add(new("collection-semantics", "Collection scenarios must preserve the checked SQL Server read boundary."));
        return errors;
    }
}

public static class SqlServerCollectionEvidenceSchema
{
    public const string Version = "inquiry-sqlserver-collection-evidence-v1";
}

public sealed record SqlServerCollectionLogicalReadEvidence(
    SqlServerCollectionTransport Transport,
    int Cardinality,
    long LastLogicalReads,
    long ExecutionCount,
    string QueryHash,
    string PlanHash);

public sealed record SqlServerCollectionPlanEvidence(
    SqlServerCollectionTransport Transport,
    int Cardinality,
    string SqlHash,
    string ParameterSignature,
    string QueryHash,
    string PlanHash,
    int CachedPlanCount);

public sealed record SqlServerCollectionEvidence(
    string SchemaVersion,
    DateTimeOffset CollectedAtUtc,
    string ImageDigest,
    string FixtureIdentityHash,
    IReadOnlyList<SqlServerCollectionLogicalReadEvidence> LogicalReads,
    IReadOnlyList<SqlServerCollectionPlanEvidence> PlanStability);

public static class SqlServerCollectionEvidenceValidator
{
    private static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        var assembly = typeof(SqlServerCollectionEvidenceValidator).Assembly;
        var name = assembly.GetManifestResourceNames().Single(static value =>
            value.EndsWith("sqlserver-collection-evidence.schema.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing SQL Server collection evidence schema.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    });

    public static IReadOnlyList<ContractError> Validate(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var schema = Schema.Value.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!schema.IsValid)
                return [new("json-schema", "SQL Server collection evidence does not match the closed schema.")];
            var evidence = document.RootElement.Deserialize<SqlServerCollectionEvidence>(EvidenceJson.Options);
            return evidence is null ? [new("deserialize", "SQL Server collection evidence deserialized to null.")] : Validate(evidence);
        }
        catch (JsonException)
        {
            return [new("invalid-json", "SQL Server collection evidence is not valid JSON.")];
        }
    }

    public static IReadOnlyList<ContractError> Validate(SqlServerCollectionEvidence evidence)
    {
        var errors = new List<ContractError>();
        if (evidence.SchemaVersion != SqlServerCollectionEvidenceSchema.Version ||
            evidence.CollectedAtUtc.Offset != TimeSpan.Zero || !IsHash(evidence.FixtureIdentityHash) ||
            !DatabaseImageCatalog.GetRequired("sqlserver").Digest.Equals(evidence.ImageDigest, StringComparison.Ordinal))
            errors.Add(new("collection-envelope", "Collection evidence must identify the checked schema, UTC collection time, fixture, and pinned image."));

        var expected = SqlServerCollectionScenarioCatalog.Scenarios
            .Select(static item => (item.Transport, item.Cardinality)).ToHashSet();
        var logical = evidence.LogicalReads.Select(static item => (item.Transport, item.Cardinality)).ToArray();
        var plans = evidence.PlanStability.Select(static item => (item.Transport, item.Cardinality)).ToArray();
        if (logical.Length != expected.Count || logical.Distinct().Count() != logical.Length || !expected.SetEquals(logical) ||
            plans.Length != expected.Count || plans.Distinct().Count() != plans.Length || !expected.SetEquals(plans))
            errors.Add(new("collection-matrix", "Logical-read and plan-stability evidence must exactly cover the 3 by 4 checked matrix."));
        if (evidence.LogicalReads.Any(static item => item.LastLogicalReads < 0 || item.ExecutionCount != 2 ||
                !IsHash(item.QueryHash) || !IsHash(item.PlanHash)))
            errors.Add(new("collection-logical-reads", "Logical-read evidence must contain non-negative reads, exactly one prime plus one measured execution, and canonical hashes."));
        if (evidence.PlanStability.Any(static item => item.CachedPlanCount <= 0 || !IsHash(item.SqlHash) ||
                !IsHash(item.ParameterSignature) || !IsHash(item.QueryHash) || !IsHash(item.PlanHash)))
            errors.Add(new("collection-plans", "Plan evidence must contain cached-plan counts and canonical hashes."));

        var expectedCachedPlanCounts = new Dictionary<int, int> { [1] = 1, [10] = 2, [100] = 3, [1_000] = 4 };
        if (evidence.PlanStability.Any(item => item.CachedPlanCount !=
                (item.Transport == SqlServerCollectionTransport.ScalarExpansion
                    ? expectedCachedPlanCounts.GetValueOrDefault(item.Cardinality)
                    : 1)))
            errors.Add(new("collection-plan-counts", "TVP and OPENJSON require one cached plan; scalar expansion requires cumulative counts 1, 2, 3, and 4."));

        foreach (var transport in new[] { SqlServerCollectionTransport.Tvp, SqlServerCollectionTransport.OpenJson })
        {
            var transportPlans = evidence.PlanStability.Where(item => item.Transport == transport).ToArray();
            var structuralShapes = transportPlans.Select(static item => (item.SqlHash, item.ParameterSignature)).Distinct().Count();
            var compiledShapes = transportPlans.Select(static item => (item.QueryHash, item.PlanHash)).Distinct().Count();
            if (structuralShapes != 1 || compiledShapes != 1)
                errors.Add(new("collection-plan-stability", $"{transport} must retain one SQL, parameter, query, and plan shape across cardinalities."));
        }
        if (evidence.PlanStability.Where(static item => item.Transport == SqlServerCollectionTransport.ScalarExpansion)
            .Select(static item => (item.SqlHash, item.ParameterSignature)).Distinct().Count() != 4)
            errors.Add(new("collection-scalar-shapes", "Scalar expansion must retain exactly four cardinality-dependent structural shapes."));
        return errors;
    }

    private static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
