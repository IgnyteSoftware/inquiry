namespace Inquiry.Testing;

/// <summary>
/// An immutable snapshot of one command executed through the Inquiry pipeline, captured by
/// <see cref="RecordingCommandInterceptor"/>.
/// </summary>
public sealed class RecordedCommand
{
    internal RecordedCommand(string commandText, IReadOnlyList<RecordedParameter> parameters)
    {
        CommandText = commandText;
        Parameters = parameters;
    }

    /// <summary>
    /// Gets the SQL command text as it was about to execute.
    /// </summary>
    public string CommandText { get; }

    /// <summary>
    /// Gets the parameters snapshotted immediately before execution.
    /// </summary>
    public IReadOnlyList<RecordedParameter> Parameters { get; }

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
/// An immutable name/value snapshot of one bound parameter.
/// </summary>
public sealed class RecordedParameter
{
    internal RecordedParameter(string name, object? value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>
    /// Gets the parameter name as bound on the <see cref="System.Data.Common.DbCommand"/>
    /// (including any provider prefix, e.g. <c>@Id</c>).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the parameter value at executing-time.
    /// </summary>
    public object? Value { get; }
}
