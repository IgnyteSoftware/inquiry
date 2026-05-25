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
internal sealed class TransactedInquiryRequestPipeline : IInquiryRequestPipeline
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly IInquiryCommandInterceptor[] _interceptors;

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

        await using var dbCommand = _connection.CreateCommand();
        dbCommand.Transaction = _transaction;
        var completed = false;
        DbDataReader? reader = null;
        try
        {
            await InitializeCommandAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
            await NotifyExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);

            reader = await dbCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return materialize(reader);
            }

            completed = true;
            await NotifyExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (reader is not null)
            {
                await reader.DisposeAsync().ConfigureAwait(false);
            }

            if (!completed)
            {
                try
                {
                    var abandoned = new OperationCanceledException(
                        "Inquiry query enumeration was disposed before completion.", cancellationToken);
                    await NotifyFailedAsync(command, dbCommand, abandoned, cancellationToken).ConfigureAwait(false);
                }
                catch { }
            }
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

        await using var dbCommand = _connection.CreateCommand();
        dbCommand.Transaction = _transaction;

        try
        {
            await InitializeCommandAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
            await NotifyExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);

            await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? materialize(reader)
                : default;

            await NotifyExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            await NotifyFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(InquiryCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

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
