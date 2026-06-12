using Inquiry.Commands;
using Inquiry.Interceptors;
using System.Data.Common;
using System.Text;

namespace Inquiry.Testing;

/// <summary>
/// An <see cref="IInquiryCommandInterceptor"/> that records every command executed through the
/// Inquiry pipeline: command text, a snapshot of the bound parameters, the affected row count,
/// and any failure. Thread-safe; test-framework-agnostic (assertion helpers throw
/// <see cref="InvalidOperationException"/>).
/// </summary>
/// <remarks>
/// Register it alongside the pipeline and inspect <see cref="Commands"/> after acting:
/// <code>
/// var recorder = new RecordingCommandInterceptor();
/// services.AddSingleton&lt;IInquiryCommandInterceptor&gt;(recorder);
/// </code>
/// Parameters are snapshotted at executing-time because <see cref="DbParameterCollection"/> is
/// mutable and may be reused or cleared after execution.
/// </remarks>
public sealed class RecordingCommandInterceptor : IInquiryCommandInterceptor
{
    private readonly object _lock = new();
    private readonly List<RecordedCommand> _commands = new();
    private readonly Dictionary<DbCommand, RecordedCommand> _inFlight = new();

    /// <summary>
    /// Gets a snapshot of the commands recorded so far, in execution order.
    /// </summary>
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

    /// <summary>
    /// Removes all recorded commands.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _commands.Clear();
            _inFlight.Clear();
        }
    }

    /// <inheritdoc />
    public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var parameters = new List<RecordedParameter>(context.Command.Parameters.Count);
        foreach (DbParameter parameter in context.Command.Parameters)
        {
            parameters.Add(new RecordedParameter(parameter.ParameterName, parameter.Value));
        }

        var recorded = new RecordedCommand(context.Command.CommandText, parameters);
        lock (_lock)
        {
            _commands.Add(recorded);
            _inFlight[context.Command] = recorded;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask CommandExecutedAsync(InquiryCommandExecutedContext context, CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        lock (_lock)
        {
            if (_inFlight.Remove(context.Command, out var recorded))
            {
                recorded.RecordsAffected = context.RecordsAffected;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask CommandFailedAsync(InquiryCommandFailedContext context, CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        lock (_lock)
        {
            if (_inFlight.Remove(context.Command, out var recorded))
            {
                recorded.Exception = context.Exception;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Asserts that at least one recorded command's text contains
    /// <paramref name="commandTextSubstring"/> (ordinal comparison). Throws
    /// <see cref="InvalidOperationException"/> listing the recorded SQL when no match is found.
    /// </summary>
    /// <returns>The first matching recorded command.</returns>
    public RecordedCommand AssertExecuted(string commandTextSubstring)
    {
        if (string.IsNullOrEmpty(commandTextSubstring))
        {
            throw new ArgumentException("Command text substring cannot be empty.", nameof(commandTextSubstring));
        }

        var commands = Commands;
        foreach (var command in commands)
        {
            if (command.CommandText.Contains(commandTextSubstring, StringComparison.Ordinal))
            {
                return command;
            }
        }

        throw new InvalidOperationException(
            "Expected a command containing \"" + commandTextSubstring + "\" to have executed, but none matched." + DescribeCommands(commands));
    }

    /// <summary>
    /// Asserts that no recorded command's text contains <paramref name="commandTextSubstring"/>
    /// (ordinal comparison). Throws <see cref="InvalidOperationException"/> listing the recorded
    /// SQL when a match is found.
    /// </summary>
    public void AssertNotExecuted(string commandTextSubstring)
    {
        if (string.IsNullOrEmpty(commandTextSubstring))
        {
            throw new ArgumentException("Command text substring cannot be empty.", nameof(commandTextSubstring));
        }

        var commands = Commands;
        foreach (var command in commands)
        {
            if (command.CommandText.Contains(commandTextSubstring, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Expected no command containing \"" + commandTextSubstring + "\" to have executed, but one did." + DescribeCommands(commands));
            }
        }
    }

    private static string DescribeCommands(IReadOnlyList<RecordedCommand> commands)
    {
        if (commands.Count == 0)
        {
            return " No commands were recorded.";
        }

        var builder = new StringBuilder(" Recorded commands:");
        for (var i = 0; i < commands.Count; i++)
        {
            builder.Append(Environment.NewLine).Append("  [").Append(i).Append("] ").Append(commands[i].CommandText);
        }

        return builder.ToString();
    }
}
