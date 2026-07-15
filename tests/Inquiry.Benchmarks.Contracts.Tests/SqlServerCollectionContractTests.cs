using System.Text.Json;
using Inquiry.Benchmarks.Contracts;
using Inquiry.Benchmarks.Contracts.Evidence;
using Inquiry.Benchmarks.Contracts.Fixtures;

namespace Inquiry.Benchmarks.Contracts.Tests;

public sealed class SqlServerCollectionContractTests
{
    [Fact]
    public void ScenarioCatalogExactlyCoversTheCheckedThreeByFourMatrix()
    {
        Assert.Equal(12, SqlServerCollectionScenarioCatalog.Scenarios.Count);
        Assert.Equal(12, SqlServerCollectionScenarioCatalog.Scenarios
            .Select(static item => (item.Transport, item.Cardinality)).Distinct().Count());
        Assert.All(SqlServerCollectionScenarioCatalog.Scenarios,
            static scenario => Assert.Empty(SqlServerCollectionScenarioCatalog.Validate(scenario)));
    }

    [Fact]
    public void ScenarioValidatorRejectsBoundaryDrift()
    {
        var scenario = SqlServerCollectionScenarioCatalog.Scenarios[0];
        Assert.Contains(SqlServerCollectionScenarioCatalog.Validate(scenario with
        {
            Cardinality = 2,
            ConnectionLifecycle = ConnectionLifecycle.Retained,
            ProjectedColumnCount = 9,
        }), static error => error.Code == "collection-cardinality");
        Assert.Contains(SqlServerCollectionScenarioCatalog.Validate(scenario with
        {
            Cardinality = 2,
            ConnectionLifecycle = ConnectionLifecycle.Retained,
            ProjectedColumnCount = 9,
        }), static error => error.Code == "collection-semantics");
    }

    [Fact]
    public void ServerEvidenceRequiresExactMatrixAndStableStructuredShapes()
    {
        var evidence = ValidEvidence();
        var json = JsonSerializer.SerializeToUtf8Bytes(evidence, EvidenceJson.Options);
        Assert.Empty(SqlServerCollectionEvidenceValidator.Validate(json));

        var drifted = evidence with
        {
            LogicalReads = evidence.LogicalReads.Skip(1).ToArray(),
            PlanStability = evidence.PlanStability.Select(item => item.Transport == SqlServerCollectionTransport.Tvp
                ? item with { SqlHash = Hash(item.Cardinality.ToString()) }
                : item).ToArray(),
        };
        var codes = SqlServerCollectionEvidenceValidator.Validate(drifted).Select(static error => error.Code).ToHashSet();
        Assert.Contains("collection-matrix", codes);
        Assert.Contains("collection-plan-stability", codes);
    }

    [Fact]
    public void ServerEvidenceSchemaRejectsUnknownPropertiesAndRawSql()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(ValidEvidence(), EvidenceJson.Options);
        using var document = JsonDocument.Parse(json);
        var fields = document.RootElement.EnumerateObject().ToDictionary(static item => item.Name, static item => item.Value.Clone());
        var mutated = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = fields["schemaVersion"], collectedAtUtc = fields["collectedAtUtc"],
            imageDigest = fields["imageDigest"], fixtureIdentityHash = fields["fixtureIdentityHash"],
            logicalReads = fields["logicalReads"], planStability = fields["planStability"], rawSql = "SELECT secret",
        });
        Assert.Contains(SqlServerCollectionEvidenceValidator.Validate(mutated), static error => error.Code == "json-schema");
    }

    [Fact]
    public void ServerEvidenceRejectsWrongStableAndScalarCacheCounts()
    {
        var evidence = ValidEvidence();
        var wrongStable = evidence with
        {
            PlanStability = evidence.PlanStability.Select(item =>
                item.Transport == SqlServerCollectionTransport.Tvp && item.Cardinality == 10
                    ? item with { CachedPlanCount = 2 }
                    : item).ToArray(),
        };
        var wrongScalar = evidence with
        {
            PlanStability = evidence.PlanStability.Select(item =>
                item.Transport == SqlServerCollectionTransport.ScalarExpansion && item.Cardinality == 100
                    ? item with { CachedPlanCount = 2 }
                    : item).ToArray(),
        };

        Assert.Contains(SqlServerCollectionEvidenceValidator.Validate(wrongStable),
            static error => error.Code == "collection-plan-counts");
        Assert.Contains(SqlServerCollectionEvidenceValidator.Validate(wrongScalar),
            static error => error.Code == "collection-plan-counts");
    }

    [Fact]
    public void ServerEvidenceRequiresFourScalarStructuralShapes()
    {
        var evidence = ValidEvidence();
        var firstScalar = evidence.PlanStability.First(static item =>
            item.Transport == SqlServerCollectionTransport.ScalarExpansion);
        var mutated = evidence with
        {
            PlanStability = evidence.PlanStability.Select(item =>
                item.Transport == SqlServerCollectionTransport.ScalarExpansion && item.Cardinality == 10
                    ? item with { SqlHash = firstScalar.SqlHash, ParameterSignature = firstScalar.ParameterSignature }
                    : item).ToArray(),
        };

        Assert.Contains(SqlServerCollectionEvidenceValidator.Validate(mutated),
            static error => error.Code == "collection-scalar-shapes");
    }

    [Fact]
    public void ServerEvidenceRejectsColdLogicalReadMeasurements()
    {
        var evidence = ValidEvidence();
        var mutated = evidence with
        {
            LogicalReads = evidence.LogicalReads.Select((item, index) =>
                index == 0 ? item with { ExecutionCount = 1 } : item).ToArray(),
        };

        Assert.Contains(SqlServerCollectionEvidenceValidator.Validate(mutated),
            static error => error.Code == "collection-logical-reads");
    }

    private static SqlServerCollectionEvidence ValidEvidence()
    {
        var logical = SqlServerCollectionScenarioCatalog.Scenarios.Select(item =>
            new SqlServerCollectionLogicalReadEvidence(item.Transport, item.Cardinality, 1, 2, Hash("query"), Hash("plan"))).ToArray();
        var plans = SqlServerCollectionScenarioCatalog.Scenarios.Select(item =>
            new SqlServerCollectionPlanEvidence(item.Transport, item.Cardinality,
                Hash(item.Transport == SqlServerCollectionTransport.ScalarExpansion ? $"sql-{item.Cardinality}" : $"sql-{item.Transport}"),
                Hash(item.Transport == SqlServerCollectionTransport.ScalarExpansion ? $"parameters-{item.Cardinality}" : $"parameters-{item.Transport}"),
                Hash("query"), Hash("plan"), item.Transport == SqlServerCollectionTransport.ScalarExpansion
                    ? item.Cardinality switch { 1 => 1, 10 => 2, 100 => 3, 1_000 => 4, _ => 0 }
                    : 1)).ToArray();
        return new(SqlServerCollectionEvidenceSchema.Version, DateTimeOffset.UtcNow,
            DatabaseImageCatalog.GetRequired("sqlserver").Digest,
            NorthwindFixtureCatalog.For(FixtureTier.Standard).IdentityHash, logical, plans);
    }

    private static string Hash(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
