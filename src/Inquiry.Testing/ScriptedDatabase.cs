using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Inquiry.Testing;

/// <summary>
/// Runs the built-in Inquiry runtime over scripted results instead of a database. Register
/// <see cref="DataSource"/> with a provider that accepts a <see cref="DbDataSource"/>, script
/// the outcome of each command, then assert on <see cref="Commands"/> and
/// <see cref="Transactions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Only the ADO.NET connection is scripted. <c>DefaultInquiry</c>, generated stores, parameter
/// binding, name-based result mapping, converters, transactions and interceptors are the
/// production code. Parameters are created by <c>providerFactory</c>, so they carry the
/// provider types the production path binds.
/// </para>
/// <code>
/// var database = new ScriptedDatabase(SqlClientFactory.Instance);
/// services.AddInquiry().AddInquirySqlServer(database.DataSource);
///
/// database.Enqueue(ScriptedResult.Rows(new ScriptedResultSet()
///     .Column&lt;int&gt;("Id").Column&lt;string&gt;("Name")
///     .Row(1, "Ada")));
/// </code>
/// <para>
/// Each command first tries the <see cref="When"/> rules in the order they were added, and then
/// takes the next <see cref="Enqueue"/> result. A command that matches neither fails with
/// <see cref="InvalidOperationException"/> that shows its text and parameters. The database
/// does not run SQL, so locks, constraints and the SQL text itself are not checked.
/// </para>
/// </remarks>
public sealed class ScriptedDatabase
{
    private readonly object _lock = new();
    private readonly Queue<ScriptedResult> _ordered = new();
    private readonly List<(Func<RecordedCommand, bool> Matches, ScriptedResult Result)> _rules = new();
    private readonly List<RecordedCommand> _commands = new();
    private readonly List<RecordedTransaction> _transactions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptedDatabase"/> class.
    /// </summary>
    /// <param name="providerFactory">
    /// The provider's factory, such as <c>SqlClientFactory.Instance</c> or
    /// <c>SqliteFactory.Instance</c>. It creates the parameters and decides whether the
    /// connection supports <see cref="DbBatch"/>, as the provider's own connection does.
    /// </param>
    public ScriptedDatabase(DbProviderFactory providerFactory)
    {
        ProviderFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        DataSource = new ScriptedDataSource(this);
    }

    /// <summary>
    /// Gets the data source to register, for example with <c>AddInquirySqlServer(DbDataSource)</c>
    /// or <c>AddInquirySqlite(DbDataSource)</c>.
    /// </summary>
    public DbDataSource DataSource { get; }

    /// <summary>Gets a snapshot of the commands run so far, in execution order.</summary>
    public IReadOnlyList<RecordedCommand> Commands
    {
        get
        {
            lock (_lock)
            {
                return _commands.ToArray();
            }
        }
    }

    /// <summary>Gets a snapshot of the transactions begun so far, in order.</summary>
    public IReadOnlyList<RecordedTransaction> Transactions
    {
        get
        {
            lock (_lock)
            {
                return _transactions.ToArray();
            }
        }
    }

    internal DbProviderFactory ProviderFactory { get; }

    /// <summary>Adds a result for the next command that no <see cref="When"/> rule matches.</summary>
    /// <param name="result">The scripted outcome.</param>
    /// <returns>This database.</returns>
    public ScriptedDatabase Enqueue(ScriptedResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_lock)
        {
            _ordered.Enqueue(result);
        }

        return this;
    }

    /// <summary>
    /// Adds a rule that answers every command that <paramref name="command"/> matches, such as
    /// <c>command => command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal)</c>.
    /// </summary>
    /// <param name="command">
    /// A predicate on the command as it is about to run: its text, parameters and transaction.
    /// </param>
    /// <param name="result">The scripted outcome.</param>
    /// <returns>This database.</returns>
    public ScriptedDatabase When(Func<RecordedCommand, bool> command, ScriptedResult result)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        lock (_lock)
        {
            _rules.Add((command, result));
        }

        return this;
    }

    internal RecordedTransaction BeginTransaction(System.Data.IsolationLevel isolationLevel)
    {
        var transaction = new RecordedTransaction(isolationLevel);
        lock (_lock)
        {
            _transactions.Add(transaction);
        }

        return transaction;
    }

    internal RecordedTransaction BeginSavepoint(RecordedTransaction parent)
    {
        var savepoint = new RecordedTransaction(parent.IsolationLevel, parent);
        lock (_lock)
        {
            _transactions.Add(savepoint);
        }

        return savepoint;
    }

    /// <summary>
    /// Records the command and returns its scripted result. Throws the scripted exception, or an
    /// <see cref="InvalidOperationException"/> when no script matches.
    /// </summary>
    internal (ScriptedResult Result, RecordedCommand Command) Execute(
        string commandText, System.Data.CommandType commandType, DbParameterCollection parameters, RecordedTransaction? transaction)
    {
        var snapshot = new List<RecordedParameter>(parameters.Count);
        Exception? readFailure = null;
        foreach (DbParameter parameter in parameters)
        {
            object? rows = null;
            if (readFailure is null)
            {
                try
                {
                    rows = ReadRows(parameter.Value);
                }
                catch (Exception failure)
                {
                    readFailure = failure;
                }
            }

            snapshot.Add(rows is null ? new RecordedParameter(parameter) : new RecordedParameter(parameter, rows));
        }

        var recorded = new RecordedCommand(commandText, commandType, snapshot, transaction);
        ScriptedResult? result = null;
        lock (_lock)
        {
            _commands.Add(recorded);
            if (readFailure is null)
            {
                try
                {
                    result = _rules.FirstOrDefault(rule => rule.Matches(recorded)).Result;
                }
                catch (Exception failure)
                {
                    readFailure = new InvalidOperationException(
                        "A When rule threw for this command." + DescribeCommand(recorded), failure);
                }

                if (readFailure is null && result is null && _ordered.Count > 0)
                {
                    result = _ordered.Dequeue();
                }
            }
        }

        var exception = readFailure
            ?? (result is null
                ? new InvalidOperationException(DescribeUnscripted(recorded))
                : result.Exception ?? ApplyOutputs(parameters, result));
        if (exception is not null)
        {
            recorded.Exception = exception;
            throw exception;
        }

        return (result!, recorded);
    }

    /// <summary>
    /// Reads a table-valued parameter's rows, as the provider does when it sends the command. The
    /// runtime binds a single-pass source and disposes it after the command, so the rows must be
    /// copied now. A failure in the source then fails the command, as it would in production.
    /// </summary>
    private static List<object?[]>? ReadRows(object? value)
    {
        if (value is DbDataReader reader)
        {
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                rows.Add(CopyRecord(reader));
            }

            return rows;
        }

        return value is IEnumerable<System.Data.IDataRecord> records ? records.Select(CopyRecord).ToList() : null;
    }

    private static object?[] CopyRecord(System.Data.IDataRecord record)
    {
        var values = new object[record.FieldCount];
        record.GetValues(values);
        return values;
    }

    /// <summary>
    /// Sets each output parameter from the script. A missing value fails, because a default would
    /// let a stored-procedure read-back pass without being scripted.
    /// </summary>
    private static InvalidOperationException? ApplyOutputs(DbParameterCollection parameters, ScriptedResult result)
    {
        var assigned = 0;
        foreach (DbParameter parameter in parameters)
        {
            if (parameter.Direction == System.Data.ParameterDirection.Input)
            {
                continue;
            }

            if (parameter.Direction == System.Data.ParameterDirection.ReturnValue)
            {
                if (!result.HasReturnValue)
                {
                    return new InvalidOperationException(
                        $"The command reads a return value through '{parameter.ParameterName}', but its scripted result sets none. "
                        + "Call ScriptedResult.WithReturnValue to set one.");
                }

                parameter.Value = result.ReturnValue;
                continue;
            }

            if (!result.Outputs.TryGetValue(ScriptedResult.LogicalName(parameter.ParameterName), out var value))
            {
                return new InvalidOperationException(
                    $"The command has {parameter.Direction} parameter '{parameter.ParameterName}', but its scripted result "
                    + "sets no value for it. Call ScriptedResult.WithOutput to set one.");
            }

            parameter.Value = value;
            assigned++;
        }

        return assigned == result.Outputs.Count
            ? null
            : new InvalidOperationException(
                "The scripted result sets output values for parameters the command does not have as output parameters: "
                + string.Join(", ", result.Outputs.Keys) + ".");
    }

    private static string DescribeUnscripted(RecordedCommand command)
        => "No scripted result matches this command." + DescribeCommand(command);

    private static string DescribeCommand(RecordedCommand command)
    {
        var builder = new StringBuilder()
            .AppendLine().Append("  ").Append(command.CommandText);
        if (command.Parameters.Count == 0)
        {
            return builder.AppendLine().Append("Parameters: none").ToString();
        }

        builder.AppendLine().Append("Parameters:");
        foreach (var parameter in command.Parameters)
        {
            builder.AppendLine().Append("  ").Append(parameter.Name)
                .Append(" (").Append(parameter.DbType).Append(") = ")
                .Append(parameter.Value switch
                {
                    null or DBNull => "NULL",
                    IReadOnlyList<object?[]> rows => $"({rows.Count} rows)",
                    var value => Convert.ToString(value, CultureInfo.InvariantCulture),
                });
        }

        return builder.ToString();
    }
}
