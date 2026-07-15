using System.Security.Cryptography;
using System.Text.Json;
using Inquiry.Benchmarks.Contracts.Evidence;

namespace Inquiry.Benchmarks.Contracts.Tests;

public sealed class SelectedBatchStrategyEvidenceTests
{
    [Fact]
    public void SelectedEvidenceMustMatchMethodProviderCardinalityRowsAndCapabilities()
    {
        var (manifest, provider, operation, cell) = FirstCell("sqlite", BatchMutationOperation.Insert);
        var capabilities = Capabilities(operation);

        AssertEvidenceCode(manifest, provider, operation, cell,
            Evidence(provider, operation, cell, "Wrong_Method", capabilities), "strategy-evidence-target");

        var wrongProvider = Evidence(provider, operation, cell, operation.SelectedMethod, capabilities);
        var providerKey = wrongProvider.CaseKey with { Provider = "sqlserver" };
        AssertEvidenceCode(manifest, provider, operation, cell,
            wrongProvider with { CaseKey = providerKey, CaseId = providerKey.StableId }, "strategy-evidence-target");

        var wrongCardinality = Evidence(provider, operation, cell, operation.SelectedMethod, capabilities);
        var cardinalityKey = wrongCardinality.CaseKey with { Cardinality = 10 };
        AssertEvidenceCode(manifest, provider, operation, cell, wrongCardinality with
        {
            CaseKey = cardinalityKey,
            CaseId = cardinalityKey.StableId,
            BenchmarkTarget = wrongCardinality.BenchmarkTarget with
            {
                Cardinality = 10,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["Rows"] = "10" },
            },
        }, "strategy-evidence-target");

        var wrongRows = Evidence(provider, operation, cell, operation.SelectedMethod, capabilities);
        wrongRows = wrongRows with
        {
            BenchmarkTarget = wrongRows.BenchmarkTarget with
            {
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["Dialect"] = "sqlite" },
            },
        };
        AssertEvidenceCode(manifest, provider, operation, cell, wrongRows, "strategy-evidence-target");

        AssertEvidenceCode(manifest, provider, operation, cell,
            Evidence(provider, operation, cell, operation.SelectedMethod,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["selected-execution-mode"] = "relabeled" }),
            "strategy-evidence-capability", capabilities);
    }

    [Fact]
    public void ShortRunCannotBecomeAuthoritativeReleaseEvidence()
    {
        var (manifest, provider, operation, cell) = FirstCell("sqlite", BatchMutationOperation.Insert);
        var capabilities = Capabilities(operation);
        var evidence = Evidence(provider, operation, cell, operation.SelectedMethod, capabilities, releaseSource: true) with
        {
            Authoritative = true,
            BenchmarkDotNet = Evidence(provider, operation, cell, operation.SelectedMethod, capabilities, releaseSource: true)
                .BenchmarkDotNet with { JobId = "ShortRun" },
        };
        var reference = Reference("evidence/short-run.json", evidence);
        var accepted = cell with
        {
            Status = SelectedStrategyStatus.Accepted,
            Confidence = SelectedStrategyConfidence.Authoritative,
            RuntimeCapabilities = capabilities,
            SelectedEvidence = reference.Reference,
        };

        AssertCode(ReplaceCell(manifest, provider, operation, accepted),
            new SelectedStrategyValidationContext(ResolveEvidence: item =>
                item.RelativeArtifactId == reference.Reference.RelativeArtifactId ? reference.Resolved : null),
            "strategy-evidence-authority");
    }

    [Fact]
    public void ControlEvidenceCannotBecomeAuthoritative()
    {
        var (manifest, provider, operation, cell) = FirstCell("mysql", BatchMutationOperation.Insert);
        var control = operation.Comparisons.Single(static comparison => comparison.Role == ComparisonMethodRole.Control);
        var capabilities = Capabilities(operation);
        var selected = Reference("evidence/selected.json",
            Evidence(provider, operation, cell, operation.SelectedMethod, capabilities));
        var controlEvidence = Reference("evidence/control.json",
            Evidence(provider, operation, cell, control.Method, capabilities, releaseSource: true) with { Authoritative = true });
        var provisional = cell with
        {
            Status = SelectedStrategyStatus.Provisional,
            Confidence = SelectedStrategyConfidence.Diagnostic,
            RuntimeCapabilities = capabilities,
            SelectedEvidence = selected.Reference,
            ComparisonEvidence = [new(control.Method, controlEvidence.Reference)],
        };
        var resolved = new Dictionary<string, ResolvedMeasuredEvidence>(StringComparer.Ordinal)
        {
            [selected.Reference.RelativeArtifactId] = selected.Resolved,
            [controlEvidence.Reference.RelativeArtifactId] = controlEvidence.Resolved,
        };

        AssertCode(ReplaceCell(manifest, provider, operation, provisional),
            new SelectedStrategyValidationContext(ResolveEvidence: item => resolved.GetValueOrDefault(item.RelativeArtifactId)),
            "strategy-control-authority");
    }

    [Fact]
    public void AcceptedCellsRequireEveryNonControlComparisonEvidence()
    {
        var (manifest, provider, operation, cell) = FirstCell("sqlite", BatchMutationOperation.Insert);
        var capabilities = Capabilities(operation);
        var selected = Reference("evidence/selected.json",
            Evidence(provider, operation, cell, operation.SelectedMethod, capabilities, releaseSource: true) with { Authoritative = true });
        var accepted = cell with
        {
            Status = SelectedStrategyStatus.Accepted,
            Confidence = SelectedStrategyConfidence.Authoritative,
            RuntimeCapabilities = capabilities,
            SelectedEvidence = selected.Reference,
        };

        AssertCode(ReplaceCell(manifest, provider, operation, accepted),
            new SelectedStrategyValidationContext(ResolveEvidence: _ => selected.Resolved),
            "strategy-comparison-evidence");
    }

    [Fact]
    public void ProvisionalSelectedEvidenceRequiresNonControlComparisonEvidence()
    {
        var (manifest, provider, operation, cell) = FirstCell("sqlite", BatchMutationOperation.Insert);
        var capabilities = Capabilities(operation);
        var selected = Reference("evidence/selected.json",
            Evidence(provider, operation, cell, operation.SelectedMethod, capabilities));
        var provisional = cell with
        {
            Status = SelectedStrategyStatus.Provisional,
            Confidence = SelectedStrategyConfidence.Diagnostic,
            RuntimeCapabilities = capabilities,
            SelectedEvidence = selected.Reference,
        };

        AssertCode(ReplaceCell(manifest, provider, operation, provisional),
            new SelectedStrategyValidationContext(ResolveEvidence: _ => selected.Resolved),
            "strategy-comparison-evidence");
    }

    [Fact]
    public void ResolvedBytesAreTheOnlySemanticEvidenceAuthority()
    {
        var (manifest, provider, operation, cell) = FirstCell("sqlite", BatchMutationOperation.Insert);
        var capabilities = Capabilities(operation);
        var trusted = Reference("evidence/selected.json",
            Evidence(provider, operation, cell, operation.SelectedMethod, capabilities));
        var attackerEnvelope = Evidence(provider, operation, cell, "Wrong_Method", capabilities);
        var attackerBytes = JsonSerializer.SerializeToUtf8Bytes(attackerEnvelope, EvidenceJson.Options);
        var provisional = cell with
        {
            Status = SelectedStrategyStatus.Provisional,
            Confidence = SelectedStrategyConfidence.Diagnostic,
            RuntimeCapabilities = capabilities,
            SelectedEvidence = trusted.Reference,
        };
        var errors = SelectedBatchStrategyValidator.Validate(
            ReplaceCell(manifest, provider, operation, provisional),
            new SelectedStrategyValidationContext(ResolveEvidence: _ => new ResolvedMeasuredEvidence(attackerBytes)));

        Assert.Contains(errors, static error => error.Code == "strategy-evidence-identity");
        Assert.Contains(errors, static error => error.Code == "strategy-evidence-target");
    }

    private static void AssertEvidenceCode(
        SelectedBatchStrategyManifest manifest,
        SelectedProviderStrategy provider,
        SelectedOperationStrategy operation,
        SelectedStrategyCell cell,
        BenchmarkEvidenceEnvelope evidence,
        string code,
        IReadOnlyDictionary<string, string>? cellCapabilities = null)
    {
        var reference = Reference("evidence/selected.json", evidence);
        var provisional = cell with
        {
            Status = SelectedStrategyStatus.Provisional,
            Confidence = SelectedStrategyConfidence.Diagnostic,
            RuntimeCapabilities = cellCapabilities ?? evidence.RuntimeCapabilities,
            SelectedEvidence = reference.Reference,
        };
        AssertCode(ReplaceCell(manifest, provider, operation, provisional),
            new SelectedStrategyValidationContext(ResolveEvidence: _ => reference.Resolved), code);
    }

    private static void AssertCode(
        SelectedBatchStrategyManifest manifest,
        SelectedStrategyValidationContext context,
        string code)
        => Assert.Contains(SelectedBatchStrategyValidator.Validate(manifest, context), error => error.Code == code);

    private static BenchmarkEvidenceEnvelope Evidence(
        SelectedProviderStrategy provider,
        SelectedOperationStrategy operation,
        SelectedStrategyCell cell,
        string method,
        IReadOnlyDictionary<string, string> capabilities,
        bool releaseSource = false)
    {
        var source = releaseSource ? TestData.PackageSource(provider.Provider) : TestData.ProjectSource(provider.Provider);
        var envelope = TestData.Envelope(source, provider.Provider);
        var key = envelope.CaseKey with
        {
            OperationSemantics = operation.Operation.ToString(),
            Cardinality = cell.Cardinality,
        };
        return envelope with
        {
            CaseKey = key,
            CaseId = key.StableId,
            BenchmarkTarget = new BenchmarkTargetEvidence(
                provider.BenchmarkAssembly,
                provider.BenchmarkType,
                method,
                cell.Cardinality,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Rows"] = cell.Cardinality.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }),
            RuntimeCapabilities = capabilities,
        };
    }

    private static IReadOnlyDictionary<string, string> Capabilities(SelectedOperationStrategy operation)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["selected-execution-mode"] = operation.ProductionExecutionMode,
        };

    private static (MeasuredEvidenceReference Reference, ResolvedMeasuredEvidence Resolved) Reference(
        string path,
        BenchmarkEvidenceEnvelope evidence)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(evidence, EvidenceJson.Options);
        return (new(path, Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant(), evidence.CaseId),
            new(json));
    }

    private static SelectedBatchStrategyManifest ReplaceCell(
        SelectedBatchStrategyManifest manifest,
        SelectedProviderStrategy provider,
        SelectedOperationStrategy operation,
        SelectedStrategyCell cell)
    {
        var replacement = operation with
        {
            Cells = operation.Cells.Select(item => item.Cardinality == cell.Cardinality ? cell : item).ToArray(),
        };
        return manifest with
        {
            Providers = manifest.Providers.Select(item => item.Provider != provider.Provider ? item : provider with
            {
                Operations = provider.Operations.Select(candidate =>
                    candidate.Operation == replacement.Operation ? replacement : candidate).ToArray(),
            }).ToArray(),
        };
    }

    private static (SelectedBatchStrategyManifest Manifest, SelectedProviderStrategy Provider,
        SelectedOperationStrategy Operation, SelectedStrategyCell Cell) FirstCell(
        string providerName,
        BatchMutationOperation operationName)
    {
        var manifest = JsonSerializer.Deserialize<SelectedBatchStrategyManifest>(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Evidence", "selected-batch-strategy-v1.json")),
            EvidenceJson.Options)!;
        var provider = manifest.Providers.Single(item => item.Provider == providerName);
        var operation = provider.Operations.Single(item => item.Operation == operationName);
        return (manifest, provider, operation, operation.Cells[0]);
    }
}
