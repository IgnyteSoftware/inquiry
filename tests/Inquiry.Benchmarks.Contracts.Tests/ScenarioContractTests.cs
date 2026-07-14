using Inquiry.Benchmarks.Contracts;

namespace Inquiry.Benchmarks.Contracts.Tests;

public sealed class ScenarioContractTests
{
    [Fact]
    public void StableCaseKeyIncludesSemanticModesButRunIdentityRetainsSourceProvenance()
    {
        var canonical = TestData.CaseKey();
        var variants = new[]
        {
            canonical with { ContractVersion = "2" },
            canonical with { WorkloadId = "customer.by-id" },
            canonical with { Provider = "postgresql" },
            canonical with { OperationSemantics = "best-effort-single" },
            canonical with { DataTier = "standard" },
            canonical with { Cardinality = 2 },
            canonical with { Buffering = BufferingMode.Streaming },
            canonical with { ConnectionLifecycle = ConnectionLifecycle.Retained },
            canonical with { Pooling = PoolingMode.Unpooled },
            canonical with { Preparation = PreparationMode.Prepared },
            canonical with { Temperature = TemperatureMode.Cold },
            canonical with { Tracking = TrackingMode.Tracked },
            canonical with { Compilation = CompilationMode.Compiled },
            canonical with { ApiStyle = ApiStyle.Idiomatic },
            canonical with { TimedBoundaryHash = new string('0', 64) },
            canonical with { Transaction = TransactionMode.Committed },
            canonical with { Competitor = "dapper" },
            canonical with { CompetitorMajor = 2 },
            canonical with { RuntimeTfm = "net10.0" },
            canonical with { JobKind = BenchmarkJobKind.InProcess },
            canonical with { MetricFamily = MetricFamily.Throughput },
        };

        Assert.All(variants, variant => Assert.NotEqual(canonical.StableId, variant.StableId));
        var differentProjectSource = canonical with
        {
            Source = BenchmarkSourceIdentity.Project(new string('b', 40), canonical.Source.ArtifactManifestHash,
                canonical.Source.ResolvedDependencies,
                TestData.Artifacts(BenchmarkSourceMode.ProjectReference)
                    .Select(static artifact => artifact with { Sha256 = new string('8', 64) }).ToArray()),
        };
        Assert.Equal(canonical.StableId, differentProjectSource.StableId);
        Assert.NotEqual(canonical.RunIdentityHash, differentProjectSource.RunIdentityHash);
        var packageSource = canonical with { Source = TestData.PackageSource() };
        Assert.NotEqual(canonical.StableId, packageSource.StableId);
        Assert.Equal(canonical.StableId, TestData.CaseKey().StableId);
    }

    [Fact]
    public void IneligibleScenarioCannotPublishCompetitorOrLoadResults()
    {
        var scenario = TestData.Scenario() with
        {
            ComparisonEligible = false,
            LoadEligible = false,
        };

        Assert.Contains(ScenarioValidator.Validate(scenario, resultHasCompetitor: true, isLoadResult: false),
            static error => error.Code == "comparison-ineligible");
        Assert.Contains(ScenarioValidator.Validate(scenario, resultHasCompetitor: false, isLoadResult: true),
            static error => error.Code == "load-ineligible");
    }

    [Fact]
    public void CommittedMutationRequiresResetOutsideTimedBoundary()
    {
        var scenario = TestData.Scenario() with
        {
            Key = TestData.Scenario().Key with { Transaction = TransactionMode.Committed },
            TimedPath = TestData.Scenario().TimedPath with { IncludesTransactionBegin = true, IncludesTransactionCommit = true },
            Expected = TestData.Scenario().Expected with { TransactionOutcome = TransactionOutcome.Committed },
            ApprovedCommandGraph = TestData.Scenario().ApprovedCommandGraph with
            {
                Commands = [TestData.Scenario().ApprovedCommandGraph.Commands[0] with
                {
                    Mutation = MutationEffect.Update,
                    TransactionOutcome = TransactionOutcome.Committed,
                }],
            },
            MutationReset = new MutationResetContract(true, false),
        };

        Assert.Contains(ScenarioValidator.Validate(scenario), static error => error.Code == "mutation-leakage");
    }

    [Fact]
    public void ParityRejectsGraphResultCardinalityCommandCountAndModeDrift()
    {
        var scenario = TestData.Scenario();
        var observation = TestData.Observation() with
        {
            Count = 2,
            Checksum = "wrong",
            CommandCount = 2,
            Buffering = BufferingMode.Streaming,
            TimedPath = scenario.TimedPath with { IncludesDuplicateRead = false },
            CommandGraph = TestData.Graph() with
            {
                Commands = [TestData.Graph().Commands[0] with { ProjectedExpressions = ["CustomerID", "CompanyName", "Phone"] }],
            },
        };

        var codes = ParityValidator.Validate(scenario, observation).Select(static error => error.Code).ToHashSet();
        Assert.Contains("result-count", codes);
        Assert.Contains("result-checksum", codes);
        Assert.Contains("command-count", codes);
        Assert.Contains("buffering-mode", codes);
        Assert.Contains("timed-boundary", codes);
        Assert.Contains("command-graph", codes);
    }

    [Fact]
    public void EquivalentParameterSigilsDoNotChangeSemanticGraph()
    {
        var expected = TestData.Graph();
        var observed = expected with
        {
            Commands = [expected.Commands[0] with { ParameterNames = [":id"], Predicates = ["CustomerID = :id"] }],
            SqlStatements = expected.SqlStatements.Select(sql => sql.Replace("@id", ":id", StringComparison.Ordinal)).ToArray(),
        };

        Assert.Equal(expected.SemanticHash, observed.SemanticHash);
        Assert.NotEqual(expected.SqlFingerprint, observed.SqlFingerprint);
        Assert.Contains(ParityValidator.Validate(TestData.Scenario(), TestData.Observation() with { CommandGraph = observed }),
            static error => error.Code == "sql-fingerprint");
    }

    [Fact]
    public void CatalogMaterializesProviderSpecificExecutableSqlAndExactExpectedResult()
    {
        var template = Assert.Single(CanonicalScenarioCatalog.Templates, static item => item.WorkloadId == "customer.by-key");
        var sqlite = template.Materialize(TestData.CaseKey());
        var sqlServer = template.Materialize(TestData.CaseKey() with { Provider = "sqlserver" });
        var oracle = template.Materialize(TestData.CaseKey() with { Provider = "oracle" });

        Assert.Contains("LIMIT 2", Assert.Single(sqlite.ApprovedCommandGraph.SqlStatements), StringComparison.Ordinal);
        Assert.Contains("TOP (2)", Assert.Single(sqlServer.ApprovedCommandGraph.SqlStatements), StringComparison.Ordinal);
        Assert.Contains("FETCH FIRST 2 ROWS ONLY", Assert.Single(oracle.ApprovedCommandGraph.SqlStatements), StringComparison.Ordinal);
        Assert.Equal(3, new[] { sqlite, sqlServer, oracle }.Select(static item => item.ApprovedCommandGraph.SqlFingerprint).Distinct().Count());
        Assert.Matches("^[a-f0-9]{64}$", sqlite.Expected.Checksum);
    }

    [Fact]
    public void ScenarioValidatorRejectsTransactionAndMutationContradictions()
    {
        var scenario = TestData.Scenario() with
        {
            MutationReset = new MutationResetContract(true, true),
            Expected = TestData.Scenario().Expected with { TransactionOutcome = TransactionOutcome.RolledBack },
        };

        var codes = ScenarioValidator.Validate(scenario).Select(static error => error.Code).ToHashSet();
        Assert.Contains("transaction-mode", codes);
        Assert.Contains("mutation-contract", codes);
    }

    [Fact]
    public void CommandSqlMustMapOneToOneInSemanticOrder()
    {
        var scenario = TestData.Scenario();
        var duplicatedNodes = scenario.ApprovedCommandGraph with
        {
            Commands = [scenario.ApprovedCommandGraph.Commands[0], scenario.ApprovedCommandGraph.Commands[0]],
        };

        Assert.Contains(ScenarioValidator.Validate(scenario with
        {
            Expected = scenario.Expected with { CommandCount = 2 },
            ApprovedCommandGraph = duplicatedNodes,
        }), static error => error.Code == "command-sql-count");
        Assert.Contains(ParityValidator.Validate(scenario, TestData.Observation() with
        {
            CommandGraph = duplicatedNodes,
        }), static error => error.Code == "sql-fingerprint");
    }

    [Fact]
    public void CheckedScenarioRejectsSemanticDimensionAndCardinalityDrift()
    {
        var scenario = TestData.Scenario();
        var drifted = scenario with
        {
            Key = scenario.Key with
            {
                OperationSemantics = "first-row",
                Cardinality = 0,
                ApiStyle = ApiStyle.Idiomatic,
            },
        };

        var codes = ScenarioValidator.Validate(drifted).Select(static error => error.Code).ToHashSet();
        Assert.Contains("scenario-cardinality", codes);
        Assert.Contains("scenario-dimensions", codes);
    }

    [Fact]
    public void CheckedCatalogSeparatesComparableWorkloadsFromInquiryOnlyMicros()
    {
        Assert.Equal(2, CanonicalScenarioCatalog.Templates.Select(static scenario => scenario.WorkloadId).Distinct(StringComparer.Ordinal).Count());
        var read = Assert.Single(CanonicalScenarioCatalog.Templates, static scenario => scenario.WorkloadId == "customer.by-key");
        Assert.True(read.ComparisonEligible);
        Assert.True(read.LoadEligible);
        Assert.Equal(["CustomerID", "CompanyName"], read.Data.Projection.OrderBy(static column => column.Ordinal).Select(static column => column.Name));

        var micro = Assert.Single(CanonicalScenarioCatalog.Templates, static scenario => scenario.WorkloadId == "inquiry.parameter-binding");
        Assert.False(micro.ComparisonEligible);
        Assert.False(micro.LoadEligible);
    }

    [Fact]
    public void ParameterBindingTemplateEnforcesEverySemanticDimension()
    {
        var template = Assert.Single(CanonicalScenarioCatalog.Templates,
            static scenario => scenario.WorkloadId == "inquiry.parameter-binding");
        var key = TestData.CaseKey() with
        {
            WorkloadId = template.WorkloadId,
            OperationSemantics = "bind-eight-parameters",
            Cardinality = 8,
            ConnectionLifecycle = ConnectionLifecycle.Retained,
            ApiStyle = ApiStyle.Micro,
            TimedBoundaryHash = template.TimedPath.IdentityHash,
        };

        var scenario = template.Materialize(key);
        Assert.DoesNotContain(ScenarioValidator.Validate(scenario), static error => error.Code == "scenario-dimensions");

        var drifts = new[]
        {
            key with { OperationSemantics = "bind-seven-parameters" },
            key with { Cardinality = 7 },
            key with { Buffering = BufferingMode.Streaming },
            key with { ConnectionLifecycle = ConnectionLifecycle.PerOperation },
            key with { Pooling = PoolingMode.Unpooled },
            key with { Preparation = PreparationMode.Prepared },
            key with { Temperature = TemperatureMode.Cold },
            key with { Tracking = TrackingMode.Tracked },
            key with { Compilation = CompilationMode.Compiled },
            key with { ApiStyle = ApiStyle.Idiomatic },
            key with { Transaction = TransactionMode.Committed },
            key with { MetricFamily = MetricFamily.Throughput },
        };
        Assert.All(drifts, drift => Assert.Throws<ArgumentException>(() => template.Materialize(drift)));
    }
}
