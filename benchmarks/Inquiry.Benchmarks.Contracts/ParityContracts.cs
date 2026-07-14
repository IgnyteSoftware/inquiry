using System.Text.RegularExpressions;

namespace Inquiry.Benchmarks.Contracts;

public sealed record CommandNode(
    CommandOperation Operation,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Joins,
    IReadOnlyList<string> Predicates,
    IReadOnlyList<string> OrderBy,
    int? Limit,
    IReadOnlyList<string> ProjectedExpressions,
    IReadOnlyList<string> ParameterTypes,
    IReadOnlyList<string> ParameterNames,
    MutationEffect Mutation,
    TransactionOutcome TransactionOutcome)
{
    internal string CanonicalSemantics => CanonicalHash.Join(
        new[]
        {
            Operation.ToString(),
            CanonicalHash.Join(Tables.Select(NormalizeSqlFragment)),
            CanonicalHash.Join(Joins.Select(NormalizeSqlFragment)),
            CanonicalHash.Join(Predicates.Select(NormalizeSqlFragment)),
            CanonicalHash.Join(OrderBy.Select(NormalizeSqlFragment)),
            Limit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            CanonicalHash.Join(ProjectedExpressions.Select(NormalizeSqlFragment)),
            CanonicalHash.Join(ParameterTypes.Select(NormalizeSqlFragment)),
            CanonicalHash.Join(ParameterNames.Select(NormalizeParameterName)),
            Mutation.ToString(),
            TransactionOutcome.ToString(),
        });

    private static string NormalizeParameterName(string name)
        => name.Trim().TrimStart('@', ':', '$', '?').ToUpperInvariant();

    private static string NormalizeSqlFragment(string value)
    {
        var whitespace = Regex.Replace(value.Trim(), "\\s+", " ");
        return Regex.Replace(whitespace, @"(?<![A-Za-z0-9_])[@:$?]([A-Za-z_][A-Za-z0-9_]*)", "$1")
            .ToUpperInvariant();
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CommandGraph(IReadOnlyList<CommandNode> Commands, IReadOnlyList<string> SqlStatements)
{
    public CommandGraph(IReadOnlyList<CommandNode> commands, string sql)
        : this(commands, commands.Count == 0 && string.IsNullOrEmpty(sql) ? [] : [sql]) { }

    [System.Text.Json.Serialization.JsonIgnore]
    public string SemanticHash => CanonicalHash.Sha256(CanonicalHash.Join(Commands.Select(static command => command.CanonicalSemantics)));
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> SqlFingerprints => SqlStatements.Select(CanonicalHash.Sha256).ToArray();
    [System.Text.Json.Serialization.JsonIgnore]
    public string SqlFingerprint => CanonicalHash.Sha256(CanonicalHash.Join(SqlFingerprints));
}

public sealed record ParityObservation(
    int Count,
    string Checksum,
    string? ErrorClass,
    TransactionOutcome TransactionOutcome,
    int CommandCount,
    BufferingMode Buffering,
    ConnectionLifecycle ConnectionLifecycle,
    PoolingMode Pooling,
    PreparationMode Preparation,
    TimedPathContract TimedPath,
    CommandGraph CommandGraph)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string IdentityHash => CanonicalHash.Sha256(CanonicalHash.Join(
    [
        Count.ToString(System.Globalization.CultureInfo.InvariantCulture), Checksum, ErrorClass ?? string.Empty,
        TransactionOutcome.ToString(), CommandCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Buffering.ToString(), ConnectionLifecycle.ToString(), Pooling.ToString(), Preparation.ToString(),
        TimedPath.IdentityHash, CommandGraph.SemanticHash, CommandGraph.SqlFingerprint,
    ]));
}

public static class ParityValidator
{
    public static IReadOnlyList<ContractError> Validate(BenchmarkScenario scenario, ParityObservation observation)
    {
        var errors = new List<ContractError>();
        AddIf(observation.Count != scenario.Expected.Count, "result-count", "Observed result cardinality differs from the scenario contract.");
        AddIf(!StringComparer.Ordinal.Equals(observation.Checksum, scenario.Expected.Checksum), "result-checksum", "Observed result checksum differs from the scenario contract.");
        AddIf(!StringComparer.Ordinal.Equals(observation.ErrorClass, scenario.Expected.ErrorClass), "result-error", "Observed error class differs from the scenario contract.");
        AddIf(observation.TransactionOutcome != scenario.Expected.TransactionOutcome, "transaction-outcome", "Observed transaction outcome differs from the scenario contract.");
        AddIf(observation.CommandCount != scenario.Expected.CommandCount, "command-count", "Observed command count differs from the scenario contract.");
        AddIf(observation.Buffering != scenario.Key.Buffering, "buffering-mode", "Observed buffering mode differs from the case key.");
        AddIf(observation.ConnectionLifecycle != scenario.Key.ConnectionLifecycle, "connection-lifecycle", "Observed connection lifecycle differs from the case key.");
        AddIf(observation.Pooling != scenario.Key.Pooling, "pooling-mode", "Observed pooling mode differs from the case key.");
        AddIf(observation.Preparation != scenario.Key.Preparation, "preparation-mode", "Observed preparation mode differs from the case key.");
        AddIf(observation.TimedPath != scenario.TimedPath, "timed-boundary", "Observed timed boundary differs from the scenario contract.");
        AddIf(!StringComparer.Ordinal.Equals(observation.CommandGraph.SemanticHash, scenario.ApprovedCommandGraph.SemanticHash), "command-graph", "Observed semantic command graph differs from the approved graph.");
        AddIf(observation.CommandGraph.Commands.Count != observation.CommandGraph.SqlStatements.Count ||
              scenario.ApprovedCommandGraph.Commands.Count != scenario.ApprovedCommandGraph.SqlStatements.Count ||
              !observation.CommandGraph.SqlFingerprints.SequenceEqual(scenario.ApprovedCommandGraph.SqlFingerprints, StringComparer.Ordinal),
            "sql-fingerprint", "Observed ordered per-command SQL fingerprints differ from the approved semantic-node mapping.");
        return errors;

        void AddIf(bool condition, string code, string message)
        {
            if (condition) errors.Add(new(code, message));
        }
    }
}
