namespace Inquiry.Testing;

/// <summary>
/// The scripted outcome of one command run against a <see cref="ScriptedDatabase"/>.
/// </summary>
/// <remarks>
/// The runtime chooses the ADO.NET call, so each outcome answers all of them:
/// <list type="table">
/// <listheader><term>Outcome</term><description>Reader / non-query / scalar</description></listheader>
/// <item><term><see cref="Rows"/></term><description>The result sets / -1 / first column of the first row, or <see langword="null"/>.</description></item>
/// <item><term><see cref="RowCount"/></term><description>No result sets with that affected count / the count / <see langword="null"/>.</description></item>
/// <item><term><see cref="Scalar"/></term><description>One unnamed column with one row / -1 / the value.</description></item>
/// <item><term><see cref="Throws"/></term><description>Each call throws the exception.</description></item>
/// </list>
/// Use <see cref="WithOutput"/> and <see cref="WithReturnValue"/> for stored-procedure results.
/// </remarks>
public sealed class ScriptedResult
{
    private ScriptedResult(
        ScriptedResultSetData[] resultSets, int recordsAffected, Exception? exception,
        IReadOnlyDictionary<string, object?>? outputs = null)
    {
        ResultSets = resultSets;
        RecordsAffected = recordsAffected;
        Exception = exception;
        Outputs = outputs ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    internal ScriptedResultSetData[] ResultSets { get; }

    internal int RecordsAffected { get; }

    internal Exception? Exception { get; }

    /// <summary>Output values keyed by parameter name without its sigil.</summary>
    internal IReadOnlyDictionary<string, object?> Outputs { get; }

    /// <summary>
    /// Returns a copy of this result that also sets an output or input-output parameter,
    /// as a stored procedure does. A command that has such a parameter fails unless its result sets
    /// a value for it. Use <see cref="WithReturnValue"/> for the return value.
    /// </summary>
    /// <param name="parameterName">The parameter name, with or without its sigil, such as <c>@Total</c>.</param>
    /// <param name="value">The value, or <see langword="null"/> for a database null.</param>
    public ScriptedResult WithOutput(string parameterName, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(parameterName);
        var outputs = new Dictionary<string, object?>(Outputs, StringComparer.OrdinalIgnoreCase)
        {
            [LogicalName(parameterName)] = value ?? DBNull.Value,
        };
        return new ScriptedResult(ResultSets, RecordsAffected, Exception, outputs)
        {
            HasReturnValue = HasReturnValue,
            ReturnValue = ReturnValue,
        };
    }

    /// <summary>
    /// Returns a copy of this result that also sets the stored procedure's return value, which
    /// the command reads through its <see cref="System.Data.ParameterDirection.ReturnValue"/>
    /// parameter.
    /// </summary>
    /// <param name="value">The return value.</param>
    public ScriptedResult WithReturnValue(object? value)
        => new(ResultSets, RecordsAffected, Exception, Outputs) { HasReturnValue = true, ReturnValue = value ?? DBNull.Value };

    internal bool HasReturnValue { get; private init; }

    internal object? ReturnValue { get; private init; }

    internal static string LogicalName(string parameterName)
        => parameterName.Length > 0 && parameterName[0] is '@' or ':' or '$' or '?'
            ? parameterName.Substring(1)
            : parameterName;

    /// <summary>Returns one or more result sets.</summary>
    /// <param name="resultSets">The result sets, in the order the reader returns them.</param>
    public static ScriptedResult Rows(params ScriptedResultSet[] resultSets)
    {
        ArgumentNullException.ThrowIfNull(resultSets);
        if (resultSets.Length == 0)
        {
            throw new ArgumentException("Give at least one result set.", nameof(resultSets));
        }

        var data = new ScriptedResultSetData[resultSets.Length];
        for (var i = 0; i < resultSets.Length; i++)
        {
            data[i] = (resultSets[i] ?? throw new ArgumentException("A result set is null.", nameof(resultSets))).Snapshot();
        }

        return new ScriptedResult(data, -1, null);
    }

    /// <summary>Reports an affected row count and returns no rows.</summary>
    /// <param name="recordsAffected">
    /// The number of rows the command affected, or -1 when the provider reports no count, as
    /// SQL Server does under <c>SET NOCOUNT ON</c>.
    /// </param>
    public static ScriptedResult RowCount(int recordsAffected)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordsAffected, -1);
        return new ScriptedResult(Array.Empty<ScriptedResultSetData>(), recordsAffected, null);
    }

    /// <summary>Returns a single value.</summary>
    /// <param name="value">The value, or <see langword="null"/> for a database null.</param>
    public static ScriptedResult Scalar(object? value)
    {
        var resultSet = new ScriptedResultSetData(
            new[] { string.Empty },
            new[] { value?.GetType() ?? typeof(object) },
            new[] { new[] { value ?? DBNull.Value } });
        return new ScriptedResult(new[] { resultSet }, -1, null);
    }

    /// <summary>
    /// Fails the command with <paramref name="exception"/>, such as a provider exception for a
    /// unique-key violation.
    /// </summary>
    /// <param name="exception">The exception the command throws.</param>
    public static ScriptedResult Throws(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new ScriptedResult(Array.Empty<ScriptedResultSetData>(), -1, exception);
    }
}
