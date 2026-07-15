using Inquiry.Connections;
using System.Data;
using System.Data.Common;

namespace Inquiry.Commands;

internal static class InquiryBatchCommandExecutor
{
    internal static async Task<int> ExecuteAsync<TItem>(
        DbConnection connection,
        DbTransaction transaction,
        IInquiryConnectionFactory connectionFactory,
        InquiryBatchExecutionMode executionMode,
        int commandTimeoutSeconds,
        bool prepareEnabled,
        bool preferPrepareOnce,
        int maxParametersPerCommand,
        InquiryBatchCommand<TItem> command,
        InquiryBatchChunkReader<TItem> chunks,
        IReadOnlyList<TItem> firstChunk,
        Func<IReadOnlyList<TItem>, CancellationToken, Task<int>>? interceptedRows,
        Func<IReadOnlyList<TItem>, CancellationToken, Task<int>>? interceptedChunk,
        CancellationToken cancellationToken)
    {
        if (interceptedRows is not null)
        {
            if (command.UseChunk is not null)
                return await ExecuteSelectableAsync(connection, transaction, connectionFactory, commandTimeoutSeconds,
                    prepareEnabled, preferPrepareOnce, maxParametersPerCommand, executionMode, command, chunks, firstChunk, interceptedRows, interceptedChunk!, cancellationToken).ConfigureAwait(false);
            return await ExecuteInterceptedAsync(chunks, firstChunk,
                command.BindItem is null ? interceptedChunk! : interceptedRows, cancellationToken).ConfigureAwait(false);
        }

        if (command.UseChunk is not null)
        {
            return await ExecuteSelectableAsync(connection, transaction, connectionFactory, commandTimeoutSeconds,
                prepareEnabled, preferPrepareOnce, maxParametersPerCommand, executionMode, command, chunks, firstChunk, null, null, cancellationToken).ConfigureAwait(false);
        }

        if (command.BindItem is null)
        {
            return await ExecuteChunkBoundAsync(connection, transaction, connectionFactory, commandTimeoutSeconds,
                prepareEnabled, command, chunks, firstChunk, cancellationToken).ConfigureAwait(false);
        }

        switch (executionMode)
        {
            case InquiryBatchExecutionMode.ArrayBinding:
                if (command.BindChunk is null)
                {
                    return await ExecuteReusedAsync(connection, transaction, connectionFactory, commandTimeoutSeconds,
                        prepareEnabled || preferPrepareOnce, command, chunks, firstChunk, cancellationToken).ConfigureAwait(false);
                }

                return await ExecuteChunkBoundAsync(connection, transaction, connectionFactory, commandTimeoutSeconds,
                    prepareEnabled, command, chunks, firstChunk, cancellationToken).ConfigureAwait(false);

            case InquiryBatchExecutionMode.ReusedCommand:
                return await ExecuteReusedAsync(connection, transaction, connectionFactory, commandTimeoutSeconds,
                    prepareEnabled || preferPrepareOnce, command, chunks, firstChunk, cancellationToken).ConfigureAwait(false);

            case InquiryBatchExecutionMode.DbBatch:
                if (connection.CanCreateBatch)
                {
                    var result = await TryExecuteDbBatchAsync(connection, transaction, commandTimeoutSeconds,
                        prepareEnabled, command, chunks, firstChunk, cancellationToken).ConfigureAwait(false);
                    if (result.Supported) return result.Total;
                }

                return await ExecuteReusedAsync(connection, transaction, connectionFactory, commandTimeoutSeconds,
                    prepareEnabled || preferPrepareOnce, command, chunks, firstChunk, cancellationToken).ConfigureAwait(false);

            default:
                throw new InvalidOperationException($"Unsupported batch execution mode: {executionMode}.");
        }
    }

    private static async Task<int> ExecuteSelectableAsync<TItem>(
        DbConnection connection,
        DbTransaction transaction,
        IInquiryConnectionFactory connectionFactory,
        int commandTimeoutSeconds,
        bool prepareEnabled,
        bool preferPrepareOnce,
        int maxParametersPerCommand,
        InquiryBatchExecutionMode executionMode,
        InquiryBatchCommand<TItem> command,
        InquiryBatchChunkReader<TItem> chunks,
        IReadOnlyList<TItem> firstChunk,
        Func<IReadOnlyList<TItem>, CancellationToken, Task<int>>? interceptedRows,
        Func<IReadOnlyList<TItem>, CancellationToken, Task<int>>? interceptedChunk,
        CancellationToken cancellationToken)
    {
        var total = 0;
        var chunk = firstChunk;
        do
        {
            if (command.ShouldUseChunk(chunk, maxParametersPerCommand))
            {
                if (interceptedChunk is not null)
                {
                    total += await interceptedChunk(chunk, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                total += await ExecuteChunkBoundAsync(connection, transaction, connectionFactory, commandTimeoutSeconds,
                    prepareEnabled, command, chunks, chunk, cancellationToken, singleChunk: true).ConfigureAwait(false);
                continue;
            }

            if (interceptedRows is not null)
            {
                total += await interceptedRows(chunk, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (executionMode == InquiryBatchExecutionMode.DbBatch && connection.CanCreateBatch)
            {
                var result = await TryExecuteDbBatchAsync(connection, transaction, commandTimeoutSeconds,
                    prepareEnabled, command, chunks, chunk, cancellationToken, singleChunk: true).ConfigureAwait(false);
                if (result.Supported)
                {
                    total += result.Total;
                    continue;
                }
            }

            total += await ExecuteReusedAsync(connection, transaction, connectionFactory, commandTimeoutSeconds,
                prepareEnabled || preferPrepareOnce, command, chunks, chunk, cancellationToken, singleChunk: true).ConfigureAwait(false);
        }
        while (chunks.MoveNext(out chunk));

        return total;
    }

    private static async Task<int> ExecuteInterceptedAsync<TItem>(
        InquiryBatchChunkReader<TItem> chunks,
        IReadOnlyList<TItem> firstChunk,
        Func<IReadOnlyList<TItem>, CancellationToken, Task<int>> executeChunk,
        CancellationToken cancellationToken)
    {
        var total = 0;
        var chunk = firstChunk;
        do
        {
            total += await executeChunk(chunk, cancellationToken).ConfigureAwait(false);
        }
        while (chunks.MoveNext(out chunk));

        return total;
    }

    private static async Task<int> ExecuteReusedAsync<TItem>(
        DbConnection connection,
        DbTransaction transaction,
        IInquiryConnectionFactory connectionFactory,
        int commandTimeoutSeconds,
        bool prepareEnabled,
        InquiryBatchCommand<TItem> command,
        InquiryBatchChunkReader<TItem> chunks,
        IReadOnlyList<TItem> firstChunk,
        CancellationToken cancellationToken,
        bool singleChunk = false)
    {
        DbCommand? dbCommand = null;
        InquiryCommandResources.CommandResourceScope commandResources = default;
        try
        {
            dbCommand = CreateCommand(connection, transaction, connectionFactory, commandTimeoutSeconds, command);
            commandResources = InquiryCommandResources.CreateScope(dbCommand);
            command.BindItem!(new InquiryParameterTarget(dbCommand), firstChunk[0]);
            connectionFactory.FinalizeCommand(dbCommand);
            await MaybePrepareAsync(dbCommand, prepareEnabled, cancellationToken).ConfigureAwait(false);

            var total = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var reuseState = new InquiryParameterReuseState(dbCommand);
            var reuseTarget = new InquiryParameterTarget(reuseState);
            var chunk = firstChunk;
            var index = 1;
            while (true)
            {
                for (; index < chunk.Count; index++)
                {
                    reuseState.BeginItem();
                    command.BindItem!(reuseTarget, chunk[index]);
                    reuseState.CompleteItem();
                    connectionFactory.FinalizeCommand(dbCommand);
                    total += await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (singleChunk || !chunks.MoveNext(out chunk)) return total;
                index = 0;
            }
        }
        catch (Exception exception)
        {
            commandResources.Capture(exception);
            throw;
        }
        finally
        {
            if (dbCommand is not null)
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<int> ExecuteChunkBoundAsync<TItem>(
        DbConnection connection,
        DbTransaction transaction,
        IInquiryConnectionFactory connectionFactory,
        int commandTimeoutSeconds,
        bool prepareEnabled,
        InquiryBatchCommand<TItem> command,
        InquiryBatchChunkReader<TItem> chunks,
        IReadOnlyList<TItem> firstChunk,
        CancellationToken cancellationToken,
        bool singleChunk = false)
    {
        var total = 0;
        var chunk = firstChunk;
        do
        {
            var dbCommand = CreateCommand(connection, transaction, connectionFactory, commandTimeoutSeconds, command,
                command.GetChunkCommandText(chunk.Count));
            var resources = InquiryCommandResources.CreateScope(dbCommand);
            try
            {
                connectionFactory.InitializeBatchChunkCommand(dbCommand, chunk.Count);
                command.BindChunk!(dbCommand, chunk);
                connectionFactory.FinalizeCommand(dbCommand);
                await MaybePrepareAsync(dbCommand, prepareEnabled, cancellationToken).ConfigureAwait(false);
                total += await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                resources.Capture(exception);
                throw;
            }
            finally
            {
                await resources.DisposeAsync().ConfigureAwait(false);
            }
        }
        while (!singleChunk && chunks.MoveNext(out chunk));

        return total;
    }

    private static async Task<DbBatchResult> TryExecuteDbBatchAsync<TItem>(
        DbConnection connection,
        DbTransaction transaction,
        int commandTimeoutSeconds,
        bool prepareEnabled,
        InquiryBatchCommand<TItem> command,
        InquiryBatchChunkReader<TItem> chunks,
        IReadOnlyList<TItem> firstChunk,
        CancellationToken cancellationToken,
        bool singleChunk = false)
    {
        var total = 0;
        var chunk = firstChunk;
        var firstBatch = true;
        do
        {
            DbBatch? batch = null;
            Exception? primaryException = null;
            try
            {
                batch = connection.CreateBatch();
                batch.Transaction = transaction;
                if (commandTimeoutSeconds > 0) batch.Timeout = commandTimeoutSeconds;

                for (var i = 0; i < chunk.Count; i++)
                {
                    var batchCommand = batch.CreateBatchCommand();
                    if (firstBatch && i == 0 && !batchCommand.CanCreateParameter) return default;
                    batchCommand.CommandText = command.CommandText!;
                    batchCommand.CommandType = command.CommandType;
                    command.BindItem!(new InquiryParameterTarget(batchCommand), chunk[i]);
                    batch.BatchCommands.Add(batchCommand);
                }

                if (prepareEnabled && command.CommandType != CommandType.StoredProcedure)
                {
                    await batch.PrepareAsync(cancellationToken).ConfigureAwait(false);
                }

                total += await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                firstBatch = false;
            }
            catch (Exception exception)
            {
                primaryException = exception;
                throw;
            }
            finally
            {
                if (batch is not null)
                {
                    try { await batch.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception cleanupException) when (primaryException is not null)
                    {
                        InquiryCleanup.ThrowIfCleanupFailed(primaryException, new List<Exception> { cleanupException });
                    }
                }
            }
        }
        while (!singleChunk && chunks.MoveNext(out chunk));

        return new DbBatchResult(true, total);
    }

    private static DbCommand CreateCommand<TItem>(
        DbConnection connection,
        DbTransaction transaction,
        IInquiryConnectionFactory connectionFactory,
        int commandTimeoutSeconds,
        InquiryBatchCommand<TItem> command,
        string? commandText = null)
    {
        var dbCommand = connection.CreateCommand();
        try
        {
            if (commandTimeoutSeconds > 0) dbCommand.CommandTimeout = commandTimeoutSeconds;
            connectionFactory.InitializeCommand(dbCommand);
            dbCommand.Transaction = transaction;
            dbCommand.CommandText = commandText ?? command.CommandText!;
            dbCommand.CommandType = command.CommandType;
            return dbCommand;
        }
        catch (Exception primaryException)
        {
            try { dbCommand.Dispose(); }
            catch (Exception cleanupException)
            {
                InquiryCleanup.ThrowIfCleanupFailed(primaryException, new List<Exception> { cleanupException });
            }

            throw;
        }
    }

    private static ValueTask MaybePrepareAsync(DbCommand command, bool prepareEnabled, CancellationToken cancellationToken)
        => prepareEnabled && command.CommandType != CommandType.StoredProcedure
            ? new ValueTask(command.PrepareAsync(cancellationToken))
            : default;

    private readonly record struct DbBatchResult(bool Supported, int Total);
}
