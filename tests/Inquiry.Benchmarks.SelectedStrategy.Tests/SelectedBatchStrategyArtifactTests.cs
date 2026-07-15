using System.Text.Json;
using System.Text.Json.Nodes;
using Inquiry.Benchmarks.Contracts.Evidence;

namespace Inquiry.Benchmarks.SelectedStrategy.Tests;

public sealed class SelectedBatchStrategyArtifactTests
{
    private static readonly IReadOnlyDictionary<string, Type> BenchmarkTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["sqlite"] = typeof(global::Inquiry.Benchmarks.BatchMutationStrategyBenchmarks),
            ["sqlserver"] = typeof(global::Inquiry.Benchmarks.SqlServer.BatchMutationStrategyBenchmarks),
            ["postgresql"] = typeof(global::Inquiry.Benchmarks.PostgreSql.BatchMutationStrategyBenchmarks),
            ["mysql"] = typeof(global::Inquiry.Benchmarks.MySql.BatchMutationStrategyBenchmarks),
            ["mariadb"] = typeof(global::Inquiry.Benchmarks.MariaDb.BatchMutationStrategyBenchmarks),
            ["oracle"] = typeof(global::Inquiry.Benchmarks.Oracle.BatchMutationStrategyBenchmarks),
        };

    [Fact]
    public void CheckedManifestMatchesClosedSchemaAndEveryCompiledBenchmarkSurface()
    {
        var result = SelectedBatchStrategyArtifactValidator.Validate(
            ManifestBytes(), new SelectedStrategyValidationContext(BenchmarkTypes));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.NotNull(result.Artifact);
        Assert.Equal(6, result.Artifact.Providers.Count);
        Assert.Equal(18, result.Artifact.Providers.Sum(static provider => provider.Operations.Count));
        Assert.Equal(72, result.Artifact.Providers.Sum(static provider =>
            provider.Operations.Sum(static operation => operation.Cells.Count)));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Evidence", "selected-batch-strategy-v1.schema.json")));
        Assert.Contains(typeof(SelectedBatchStrategyManifest).Assembly.GetManifestResourceNames(),
            static name => name.EndsWith("selected-batch-strategy-v1.schema.json", StringComparison.Ordinal));
        Assert.All(result.Artifact.Providers.SelectMany(static provider => provider.Operations)
            .SelectMany(static operation => operation.Cells), cell =>
        {
            Assert.Equal(SelectedStrategyStatus.PendingMeasurement, cell.Status);
            Assert.Equal(SelectedStrategyConfidence.Unmeasured, cell.Confidence);
            Assert.Null(cell.RuntimeCapabilities);
            Assert.Null(cell.SelectedEvidence);
            Assert.Empty(cell.ComparisonEvidence);
        });
    }

    [Fact]
    public void IntegratedInsertStrategiesArePinnedWithoutClaimingMeasuredCells()
    {
        var manifest = Manifest();
        var sqlite = manifest.Providers.Single(static provider => provider.Provider == "sqlite")
            .Operations.Single(static operation => operation.Operation == BatchMutationOperation.Insert);
        Assert.Equal("generatedReusedRowPreferPrepareOnce", sqlite.ProductionExecutionMode);
        Assert.Equal("single-row-insert", sqlite.SqlShape);
        Assert.Equal("configured-max-batch-size", sqlite.ChunkPolicy);

        var sqlServer = manifest.Providers.Single(static provider => provider.Provider == "sqlserver")
            .Operations.Single(static operation => operation.Operation == BatchMutationOperation.Insert);
        Assert.Equal("generatedAdaptiveSetBasedBelow250DbBatchAtOrAbove250WithReusedFallback", sqlServer.ProductionExecutionMode);
        Assert.Equal("multi-row-values-when-below-250-and-within-parameter-limit-otherwise-single-row-insert", sqlServer.SqlShape);
        Assert.Equal("configured-max-batch-size-capped-at-1000-with-independent-set-based-parameter-limit", sqlServer.ChunkPolicy);

        Assert.All(sqlite.Cells.Concat(sqlServer.Cells), static cell =>
        {
            Assert.Equal(SelectedStrategyStatus.PendingMeasurement, cell.Status);
            Assert.Equal(SelectedStrategyConfidence.Unmeasured, cell.Confidence);
        });
    }

    [Fact]
    public void StaleIntegratedInsertStrategiesFailClosed()
    {
        var manifest = Manifest();
        var sqliteProvider = manifest.Providers.Single(static provider => provider.Provider == "sqlite");
        var sqliteInsert = sqliteProvider.Operations.Single(static operation => operation.Operation == BatchMutationOperation.Insert);
        AssertCode(ReplaceOperation(manifest, sqliteProvider, sqliteInsert with
        {
            ProductionExecutionMode = "generatedChunkBound",
            SqlShape = "multi-row-values",
            ChunkPolicy = "effective-max-batch-and-parameters",
        }), "strategy-operation-contract");

        var sqlServerProvider = manifest.Providers.Single(static provider => provider.Provider == "sqlserver");
        var sqlServerInsert = sqlServerProvider.Operations.Single(static operation => operation.Operation == BatchMutationOperation.Insert);
        AssertCode(ReplaceOperation(manifest, sqlServerProvider, sqlServerInsert with
        {
            ProductionExecutionMode = "generatedChunkBound",
            SqlShape = "multi-row-values",
            ChunkPolicy = "effective-max-batch-and-parameters",
        }), "strategy-operation-contract");
    }

    [Fact]
    public void RemovingOrDuplicatingMatrixDimensionsFailsClosed()
    {
        var manifest = Manifest();
        AssertCode(manifest with { Providers = manifest.Providers.Skip(1).ToArray() }, "strategy-providers");
        AssertCode(manifest with { Providers = manifest.Providers.Append(manifest.Providers[0]).ToArray() }, "strategy-providers");

        var provider = manifest.Providers[0];
        AssertCode(ReplaceProvider(manifest, provider with { Operations = provider.Operations.Skip(1).ToArray() }),
            "strategy-operations");
        AssertCode(ReplaceProvider(manifest, provider with { Operations = provider.Operations.Append(provider.Operations[0]).ToArray() }),
            "strategy-operations");

        var operation = provider.Operations[0];
        AssertCode(ReplaceOperation(manifest, provider, operation with { Cells = operation.Cells.Skip(1).ToArray() }),
            "strategy-cells");
        AssertCode(ReplaceOperation(manifest, provider, operation with { Cells = operation.Cells.Append(operation.Cells[0]).ToArray() }),
            "strategy-cells");
    }

    [Fact]
    public void MatrixDimensionsMustRemainInCanonicalOrder()
    {
        var manifest = Manifest();
        AssertCode(manifest with { Providers = manifest.Providers.Reverse().ToArray() }, "strategy-provider-order");

        var provider = manifest.Providers[0];
        AssertCode(ReplaceProvider(manifest, provider with { Operations = provider.Operations.Reverse().ToArray() }),
            "strategy-operation-order");

        var operation = provider.Operations[0];
        AssertCode(ReplaceOperation(manifest, provider, operation with { Cells = operation.Cells.Reverse().ToArray() }),
            "strategy-cell-order");
    }

    [Fact]
    public void SelectedMethodModeShapeComparisonAndEvidenceCannotDisappearOrBeRelabeled()
    {
        var manifest = Manifest();
        var provider = manifest.Providers[0];
        var operation = provider.Operations[0];
        AssertCode(ReplaceOperation(manifest, provider, operation with { SelectedMethod = "Raw_PrecomputedMultiRowInsertFloor" }),
            "strategy-selected-method");
        AssertCode(ReplaceOperation(manifest, provider, operation with { ProductionExecutionMode = "generatedDbBatch" }),
            "strategy-operation-contract");
        AssertCode(ReplaceOperation(manifest, provider, operation with { SqlShape = "single-row-update-db-batch" }),
            "strategy-operation-contract");
        AssertCode(ReplaceOperation(manifest, provider, operation with { ChunkPolicy = "fixed-1000" }),
            "strategy-operation-contract");
        AssertCode(ReplaceOperation(manifest, provider, operation with { Comparisons = [] }),
            "strategy-comparisons");
        AssertCode(ReplaceOperation(manifest, provider, operation with
        {
            Comparisons = operation.Comparisons.Skip(1).ToArray(),
        }), "strategy-benchmark-surface");
        AssertCode(ReplaceOperation(manifest, provider, operation with
        {
            Comparisons = operation.Comparisons.Select((comparison, index) => index == 0
                ? comparison with { Role = ComparisonMethodRole.EndToEnd }
                : comparison).ToArray(),
        }), "strategy-comparison-role");

        var cell = operation.Cells[0] with
        {
            Status = SelectedStrategyStatus.Provisional,
            Confidence = SelectedStrategyConfidence.Diagnostic,
            RuntimeCapabilities = new Dictionary<string, string> { ["selected-execution-mode"] = operation.ProductionExecutionMode },
            SelectedEvidence = new MeasuredEvidenceReference("../escape.json", "not-a-hash", "not-a-case"),
        };
        AssertCode(ReplaceOperation(manifest, provider, operation with
        {
            Cells = operation.Cells.Select(item => item.Cardinality == cell.Cardinality ? cell : item).ToArray(),
        }), "strategy-evidence-reference");
    }

    [Fact]
    public void CapabilityProbeAndFallbackAreBoundToCompiledSurface()
    {
        var manifest = Manifest();
        var provider = manifest.Providers.Single(static item => item.Provider == "sqlserver");
        var capability = Assert.Single(provider.Capabilities);
        AssertCode(ReplaceProvider(manifest, provider with
        {
            Capabilities = [capability with { FallbackExecutionMode = "db-batch-disabled" }],
        }), "strategy-capability");
        AssertCode(ReplaceProvider(manifest, provider with
        {
            Capabilities = [capability with { ProbeMember = "MissingCapability" }],
        }), "strategy-capability-surface");
        AssertCode(ReplaceProvider(manifest, provider with
        {
            Capabilities = [capability with { AffectedMethods = capability.AffectedMethods.Skip(1).ToArray() }],
        }), "strategy-capability");

        var operation = provider.Operations[0];
        var cell = operation.Cells[0] with
        {
            Status = SelectedStrategyStatus.Provisional,
            Confidence = SelectedStrategyConfidence.Diagnostic,
            RuntimeCapabilities = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["selected-execution-mode"] = operation.ProductionExecutionMode,
                [capability.Id] = "true",
            },
            SelectedEvidence = new("evidence/selected.json", new string('a', 64), new string('b', 64)),
            ComparisonEvidence =
            [
                new(operation.Comparisons[0].Method,
                    new("evidence/comparison.json", new string('c', 64), new string('d', 64))),
            ],
        };
        AssertCode(ReplaceOperation(manifest, provider, operation with
        {
            Cells = operation.Cells.Select(item => item.Cardinality == cell.Cardinality ? cell : item).ToArray(),
        }), "strategy-runtime-capability");

        var extraCapabilityCell = cell with
        {
            RuntimeCapabilities = new Dictionary<string, string>(cell.RuntimeCapabilities!, StringComparer.Ordinal)
            {
                ["unchecked-capability"] = "true",
            },
        };
        AssertCode(ReplaceOperation(manifest, provider, operation with
        {
            Cells = operation.Cells.Select(item =>
                item.Cardinality == extraCapabilityCell.Cardinality ? extraCapabilityCell : item).ToArray(),
        }), "strategy-runtime-capability");
    }

    [Fact]
    public void BenchmarkSurfaceContextAndIdentityAreRequired()
    {
        var withoutContext = SelectedBatchStrategyArtifactValidator.Validate(ManifestBytes());
        Assert.Contains(withoutContext.Errors, static error => error.Code == "strategy-benchmark-context");

        var manifest = Manifest();
        var provider = manifest.Providers[0];
        AssertCode(ReplaceProvider(manifest, provider with
        {
            BenchmarkAssembly = "Arbitrary.Benchmarks",
            BenchmarkType = "Arbitrary.Benchmarks.UncheckedType",
        }), "strategy-benchmark-type");
    }

    [Fact]
    public void CheckedProviderProvisionalStatusCannotBeRelabeled()
    {
        var manifest = Manifest();
        var provider = manifest.Providers.Single(static item => item.Provider == "sqlite");

        AssertCode(ReplaceProvider(manifest, provider with { Provisional = false }), "strategy-provisional");
    }

    [Fact]
    public void RowsMustBeTheOnlyBenchmarkParameterDimension()
    {
        var types = new Dictionary<string, Type>(BenchmarkTypes, StringComparer.Ordinal)
        {
            ["sqlite"] = typeof(ExtraParameterDimensionBenchmark),
        };

        var errors = SelectedBatchStrategyValidator.Validate(
            Manifest(), new SelectedStrategyValidationContext(types));

        Assert.Contains(errors, static error => error.Code == "strategy-params");
    }

    [Fact]
    public void PendingCellsCannotClaimConfidenceCapabilitiesOrEvidence()
    {
        var manifest = Manifest();
        var provider = manifest.Providers[0];
        var operation = provider.Operations[0];
        var cells = operation.Cells.ToArray();
        cells[0] = cells[0] with
        {
            Confidence = SelectedStrategyConfidence.Authoritative,
            RuntimeCapabilities = new Dictionary<string, string> { ["selected-execution-mode"] = operation.ProductionExecutionMode },
            SelectedEvidence = new MeasuredEvidenceReference("evidence/result.json", new string('a', 64), new string('b', 64)),
        };
        AssertCode(ReplaceOperation(manifest, provider, operation with { Cells = cells }), "strategy-pending");
    }

    [Fact]
    public void ClosedSchemaRejectsUnknownProperties()
    {
        var root = JsonNode.Parse(ManifestBytes())!.AsObject();
        root["undocumented"] = true;

        var result = SelectedBatchStrategyArtifactValidator.Validate(
            JsonSerializer.SerializeToUtf8Bytes(root), new SelectedStrategyValidationContext(BenchmarkTypes));

        Assert.Contains(result.Errors, static error => error.Code == "json-schema");
    }

    private static void AssertCode(SelectedBatchStrategyManifest manifest, string code)
    {
        var errors = SelectedBatchStrategyValidator.Validate(
            manifest, new SelectedStrategyValidationContext(BenchmarkTypes));
        Assert.Contains(errors, error => error.Code == code);
    }

    private static SelectedBatchStrategyManifest ReplaceProvider(
        SelectedBatchStrategyManifest manifest,
        SelectedProviderStrategy replacement)
        => manifest with
        {
            Providers = manifest.Providers.Select(provider =>
                provider.Provider == replacement.Provider ? replacement : provider).ToArray(),
        };

    private static SelectedBatchStrategyManifest ReplaceOperation(
        SelectedBatchStrategyManifest manifest,
        SelectedProviderStrategy provider,
        SelectedOperationStrategy replacement)
        => ReplaceProvider(manifest, provider with
        {
            Operations = provider.Operations.Select(operation =>
                operation.Operation == replacement.Operation ? replacement : operation).ToArray(),
        });

    private static SelectedBatchStrategyManifest Manifest()
        => JsonSerializer.Deserialize<SelectedBatchStrategyManifest>(ManifestBytes(), EvidenceJson.Options)!;

    private static byte[] ManifestBytes()
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Evidence", "selected-batch-strategy-v1.json"));

    private sealed class ExtraParameterDimensionBenchmark
    {
        [BenchmarkDotNet.Attributes.Params(1, 10, 100, 1000)]
        public int Rows = 1;

        [BenchmarkDotNet.Attributes.Params("first", "second")]
        public string Mode = string.Empty;
    }
}
