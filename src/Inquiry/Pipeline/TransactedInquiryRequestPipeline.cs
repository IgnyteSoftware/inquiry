using Inquiry.Commands;
using Inquiry.Interceptors;
using Inquiry.Parameters;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Inquiry.Pipeline;

/// <summary>
/// An <see cref="IInquiryRequestPipeline"/> that executes commands on an already-open connection
/// within an active transaction.
/// </summary>
/// <remarks>
/// A single <see cref="DbConnection"/> is not thread-safe, so this pipeline rejects concurrent
/// operations: starting a second op while another is in flight throws
/// <see cref="InvalidOperationException"/> instead of corrupting the connection state.
/// </remarks>
internal sealed class TransactedInquiryRequestPipeline : IInquiryRequestPipeline
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly IInquiryCommandInterceptor[] _interceptors;
    private int _inFlight; // 0 = idle, 1 = busy

    internal TransactedInquiryRequestPipeline(
        DbConnection connection,
        DbTransaction transaction,
        IInquiryCommandInterceptor[] interceptors)
    {
        _connection = connection;
        _transaction = transaction;
        _interceptors = interceptors;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<T> QueryAsync<T>(
        InquiryCommand command,
        Func<DbDataReader, T> materialize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (materialize is null) throw new ArgumentNullException(nameof(materialize));

        EnterInFlight();
        DbDataReader? reader = null;
        var dbCommand = _connection.CreateCommand();
        try
        {
            dbCommand.Transaction = _transaction;
            await InitializeCommandAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
            await NotifyExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);

            try
            {
                reader = await dbCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await NotifyFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }

            while (true)
            {
                bool hasRow;
                try
                {
                    hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await NotifyFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                if (!hasRow)
                {
                    break;
                }

                T item;
                try
                {
                    item = materialize(reader);
                }
                catch (Exception exception)
                {
                    await NotifyFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                yield return item;
            }

            await NotifyExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (reader is not null)
            {
                await reader.DisposeAsync().ConfigureAwait(false);
            }
            await dbCommand.DisposeAsync().ConfigureAwait(false);
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        InquiryCommand command,
        Func<DbDataReader, T> materialize,
        CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (materialize is null) throw new ArgumentNullException(nameof(materialize));

        EnterInFlight();
        try
        {
            await using var dbCommand = _connection.CreateCommand();
            dbCommand.Transaction = _transaction;

            try
            {
                await InitializeCommandAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await NotifyExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);

                await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    await NotifyExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
                    return default;
                }

                var result = materialize(reader);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.");
                }

                await NotifyExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception)
            {
                await NotifyFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(InquiryCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        EnterInFlight();
        try
        {
            await using var dbCommand = _connection.CreateCommand();
            dbCommand.Transaction = _transaction;

            try
            {
                await InitializeCommandAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await NotifyExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);

                var recordsAffected = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                await NotifyExecutedAsync(command, dbCommand, recordsAffected, cancellationToken).ConfigureAwait(false);
                return recordsAffected;
            }
            catch (Exception exception)
            {
                await NotifyFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    private void EnterInFlight()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "Cannot start a new Inquiry operation while another operation is in flight on the same transaction. " +
                "DbConnection is not thread-safe; serialize operations within a single transaction (no Task.WhenAll, no concurrent foreach).");
        }
    }

    private void ExitInFlight() => System.Threading.Interlocked.Exchange(ref _inFlight, 0);

    private async ValueTask InitializeCommandAsync(DbCommand dbCommand, InquiryCommand command, CancellationToken ct)
    {
        dbCommand.CommandText = command.CommandText;
        if (command.CommandType is not null) dbCommand.CommandType = command.CommandType.Value;
        if (command.CommandTimeout is not null) dbCommand.CommandTimeout = command.CommandTimeout.Value;
        InquiryParameterBinder.Bind(dbCommand, command.Parameters);
        var context = new InquiryCommandContext(command, dbCommand);
        foreach (var interceptor in _interceptors)
            await interceptor.CommandInitializedAsync(context, ct).ConfigureAwait(false);
    }

    private async ValueTask NotifyExecutingAsync(InquiryCommand cmd, DbCommand dbCmd, CancellationToken ct)
    {
        var context = new InquiryCommandContext(cmd, dbCmd);
        foreach (var interceptor in _interceptors)
            await interceptor.CommandExecutingAsync(context, ct).ConfigureAwait(false);
    }

    private async ValueTask NotifyExecutedAsync(InquiryCommand cmd, DbCommand dbCmd, int? rows, CancellationToken ct)
    {
        var context = new InquiryCommandExecutedContext(cmd, dbCmd, rows);
        foreach (var interceptor in _interceptors)
            await interceptor.CommandExecutedAsync(context, ct).ConfigureAwait(false);
    }

    private async ValueTask NotifyFailedAsync(InquiryCommand cmd, DbCommand dbCmd, Exception ex, CancellationToken ct)
    {
        var context = new InquiryCommandFailedContext(cmd, dbCmd, ex);
        foreach (var interceptor in _interceptors)
            await interceptor.CommandFailedAsync(context, ct).ConfigureAwait(false);
    }
}
