using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Json.Schema;

namespace Inquiry.Benchmarks.Contracts.Evidence;

public static class SelectedBatchStrategySchema
{
    public const string Version = "inquiry-selected-batch-strategy-v1";
    public static IReadOnlyList<int> Cardinalities { get; } = [1, 10, 100, 1000];
    public static IReadOnlyList<string> ProviderOrder { get; } =
        ["sqlite", "sqlserver", "postgresql", "mysql", "mariadb", "oracle"];
    public static IReadOnlySet<string> Providers { get; } = ProviderOrder.ToHashSet(StringComparer.Ordinal);
    public static IReadOnlyList<BatchMutationOperation> OperationOrder { get; } =
        [BatchMutationOperation.Insert, BatchMutationOperation.Update, BatchMutationOperation.Delete];
    public static IReadOnlySet<BatchMutationOperation> Operations { get; } = OperationOrder.ToHashSet();
}

public enum BatchMutationOperation { Insert, Update, Delete }
public enum SelectedStrategyStatus { PendingMeasurement, Provisional, Accepted }
public enum SelectedStrategyConfidence { Unmeasured, Diagnostic, Authoritative }
public enum ComparisonMethodRole
{
    DirectDriver,
    DirectDriverFloor,
    PrecomputedTransportFloor,
    EndToEnd,
    NativeTransport,
    AlternativeTransport,
    Control,
}

public sealed record MeasuredEvidenceReference(string RelativeArtifactId, string Sha256, string CaseId);
public sealed record ComparisonEvidenceReference(string Method, MeasuredEvidenceReference Evidence);

public sealed record SelectedStrategyCell(
    int Cardinality,
    SelectedStrategyStatus Status,
    SelectedStrategyConfidence Confidence,
    IReadOnlyDictionary<string, string>? RuntimeCapabilities,
    MeasuredEvidenceReference? SelectedEvidence,
    IReadOnlyList<ComparisonEvidenceReference> ComparisonEvidence);

public sealed record ComparisonMethodContract(
    string Method,
    ComparisonMethodRole Role,
    string? CapabilityId);

public sealed record RuntimeCapabilityContract(
    string Id,
    string ProbeMember,
    string SupportedExecutionMode,
    string FallbackExecutionMode,
    IReadOnlyList<string> AffectedMethods);

public sealed record SelectedOperationStrategy(
    BatchMutationOperation Operation,
    string BenchmarkCategory,
    string SelectedMethod,
    string ProductionExecutionMode,
    string SqlShape,
    string ChunkPolicy,
    IReadOnlyList<ComparisonMethodContract> Comparisons,
    IReadOnlyList<SelectedStrategyCell> Cells);

public sealed record SelectedProviderStrategy(
    string Provider,
    string BenchmarkAssembly,
    string BenchmarkType,
    bool Provisional,
    IReadOnlyList<RuntimeCapabilityContract> Capabilities,
    IReadOnlyList<SelectedOperationStrategy> Operations);

public sealed record SelectedBatchStrategyManifest(
    string SchemaVersion,
    IReadOnlyList<int> Cardinalities,
    IReadOnlyList<SelectedProviderStrategy> Providers);

public sealed record ResolvedMeasuredEvidence(
    byte[] Json,
    EvidenceArtifactValidationContext? ValidationContext = null);

public sealed record SelectedStrategyValidationContext(
    IReadOnlyDictionary<string, Type>? BenchmarkTypes = null,
    Func<MeasuredEvidenceReference, ResolvedMeasuredEvidence?>? ResolveEvidence = null);

public static class SelectedBatchStrategyValidator
{
    private sealed record CheckedOperation(
        string Category,
        string SelectedMethod,
        string ExecutionMode,
        string SqlShape,
        string ChunkPolicy);

    private static readonly IReadOnlyDictionary<(string Provider, BatchMutationOperation Operation), CheckedOperation>
        CheckedOperations = new Dictionary<(string, BatchMutationOperation), CheckedOperation>
        {
            { ("sqlite", BatchMutationOperation.Insert), new("Insert", "Inquiry_SelectedInsertAll", "generatedReusedRowPreferPrepareOnce", "single-row-insert", "configured-max-batch-size") },
            { ("sqlite", BatchMutationOperation.Update), new("Update", "Inquiry_SelectedUpdateAll", "generatedReusedCommand", "single-row-update", "effective-max-batch-and-parameters") },
            { ("sqlite", BatchMutationOperation.Delete), new("Delete", "Inquiry_SelectedDeleteAll", "generatedSingleStatementPredicate", "json-each-keyset-delete", "none-single-statement") },
            { ("sqlserver", BatchMutationOperation.Insert), new("Insert", "Inquiry_SelectedInsertAll", "generatedAdaptiveSetBasedBelow250DbBatchAtOrAbove250WithReusedFallback", "multi-row-values-when-below-250-and-within-parameter-limit-otherwise-single-row-insert", "configured-max-batch-size-capped-at-1000-with-independent-set-based-parameter-limit") },
            { ("sqlserver", BatchMutationOperation.Update), new("Update", "Inquiry_SelectedUpdateAll", "generatedDbBatch", "single-row-update-db-batch", "effective-max-batch-and-parameters") },
            { ("sqlserver", BatchMutationOperation.Delete), new("Delete", "Inquiry_SelectedDeleteAll", "generatedSingleStatementPredicate", "tvp-keyset-delete", "none-single-statement") },
            { ("postgresql", BatchMutationOperation.Insert), new("Insert", "Inquiry_SelectedInsertAll", "generatedChunkBound", "multi-row-values", "effective-max-batch-and-parameters") },
            { ("postgresql", BatchMutationOperation.Update), new("Update", "Inquiry_SelectedUpdateAll", "generatedDbBatch", "single-row-update-db-batch", "effective-max-batch-and-parameters") },
            { ("postgresql", BatchMutationOperation.Delete), new("Delete", "Inquiry_SelectedDeleteAll", "generatedSingleStatementPredicate", "any-array-keyset-delete", "none-single-statement") },
            { ("mysql", BatchMutationOperation.Insert), new("BatchInsert", "Inquiry_SelectedInsertAll", "generatedChunkBound", "multi-row-values", "effective-max-batch-and-parameters") },
            { ("mysql", BatchMutationOperation.Update), new("BatchUpdate", "Inquiry_SelectedUpdateAll", "generatedSelectableHybrid", "derived-table-join-with-db-batch-single-row-tail", "effective-max-batch-and-parameters") },
            { ("mysql", BatchMutationOperation.Delete), new("BatchDelete", "Inquiry_SelectedDeleteAll", "generatedSingleStatementPredicate", "json-table-keyset-delete", "none-single-statement") },
            { ("mariadb", BatchMutationOperation.Insert), new("BatchInsert", "Inquiry_SelectedInsertAll", "generatedChunkBound", "multi-row-values", "effective-max-batch-and-parameters") },
            { ("mariadb", BatchMutationOperation.Update), new("BatchUpdate", "Inquiry_SelectedUpdateAll", "generatedSelectableHybrid", "derived-table-join-with-db-batch-single-row-tail", "effective-max-batch-and-parameters") },
            { ("mariadb", BatchMutationOperation.Delete), new("BatchDelete", "Inquiry_SelectedDeleteAll", "generatedSingleStatementPredicate", "json-table-keyset-delete", "none-single-statement") },
            { ("oracle", BatchMutationOperation.Insert), new("BatchInsert", "Inquiry_SelectedInsertAll", "generatedArrayBinding", "single-statement-array-binding-insert", "effective-max-batch-and-parameters") },
            { ("oracle", BatchMutationOperation.Update), new("BatchUpdate", "Inquiry_SelectedUpdateAll", "generatedArrayBinding", "single-statement-array-binding-update", "effective-max-batch-and-parameters") },
            { ("oracle", BatchMutationOperation.Delete), new("BatchDelete", "Inquiry_SelectedDeleteAll", "generatedSingleStatementPredicate", "json-table-keyset-delete", "none-single-statement") },
        };

    private static readonly IReadOnlyDictionary<string, ComparisonMethodRole> CheckedComparisonRoles =
        new Dictionary<string, ComparisonMethodRole>(StringComparer.Ordinal)
        {
            ["Direct_ReusedPreparedInsert"] = ComparisonMethodRole.DirectDriver,
            ["Direct_ReusedPreparedUpdate"] = ComparisonMethodRole.DirectDriver,
            ["Direct_ReusedPreparedDelete"] = ComparisonMethodRole.DirectDriver,
            ["Raw_PrecomputedMultiRowInsertFloor"] = ComparisonMethodRole.PrecomputedTransportFloor,
            ["Raw_PreSerializedJsonEachDeleteFloor"] = ComparisonMethodRole.PrecomputedTransportFloor,
            ["Raw_PrecomputedTvpDeleteFloor"] = ComparisonMethodRole.PrecomputedTransportFloor,
            ["Raw_EndToEndMultiRowInsert"] = ComparisonMethodRole.EndToEnd,
            ["Raw_EndToEndJsonEachDelete"] = ComparisonMethodRole.EndToEnd,
            ["Raw_EndToEndTvpDelete"] = ComparisonMethodRole.EndToEnd,
            ["Native_DbBatchInsert"] = ComparisonMethodRole.NativeTransport,
            ["Native_DbBatchUpdate"] = ComparisonMethodRole.NativeTransport,
            ["Native_DbBatchDelete"] = ComparisonMethodRole.NativeTransport,
            ["Native_NpgsqlBatchUpdate"] = ComparisonMethodRole.NativeTransport,
            ["Raw_AnyArrayDelete"] = ComparisonMethodRole.AlternativeTransport,
            ["Raw_MultiRowInsertControl"] = ComparisonMethodRole.Control,
            ["Raw_CaseUpdateControl"] = ComparisonMethodRole.Control,
            ["Raw_DerivedTableJoinControl"] = ComparisonMethodRole.Control,
            ["Raw_ExpandedInDeleteControl"] = ComparisonMethodRole.Control,
            ["Raw_JsonTableDeleteControl"] = ComparisonMethodRole.Control,
            ["Insert_ReusedPreparedCommand"] = ComparisonMethodRole.DirectDriver,
            ["Update_ReusedPreparedCommand"] = ComparisonMethodRole.DirectDriver,
            ["Delete_ReusedPreparedCommand"] = ComparisonMethodRole.DirectDriver,
            ["Insert_GeneratedChunkBinderControl"] = ComparisonMethodRole.Control,
            ["Update_GeneratedChunkBinderControl"] = ComparisonMethodRole.Control,
            ["Delete_GeneratedChunkBinderControl"] = ComparisonMethodRole.Control,
            ["Insert_DirectDriverArrayBindingFloor"] = ComparisonMethodRole.DirectDriverFloor,
            ["Update_DirectDriverArrayBindingFloor"] = ComparisonMethodRole.DirectDriverFloor,
            ["Delete_DirectDriverArrayBindingFloor"] = ComparisonMethodRole.DirectDriverFloor,
            ["Insert_PreIssue180GeneratedInsertSelectControl"] = ComparisonMethodRole.Control,
            ["Delete_PreIssue180GeneratedJsonTableControl"] = ComparisonMethodRole.Control,
        };

    public static IReadOnlyList<ContractError> Validate(
        SelectedBatchStrategyManifest manifest,
        SelectedStrategyValidationContext? context = null)
    {
        var errors = new List<ContractError>();
        AddIf(manifest.SchemaVersion != SelectedBatchStrategySchema.Version, "strategy-schema-version",
            "Selected batch strategy schema version is not supported.");
        AddIf(!manifest.Cardinalities.SequenceEqual(SelectedBatchStrategySchema.Cardinalities), "strategy-cardinalities",
            "Selected batch strategy cardinalities must be exactly 1, 10, 100, and 1000.");
        AddIf(context?.BenchmarkTypes is null, "strategy-benchmark-context",
            "Selected strategy validation requires the resolved compiled benchmark type map.");

        var providerGroups = manifest.Providers.GroupBy(static provider => provider.Provider, StringComparer.Ordinal).ToArray();
        AddIf(providerGroups.Any(static group => group.Count() != 1) ||
              !providerGroups.Select(static group => group.Key).ToHashSet(StringComparer.Ordinal)
                  .SetEquals(SelectedBatchStrategySchema.Providers),
            "strategy-providers", "Selected batch strategy must contain each supported provider exactly once.");
        AddIf(!manifest.Providers.Select(static provider => provider.Provider)
                .SequenceEqual(SelectedBatchStrategySchema.ProviderOrder, StringComparer.Ordinal),
            "strategy-provider-order", "Selected batch strategy providers must use canonical checked order.");

        foreach (var provider in manifest.Providers)
        {
            ValidateProvider(provider, errors);
            if (context?.BenchmarkTypes is not null) ValidateSurface(provider, context, errors);
            ValidateEvidence(provider, context?.ResolveEvidence, errors);
        }

        var cells = manifest.Providers.Sum(static provider =>
            provider.Operations.Sum(static operation => operation.Cells.Count));
        AddIf(cells != 72, "strategy-matrix", "Selected batch strategy must contain exactly 72 provider/operation/cardinality cells.");
        return errors;

        void AddIf(bool condition, string code, string message)
        {
            if (condition) errors.Add(new(code, message));
        }
    }

    private static void ValidateProvider(SelectedProviderStrategy provider, List<ContractError> errors)
    {
        var expectedProvisional = provider.Provider is "sqlite" or "sqlserver";
        AddIf(provider.Provisional != expectedProvisional, "strategy-provisional",
            $"Provider '{provider.Provider}' provisional status does not match the checked release status.");
        AddIf(string.IsNullOrWhiteSpace(provider.BenchmarkAssembly) || string.IsNullOrWhiteSpace(provider.BenchmarkType),
            "strategy-benchmark-type", $"Provider '{provider.Provider}' must identify its benchmark assembly and type.");
        var operations = provider.Operations.GroupBy(static operation => operation.Operation).ToArray();
        AddIf(operations.Any(static group => group.Count() != 1) ||
              !operations.Select(static group => group.Key).ToHashSet().SetEquals(SelectedBatchStrategySchema.Operations),
            "strategy-operations", $"Provider '{provider.Provider}' must contain Insert, Update, and Delete exactly once.");
        AddIf(!provider.Operations.Select(static operation => operation.Operation)
                .SequenceEqual(SelectedBatchStrategySchema.OperationOrder),
            "strategy-operation-order", $"Provider '{provider.Provider}' operations must use canonical checked order.");

        var capabilities = provider.Capabilities.GroupBy(static capability => capability.Id, StringComparer.Ordinal).ToArray();
        AddIf(capabilities.Any(static group => group.Count() != 1), "strategy-capability",
            $"Provider '{provider.Provider}' has duplicate capability IDs.");
        foreach (var capability in provider.Capabilities)
        {
            AddIf(string.IsNullOrWhiteSpace(capability.Id) || string.IsNullOrWhiteSpace(capability.ProbeMember) ||
                  string.IsNullOrWhiteSpace(capability.SupportedExecutionMode) ||
                  string.IsNullOrWhiteSpace(capability.FallbackExecutionMode) || capability.AffectedMethods.Count == 0 ||
                  capability.AffectedMethods.Any(string.IsNullOrWhiteSpace) ||
                  capability.AffectedMethods.Distinct(StringComparer.Ordinal).Count() != capability.AffectedMethods.Count,
                "strategy-capability", $"Provider '{provider.Provider}' has an incomplete capability/fallback declaration.");
            var boundMethods = provider.Operations.SelectMany(static operation => operation.Comparisons)
                .Where(comparison => StringComparer.Ordinal.Equals(comparison.CapabilityId, capability.Id))
                .Select(static comparison => comparison.Method).ToHashSet(StringComparer.Ordinal);
            AddIf(!boundMethods.SetEquals(capability.AffectedMethods), "strategy-capability",
                $"Provider '{provider.Provider}' capability '{capability.Id}' affected methods do not match its comparison bindings.");
        }
        if (provider.Provider == "sqlserver")
        {
            var capability = provider.Capabilities.SingleOrDefault();
            AddIf(capability is null || capability.Id != "db-batch" || capability.ProbeMember != "CanCreateBatch" ||
                  capability.SupportedExecutionMode != "native-db-batch-comparison" ||
                  capability.FallbackExecutionMode != "native-comparison-unavailable" ||
                  !capability.AffectedMethods.SequenceEqual(
                      ["Native_DbBatchInsert", "Native_DbBatchUpdate", "Native_DbBatchDelete"], StringComparer.Ordinal),
                "strategy-capability", "SQL Server must retain its exact checked DbBatch capability contract.");
        }
        else
        {
            AddIf(provider.Capabilities.Count != 0, "strategy-capability",
                $"Provider '{provider.Provider}' cannot declare an unchecked runtime capability.");
        }

        foreach (var operation in provider.Operations)
        {
            var prefix = $"Provider '{provider.Provider}' operation '{operation.Operation}'";
            AddIf(string.IsNullOrWhiteSpace(operation.BenchmarkCategory) ||
                  string.IsNullOrWhiteSpace(operation.SelectedMethod) ||
                  string.IsNullOrWhiteSpace(operation.ProductionExecutionMode) ||
                  string.IsNullOrWhiteSpace(operation.SqlShape) ||
                  string.IsNullOrWhiteSpace(operation.ChunkPolicy),
                "strategy-operation", $"{prefix} must declare category, selected method, execution mode, SQL shape, and chunk policy.");
            if (CheckedOperations.TryGetValue((provider.Provider, operation.Operation), out var checkedOperation))
            {
                AddIf(operation.BenchmarkCategory != checkedOperation.Category ||
                      operation.SelectedMethod != checkedOperation.SelectedMethod ||
                      operation.ProductionExecutionMode != checkedOperation.ExecutionMode ||
                      operation.SqlShape != checkedOperation.SqlShape ||
                      operation.ChunkPolicy != checkedOperation.ChunkPolicy,
                    "strategy-operation-contract", $"{prefix} does not match its exact checked strategy contract.");
            }
            AddIf(!operation.SelectedMethod.StartsWith("Inquiry_Selected", StringComparison.Ordinal) ||
                  ContainsFloorOrControl(operation.SelectedMethod),
                "strategy-selected-method", $"{prefix} must select an Inquiry_Selected method that is neither a floor nor a control.");
            AddIf(operation.Comparisons.Count == 0 ||
                  operation.Comparisons.GroupBy(static comparison => comparison.Method, StringComparer.Ordinal)
                      .Any(static group => group.Count() != 1) ||
                  operation.Comparisons.Any(comparison =>
                      StringComparer.Ordinal.Equals(comparison.Method, operation.SelectedMethod)),
                "strategy-comparisons", $"{prefix} must declare unique non-selected comparison methods.");

            foreach (var comparison in operation.Comparisons)
            {
                AddIf(!CheckedComparisonRoles.TryGetValue(comparison.Method, out var checkedRole) ||
                      comparison.Role != checkedRole, "strategy-comparison-role",
                    $"{prefix} comparison '{comparison.Method}' is mislabeled as {comparison.Role}.");
                AddIf(comparison.CapabilityId is not null &&
                      !provider.Capabilities.Any(capability => StringComparer.Ordinal.Equals(capability.Id, comparison.CapabilityId)),
                    "strategy-capability", $"{prefix} comparison '{comparison.Method}' references an unknown capability.");
            }

            foreach (var capability in provider.Capabilities)
            {
                var comparisonMethods = provider.Operations.SelectMany(static item => item.Comparisons)
                    .Select(static comparison => comparison.Method).ToHashSet(StringComparer.Ordinal);
                AddIf(capability.AffectedMethods.Any(method => !comparisonMethods.Contains(method)), "strategy-capability",
                    $"Provider '{provider.Provider}' capability '{capability.Id}' references a missing comparison method.");
            }

            var cellGroups = operation.Cells.GroupBy(static cell => cell.Cardinality).ToArray();
            AddIf(cellGroups.Any(static group => group.Count() != 1) ||
                  !cellGroups.Select(static group => group.Key).Order().SequenceEqual(SelectedBatchStrategySchema.Cardinalities),
                "strategy-cells", $"{prefix} must contain each required cardinality exactly once.");
            AddIf(!operation.Cells.Select(static cell => cell.Cardinality)
                    .SequenceEqual(SelectedBatchStrategySchema.Cardinalities),
                "strategy-cell-order", $"{prefix} cells must use canonical checked cardinality order.");
            foreach (var cell in operation.Cells) ValidateCell(prefix, provider, operation, cell, errors);
        }

        void AddIf(bool condition, string code, string message)
        {
            if (condition) errors.Add(new(code, message));
        }
    }

    private static void ValidateCell(
        string prefix,
        SelectedProviderStrategy provider,
        SelectedOperationStrategy operation,
        SelectedStrategyCell cell,
        List<ContractError> errors)
    {
        if (cell.Status == SelectedStrategyStatus.PendingMeasurement)
        {
            if (cell.Confidence != SelectedStrategyConfidence.Unmeasured || cell.RuntimeCapabilities is not null ||
                cell.SelectedEvidence is not null ||
                cell.ComparisonEvidence.Count != 0)
                errors.Add(new("strategy-pending", $"{prefix} cardinality {cell.Cardinality} pending cells cannot claim evidence or confidence."));
            return;
        }

        if (cell.SelectedEvidence is null)
            errors.Add(new("strategy-evidence", $"{prefix} cardinality {cell.Cardinality} requires selected-method evidence."));
        if (cell.Status == SelectedStrategyStatus.Provisional && cell.Confidence != SelectedStrategyConfidence.Diagnostic)
            errors.Add(new("strategy-confidence", $"{prefix} provisional cells must have diagnostic confidence."));
        if (cell.Status == SelectedStrategyStatus.Accepted && cell.Confidence != SelectedStrategyConfidence.Authoritative)
            errors.Add(new("strategy-confidence", $"{prefix} accepted cells must have authoritative confidence."));
        var expectedCapabilityKeys = provider.Capabilities.Select(static capability => capability.Id)
            .Append("selected-execution-mode")
            .ToHashSet(StringComparer.Ordinal);
        if (cell.RuntimeCapabilities is null ||
            !cell.RuntimeCapabilities.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedCapabilityKeys) ||
            !cell.RuntimeCapabilities.TryGetValue("selected-execution-mode", out var executionMode) ||
            !StringComparer.Ordinal.Equals(executionMode, operation.ProductionExecutionMode))
            errors.Add(new("strategy-runtime-capability",
                $"{prefix} measured cells must bind an exact runtime capability map to the selected execution mode."));
        else if (provider.Capabilities.Any(capability =>
                     !cell.RuntimeCapabilities.TryGetValue(capability.Id, out var value) ||
                     value != capability.SupportedExecutionMode && value != capability.FallbackExecutionMode))
            errors.Add(new("strategy-runtime-capability",
                $"{prefix} measured cells must record every provider capability as its exact supported or fallback execution mode."));

        var comparisonMethods = cell.ComparisonEvidence.Select(static item => item.Method).ToArray();
        if (comparisonMethods.Distinct(StringComparer.Ordinal).Count() != comparisonMethods.Length ||
            comparisonMethods.Any(method => !operation.Comparisons.Any(comparison =>
                StringComparer.Ordinal.Equals(comparison.Method, method))))
            errors.Add(new("strategy-comparison-evidence", $"{prefix} cardinality {cell.Cardinality} has duplicate or unknown comparison evidence."));
        if (cell.Status == SelectedStrategyStatus.Accepted)
        {
            var requiredComparisons = operation.Comparisons
                .Where(static comparison => comparison.Role != ComparisonMethodRole.Control)
                .Select(static comparison => comparison.Method)
                .ToHashSet(StringComparer.Ordinal);
            if (!comparisonMethods.ToHashSet(StringComparer.Ordinal).SetEquals(requiredComparisons))
                errors.Add(new("strategy-comparison-evidence",
                    $"{prefix} cardinality {cell.Cardinality} accepted cells require evidence for every non-control comparison."));
        }
        if (cell.Status == SelectedStrategyStatus.Provisional &&
            !comparisonMethods.Any(method => operation.Comparisons.Any(comparison =>
                comparison.Role != ComparisonMethodRole.Control &&
                StringComparer.Ordinal.Equals(comparison.Method, method))))
            errors.Add(new("strategy-comparison-evidence",
                $"{prefix} cardinality {cell.Cardinality} provisional cells require non-control comparison evidence."));
        foreach (var reference in EnumerateReferences(cell)) ValidateReference(reference, errors);
    }

    private static void ValidateSurface(
        SelectedProviderStrategy provider,
        SelectedStrategyValidationContext context,
        List<ContractError> errors)
    {
        if (!context.BenchmarkTypes!.TryGetValue(provider.Provider, out var type))
        {
            errors.Add(new("strategy-benchmark-type", $"Provider '{provider.Provider}' has no resolved benchmark type."));
            return;
        }
        if (!StringComparer.Ordinal.Equals(type.Assembly.GetName().Name, provider.BenchmarkAssembly) ||
            !StringComparer.Ordinal.Equals(type.FullName, provider.BenchmarkType))
            errors.Add(new("strategy-benchmark-type", $"Provider '{provider.Provider}' benchmark type identity does not match the manifest."));

        var rows = type.GetField("Rows", BindingFlags.Instance | BindingFlags.Public);
        var values = rows?.GetCustomAttribute<ParamsAttribute>()?.Values?.Select(Convert.ToInt32).ToArray();
        var parameterDimensions = type.GetMembers(BindingFlags.Instance | BindingFlags.Public)
            .Where(static member => member.GetCustomAttribute<ParamsAttribute>() is not null).ToArray();
        if (rows?.FieldType != typeof(int) || values is null ||
            !values.SequenceEqual(SelectedBatchStrategySchema.Cardinalities) ||
            parameterDimensions.Length != 1 || parameterDimensions[0] is not FieldInfo dimension ||
            dimension.Name != "Rows" || dimension.DeclaringType != type)
            errors.Add(new("strategy-params",
                $"Provider '{provider.Provider}' must expose Rows as its sole parameter dimension with exactly [Params(1, 10, 100, 1000)]."));

        foreach (var operation in provider.Operations)
        {
            ValidateMethod(type, operation.SelectedMethod, operation.BenchmarkCategory, true, errors);
            foreach (var comparison in operation.Comparisons)
                ValidateMethod(type, comparison.Method, operation.BenchmarkCategory, false, errors);

            var compiledComparisons = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetCustomAttribute<BenchmarkAttribute>() is not null &&
                    method.GetCustomAttributes<BenchmarkCategoryAttribute>()
                        .SelectMany(static attribute => attribute.Categories)
                        .Contains(operation.BenchmarkCategory, StringComparer.Ordinal) &&
                    !StringComparer.Ordinal.Equals(method.Name, operation.SelectedMethod))
                .Select(static method => method.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (!compiledComparisons.SetEquals(operation.Comparisons.Select(static comparison => comparison.Method)))
                errors.Add(new("strategy-benchmark-surface",
                    $"Provider '{provider.Provider}' category '{operation.BenchmarkCategory}' must declare every compiled comparison benchmark exactly once."));
        }
        foreach (var capability in provider.Capabilities)
        {
            var property = type.GetProperty(capability.ProbeMember, BindingFlags.Instance | BindingFlags.Public);
            if (property?.PropertyType != typeof(bool) || property.GetMethod is null)
                errors.Add(new("strategy-capability-surface",
                    $"Provider '{provider.Provider}' capability '{capability.Id}' must resolve to a public bool property."));
        }
    }

    private static void ValidateMethod(
        Type type,
        string methodName,
        string category,
        bool selected,
        List<ContractError> errors)
    {
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        var categories = method?.GetCustomAttributes<BenchmarkCategoryAttribute>()
            .SelectMany(static attribute => attribute.Categories).ToArray();
        if (method is null || method.GetCustomAttribute<BenchmarkAttribute>() is null ||
            method.ReturnType != typeof(Task<int>) || categories is null ||
            !categories.Contains(category, StringComparer.Ordinal))
            errors.Add(new("strategy-benchmark-method", $"Benchmark method '{type.FullName}.{methodName}' does not match its checked surface contract."));
        if (selected && (!methodName.StartsWith("Inquiry_Selected", StringComparison.Ordinal) || ContainsFloorOrControl(methodName)))
            errors.Add(new("strategy-selected-method", $"Benchmark method '{type.FullName}.{methodName}' is not a valid selected method."));
    }

    private static void ValidateEvidence(
        SelectedProviderStrategy provider,
        Func<MeasuredEvidenceReference, ResolvedMeasuredEvidence?>? resolver,
        List<ContractError> errors)
    {
        foreach (var operation in provider.Operations)
        foreach (var cell in operation.Cells.Where(static cell => cell.Status != SelectedStrategyStatus.PendingMeasurement))
        {
            if (resolver is null)
            {
                errors.Add(new("strategy-evidence-context", "Measured strategy cells require an evidence resolver."));
                continue;
            }
            if (cell.SelectedEvidence is not null)
                ValidateResolved(provider, operation, cell, operation.SelectedMethod, cell.SelectedEvidence, false, resolver, errors);
            foreach (var comparisonEvidence in cell.ComparisonEvidence)
            {
                var comparison = operation.Comparisons.SingleOrDefault(candidate =>
                    StringComparer.Ordinal.Equals(candidate.Method, comparisonEvidence.Method));
                if (comparison is not null)
                    ValidateResolved(provider, operation, cell, comparison.Method, comparisonEvidence.Evidence,
                        comparison.Role == ComparisonMethodRole.Control, resolver, errors);
            }
        }
    }

    private static void ValidateResolved(
        SelectedProviderStrategy provider,
        SelectedOperationStrategy operation,
        SelectedStrategyCell cell,
        string expectedMethod,
        MeasuredEvidenceReference reference,
        bool control,
        Func<MeasuredEvidenceReference, ResolvedMeasuredEvidence?> resolver,
        List<ContractError> errors)
    {
        var resolved = resolver(reference);
        if (resolved is null)
        {
            errors.Add(new("strategy-evidence-missing", $"Measured evidence '{reference.RelativeArtifactId}' could not be resolved."));
            return;
        }
        var actualHash = Convert.ToHexString(SHA256.HashData(resolved.Json)).ToLowerInvariant();
        if (!StringComparer.Ordinal.Equals(actualHash, reference.Sha256))
            errors.Add(new("strategy-evidence-identity", "Measured evidence content hash or case ID does not match its reference."));
        var artifactValidation = EvidenceArtifactValidator.Validate(resolved.Json, resolved.ValidationContext);
        var evidence = artifactValidation.Artifact;
        if (evidence is null)
        {
            errors.Add(new("strategy-evidence-invalid",
                "Measured strategy evidence bytes could not be validated as a benchmark evidence artifact."));
            return;
        }
        if (!StringComparer.Ordinal.Equals(evidence.CaseId, reference.CaseId))
            errors.Add(new("strategy-evidence-identity", "Measured evidence content hash or case ID does not match its reference."));
        if (!StringComparer.OrdinalIgnoreCase.Equals(evidence.CaseKey.Provider, provider.Provider) ||
            !StringComparer.OrdinalIgnoreCase.Equals(evidence.CaseKey.OperationSemantics, operation.Operation.ToString()) ||
            evidence.CaseKey.Cardinality != cell.Cardinality ||
            !StringComparer.Ordinal.Equals(evidence.BenchmarkTarget.AssemblyName, provider.BenchmarkAssembly) ||
            !StringComparer.Ordinal.Equals(evidence.BenchmarkTarget.TypeName, provider.BenchmarkType) ||
            !StringComparer.Ordinal.Equals(evidence.BenchmarkTarget.MethodName, expectedMethod) ||
            evidence.BenchmarkTarget.Cardinality != cell.Cardinality ||
            evidence.BenchmarkTarget.Parameters.Count != 1 ||
            !evidence.BenchmarkTarget.Parameters.TryGetValue("Rows", out var rows) ||
            rows != cell.Cardinality.ToString(System.Globalization.CultureInfo.InvariantCulture))
            errors.Add(new("strategy-evidence-target", "Measured evidence provider, cardinality, or benchmark target does not match the strategy cell."));
        if (cell.RuntimeCapabilities is null || !evidence.RuntimeCapabilities.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(cell.RuntimeCapabilities.OrderBy(static pair => pair.Key, StringComparer.Ordinal)))
            errors.Add(new("strategy-evidence-capability", "Measured evidence runtime capabilities do not exactly match the strategy cell."));
        if (artifactValidation.Errors.Count != 0)
            errors.Add(new("strategy-evidence-invalid", "Measured strategy evidence does not satisfy the production artifact validator."));
        var checkedJob = BenchmarkJobCatalog.Jobs.SingleOrDefault(job =>
            StringComparer.Ordinal.Equals(job.Id, evidence.BenchmarkDotNet.JobId));
        var releaseAuthoritative = evidence.Authoritative && evidence.Source.ReleaseEligible &&
                                   checkedJob is not null &&
                                   StringComparer.Ordinal.Equals(evidence.BenchmarkJobContractHash, checkedJob.IdentityHash);
        if (cell.Status == SelectedStrategyStatus.Accepted && (!releaseAuthoritative || control))
            errors.Add(new("strategy-evidence-authority", "Accepted strategy evidence must be authoritative and cannot target a control benchmark."));
        if (control && evidence.Authoritative)
            errors.Add(new("strategy-control-authority", "Control benchmark evidence can never be authoritative."));
    }

    private static IEnumerable<MeasuredEvidenceReference> EnumerateReferences(SelectedStrategyCell cell)
    {
        if (cell.SelectedEvidence is not null) yield return cell.SelectedEvidence;
        foreach (var comparison in cell.ComparisonEvidence) yield return comparison.Evidence;
    }

    private static void ValidateReference(MeasuredEvidenceReference reference, List<ContractError> errors)
    {
        var safePath = !string.IsNullOrWhiteSpace(reference.RelativeArtifactId) &&
                       reference.RelativeArtifactId == reference.RelativeArtifactId.Trim() &&
                       !reference.RelativeArtifactId.StartsWith("/", StringComparison.Ordinal) &&
                       !reference.RelativeArtifactId.Contains('\\') && !reference.RelativeArtifactId.Contains(':') &&
                       reference.RelativeArtifactId.Split('/').All(static segment => segment is not ("" or "." or ".."));
        if (!safePath || !IsSha256(reference.Sha256) || !IsSha256(reference.CaseId))
            errors.Add(new("strategy-evidence-reference", "Measured evidence references require a safe relative path, SHA-256, and case ID."));
    }

    private static bool ContainsFloorOrControl(string method)
        => method.Contains("Floor", StringComparison.OrdinalIgnoreCase) ||
           method.Contains("Control", StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public static class SelectedBatchStrategyArtifactValidator
{
    public static ArtifactValidationResult<SelectedBatchStrategyManifest> Validate(
        byte[] json,
        SelectedStrategyValidationContext? context = null)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            return new(null, [new("invalid-json", "Selected strategy artifact is not valid JSON.")]);
        }

        using (document)
        {
            var schema = CheckedArtifactSchemas.SelectedStrategy.Evaluate(
                document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!schema.IsValid)
                return new(null, [new("json-schema", "Selected strategy artifact does not match the closed checked schema.")]);
            var hygiene = EvidenceHygieneValidator.Validate(json);
            if (hygiene.Count != 0) return new(null, hygiene);
            SelectedBatchStrategyManifest? artifact;
            try { artifact = document.RootElement.Deserialize<SelectedBatchStrategyManifest>(EvidenceJson.Options); }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return new(null, [new("deserialize", "Selected strategy artifact could not be deserialized after schema validation.")]);
            }
            if (artifact is null) return new(null, [new("deserialize", "Selected strategy artifact deserialized to null.")]);
            var errors = SelectedBatchStrategyValidator.Validate(artifact, context);
            return new(artifact, errors);
        }
    }
}
