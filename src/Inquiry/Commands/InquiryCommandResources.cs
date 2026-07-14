using System.ComponentModel;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Inquiry.Commands;

/// <summary>Generated-support lifetime scope for resources retained by command parameter binders.</summary>
/// <remarks>
/// Built-in Inquiry pipelines call <see cref="Dispose(DbCommand)"/> in their command cleanup paths.
/// Custom pipelines and direct binder callers must do the same before disposing the command. If both
/// execution and cleanup fail, preserve the execution exception as the first inner exception of an
/// <see cref="AggregateException"/> and append cleanup failures in disposal order.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class InquiryCommandResources
{
    private static readonly ConditionalWeakTable<DbCommand, ResourceSet> Resources = new();

    internal static void Register(DbCommand command, IInquiryExecutionResource resource)
        => Resources.GetOrCreateValue(command).Add(resource);

    internal static void Unregister(DbCommand command, IInquiryExecutionResource resource)
    {
        if (Resources.TryGetValue(command, out var set)) set.Remove(resource);
    }

    internal static CommandResourceScope CreateScope(DbCommand command, DbConnection? ownedConnection = null)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        return new CommandResourceScope(command, ownedConnection);
    }

    /// <summary>Disposes and detaches all binder-owned resources registered for a command.</summary>
    /// <remarks>
    /// This method is idempotent after resources have been detached. It may throw a resource's disposal
    /// exception or an <see cref="AggregateException"/> when multiple resources fail to dispose.
    /// </remarks>
    public static void Dispose(DbCommand command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (!Resources.TryGetValue(command, out var set)) return;
        Resources.Remove(command);
        set.Dispose();
    }

    /// <summary>
    /// Releases one execution's resources without allocating a per-command owner on the success path.
    /// The primary exception is not rethrown here unless cleanup also fails; the caller's active throw
    /// retains its original stack. Cleanup failures are appended after the primary in execution order.
    /// </summary>
    internal static async ValueTask DisposeExecutionAsync(
        DbCommand command,
        DbDataReader? reader,
        DbConnection? ownedConnection,
        Exception? primaryException)
    {
        List<Exception>? cleanupExceptions = null;
        try
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, exception); }
        try { Dispose(command); }
        catch (Exception exception) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, exception); }
        try { await command.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, exception); }
        try
        {
            if (ownedConnection is not null) await ownedConnection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, exception); }

        if (primaryException is not null) InquiryCleanup.ThrowIfCleanupFailed(primaryException, cleanupExceptions);
        else InquiryCleanup.ThrowIfAny(cleanupExceptions);
    }

    /// <summary>
    /// Allocation-free mutable execution state. Callers keep this as one ordinary local and invoke
    /// <see cref="DisposeAsync"/> explicitly from finally; it must never be used through a using
    /// declaration or copied after mutation, because either would lose captured reader/exception state.
    /// </summary>
    internal struct CommandResourceScope
    {
        private readonly DbCommand _command;
        private readonly DbConnection? _ownedConnection;
        private DbDataReader? _reader;
        private Exception? _primaryException;

        internal CommandResourceScope(DbCommand command, DbConnection? ownedConnection)
        {
            _command = command;
            _ownedConnection = ownedConnection;
            _reader = null;
            _primaryException = null;
        }

        internal void OwnReader(DbDataReader reader)
            => _reader = reader ?? throw new ArgumentNullException(nameof(reader));

        internal void Capture(Exception primaryException)
            => _primaryException ??= primaryException ?? throw new ArgumentNullException(nameof(primaryException));

        internal ValueTask DisposeAsync()
            => DisposeExecutionAsync(_command, _reader, _ownedConnection, _primaryException);
    }

    private sealed class ResourceSet : IDisposable
    {
        private readonly object _gate = new();
        private List<IInquiryExecutionResource>? _items = new();

        public void Add(IInquiryExecutionResource resource)
        {
            lock (_gate)
            {
                if (_items is null) throw new ObjectDisposedException(nameof(ResourceSet));
                _items.Add(resource);
            }
        }

        public void Remove(IInquiryExecutionResource resource)
        {
            lock (_gate) _items?.Remove(resource);
        }

        public void Dispose()
        {
            List<IInquiryExecutionResource>? items;
            lock (_gate)
            {
                items = _items;
                _items = null;
            }
            if (items is null) return;
            List<Exception>? exceptions = null;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                try
                {
                    items[i].Dispose();
                }
                catch (Exception exception)
                {
                    exceptions = InquiryCleanup.Add(exceptions, exception);
                }
            }
            InquiryCleanup.ThrowIfAny(exceptions);
        }
    }
}

internal interface IInquiryExecutionResource : IDisposable;
