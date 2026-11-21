using System.Data;
using System.Data.Common;

namespace Inquiry.Testing;

/// <summary>
/// An immutable snapshot of one command executed through the Inquiry pipeline, captured by
/// <see cref="RecordingCommandInterceptor"/> or <see cref="ScriptedDatabase"/>.
/// </summary>
public sealed class RecordedCommand
{
    internal RecordedCommand(
        string commandText, CommandType commandType, IReadOnlyList<RecordedParameter> parameters, RecordedTransaction? transaction = null)
    {
        CommandText = commandText;
        CommandType = commandType;
        Parameters = parameters;
        Transaction = transaction;
    }

    /// <summary>
    /// Gets the SQL command text as it was about to execute.
    /// </summary>
    public string CommandText { get; }

    /// <summary>
    /// Gets how the command text is read, for example <see cref="CommandType.StoredProcedure"/> for a
    /// stored-procedure call.
    /// </summary>
    public CommandType CommandType { get; }

    /// <summary>
    /// Gets the parameters snapshotted immediately before execution.
    /// </summary>
    public IReadOnlyList<RecordedParameter> Parameters { get; }

    /// <summary>
    /// Gets the transaction the command ran in, or <see langword="null"/> when it ran without one.
    /// Only <see cref="ScriptedDatabase"/> records it; <see cref="RecordingCommandInterceptor"/>
    /// leaves it <see langword="null"/>.
    /// </summary>
    public RecordedTransaction? Transaction { get; }

    /// <summary>
    /// Gets the affected row count reported after successful execution, when available.
    /// <see langword="null"/> until the command completes, for queries that do not report a
    /// count, and for failed commands.
    /// </summary>
    public int? RecordsAffected
    {
        get
        {
            var value = Volatile.Read(ref _recordsAffected);
            return value == NotSet ? null : value;
        }
        internal set => Volatile.Write(ref _recordsAffected, value ?? NotSet);
    }

    /// <summary>
    /// Gets the exception thrown by the command, or <see langword="null"/> when the command
    /// succeeded (or has not completed).
    /// </summary>
    public Exception? Exception
    {
        get => Volatile.Read(ref _exception);
        internal set => Volatile.Write(ref _exception, value);
    }

    private const int NotSet = int.MinValue;
    private int _recordsAffected = NotSet;
    private Exception? _exception;
}

/// <summary>
/// An immutable snapshot of one bound parameter.
/// </summary>
public sealed class RecordedParameter
{
    internal RecordedParameter(DbParameter parameter)
        : this(parameter, parameter.Value)
    {
    }

    internal RecordedParameter(DbParameter parameter, object? value)
    {
        Name = parameter.ParameterName;
        Value = value;
        DbType = parameter.DbType;
        Direction = parameter.Direction;
        Size = parameter.Size;
        Precision = parameter.Precision;
        Scale = parameter.Scale;
        ProviderParameter = (parameter as ICloneable)?.Clone() as DbParameter;
        if (ProviderParameter is not null && !ReferenceEquals(value, parameter.Value))
        {
            // The copied table-valued rows replace the single-pass source, which is spent by now.
            ProviderParameter.Value = value;
        }
    }

    /// <summary>
    /// Gets the parameter name as bound on the <see cref="System.Data.Common.DbCommand"/>
    /// (including any provider prefix, e.g. <c>@Id</c>).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the parameter value at executing-time. For a table-valued parameter recorded by
    /// <see cref="ScriptedDatabase"/>, it is the rows the command sent, as an
    /// <see cref="IReadOnlyList{T}"/> of <c>object?[]</c>, one array per row.
    /// </summary>
    public object? Value { get; }

    /// <summary>Gets the parameter's <see cref="System.Data.DbType"/> at executing-time.</summary>
    public DbType DbType { get; }

    /// <summary>Gets the parameter's direction at executing-time.</summary>
    public ParameterDirection Direction { get; }

    /// <summary>Gets the parameter's size at executing-time.</summary>
    public int Size { get; }

    /// <summary>Gets the parameter's precision at executing-time.</summary>
    public byte Precision { get; }

    /// <summary>Gets the parameter's scale at executing-time.</summary>
    public byte Scale { get; }

    /// <summary>
    /// Gets a copy of the provider's parameter object, taken at executing-time, or
    /// <see langword="null"/> when the provider's parameter does not implement
    /// <see cref="ICloneable"/>. Cast it to read the provider type, for example
    /// <c>((SqlParameter)parameter.ProviderParameter).SqlDbType</c>. SqlClient's parameter
    /// supports this. SQLite's parameter does not, so a <c>SqliteType</c> set directly on it is
    /// not recorded. Inquiry's SQLite binding sets only <see cref="DbType"/>.
    /// </summary>
    public DbParameter? ProviderParameter { get; }
}
