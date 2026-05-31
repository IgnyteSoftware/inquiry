using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Parameters;
using System.Data;
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
    private const CommandBehavior ReadBehavior = CommandBehavior.SingleResult;
    private const CommandBehavior SingleRowBehavior = CommandBehavior.SingleResult | CommandBehavior.SingleRow;

    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly IInquiryCommandInterceptor[] _interceptors;
    private readonly IInquiryConnectionFactory _connectionFactory;
    private readonly bool _prepareEnabled;
    private int _inFlight; // 0 = idle, 1 = busy

    internal TransactedInquiryRequestPipeline(
        DbConnection connection,
        DbTransaction transaction,
        IInquiryCommandInterceptor[] interceptors,
        IInquiryConnectionFactory connectionFactory,
        InquiryOptions? options)
    {
        _connection = connection;
        _transaction = transaction;
        _interceptors = interceptors;
        _connectionFactory = connectionFactory;
        _prepareEnabled = (options?.PrepareStatements ?? PreparedStatementMode.None) == PreparedStatementMode.Auto
            && _connectionFactory.SupportsPersistentPreparedStatements;
    }

    private bool HasInterceptors => _interceptors.Length > 0;

    /// <summary>
    /// Creates a transaction-enlisted command and runs the factory's
    /// <see cref="IInquiryConnectionFactory.InitializeCommand"/> hook (F4).
    /// </summary>
    private DbCommand CreateCommand()
    {
        var dbCommand = _connection.CreateCommand();
        _connectionFactory.InitializeCommand(dbCommand);
        return dbCommand;
    }

    // Prepares the command when enabled and it is not a stored procedure. In a transaction the same
    // physical connection is reused across operations, so preparation is especially valuable here.
    private ValueTask MaybePrepareAsync(DbCommand dbCommand, CancellationToken cancellationToken)
    {
        if (_prepareEnabled && dbCommand.CommandType != CommandType.StoredProcedure)
        {
            return new ValueTask(dbCommand.PrepareAsync(cancellationToken));
        }

        return default;
    }

    // ---- Class-materializer overloads (ad-hoc IInquiry path) -----------------------------

    /// <inheritdoc />
    public async IAsyncEnumerable<T> QueryAsync<T>(
        InquiryCommand command,
        IInquiryEntityMaterializer<T> materializer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (materializer is null) throw new ArgumentNullException(nameof(materializer));

        EnterInFlight();
        DbDataReader? reader = null;
        var dbCommand = CreateCommand();
        try
        {
            dbCommand.Transaction = _transaction;
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
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
                    if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                if (!hasRow) break;

                T item;
                try
                {
                    item = materializer.Materialize(reader);
                }
                catch (Exception exception)
                {
                    if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                yield return item;
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
            await dbCommand.DisposeAsync().ConfigureAwait(false);
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> QueryListAsync<T>(
        InquiryCommand command,
        IInquiryEntityMaterializer<T> materializer,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (materializer is null) throw new ArgumentNullException(nameof(materializer));

        EnterInFlight();
        try
        {
            await using var dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;

            try
            {
                InitializeCommandSync(dbCommand, command);
                if (HasInterceptors)
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                await using var reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
                var list = new List<T>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    list.Add(materializer.Materialize(reader));
                }

                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
                return list;
            }
            catch (Exception exception)
            {
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        InquiryCommand command,
        IInquiryEntityMaterializer<T> materializer,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (materializer is null) throw new ArgumentNullException(nameof(materializer));

        EnterInFlight();
        try
        {
            await using var dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;

            try
            {
                InitializeCommandSync(dbCommand, command);
                if (HasInterceptors)
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                await using var reader = await dbCommand.ExecuteReaderAsync(SingleRowBehavior, cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
                    return default;
                }

                var result = materializer.Materialize(reader);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.");
                }

                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception)
            {
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    // ---- Struct-materializer overloads (generated-store path) -----------------------------

    /// <inheritdoc />
    public async IAsyncEnumerable<T> QueryAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        EnterInFlight();
        DbDataReader? reader = null;
        var dbCommand = CreateCommand();
        try
        {
            dbCommand.Transaction = _transaction;
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
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
                    if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                if (!hasRow) break;

                T item;
                try
                {
                    item = materializer.Materialize(reader);
                }
                catch (Exception exception)
                {
                    if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                yield return item;
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
            await dbCommand.DisposeAsync().ConfigureAwait(false);
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> QueryListAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        EnterInFlight();
        try
        {
            await using var dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;

            try
            {
                InitializeCommandSync(dbCommand, command);
                if (HasInterceptors)
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                await using var reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
                var list = new List<T>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    list.Add(materializer.Materialize(reader));
                }

                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
                return list;
            }
            catch (Exception exception)
            {
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        EnterInFlight();
        try
        {
            await using var dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;

            try
            {
                InitializeCommandSync(dbCommand, command);
                if (HasInterceptors)
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                await using var reader = await dbCommand.ExecuteReaderAsync(SingleRowBehavior, cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
                    return default;
                }

                var result = materializer.Materialize(reader);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.");
                }

                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception)
            {
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> QueryListAsync<T, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        EnterInFlight();
        try
        {
            await using var dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;
            var interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;

            try
            {
                dbCommand.CommandText = commandText;
                bindParameters(dbCommand, args);
                if (interceptorCommand is not null)
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                await using var reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
                var list = new List<T>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    list.Add(materializer.Materialize(reader));
                }

                if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, null, cancellationToken).ConfigureAwait(false);
                return list;
            }
            catch (Exception exception)
            {
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        EnterInFlight();
        try
        {
            await using var dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;
            var interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;

            try
            {
                dbCommand.CommandText = commandText;
                bindParameters(dbCommand, args);
                if (interceptorCommand is not null)
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                await using var reader = await dbCommand.ExecuteReaderAsync(SingleRowBehavior, cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, null, cancellationToken).ConfigureAwait(false);
                    return default;
                }

                var result = materializer.Materialize(reader);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.");
                }

                if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, null, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception)
            {
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
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
            await using var dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;

            try
            {
                InitializeCommandSync(dbCommand, command);
                if (HasInterceptors)
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                var recordsAffected = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected, cancellationToken).ConfigureAwait(false);
                return recordsAffected;
            }
            catch (Exception exception)
            {
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync<TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        EnterInFlight();
        try
        {
            await using var dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;
            var interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;

            try
            {
                dbCommand.CommandText = commandText;
                bindParameters(dbCommand, args);

                if (interceptorCommand is not null)
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                var recordsAffected = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected, cancellationToken).ConfigureAwait(false);
                return recordsAffected;
            }
            catch (Exception exception)
            {
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    // ---- Shared helpers --------------------------------------------------------------

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

    private static void InitializeCommandSync(DbCommand dbCommand, InquiryCommand command)
    {
        dbCommand.CommandText = command.CommandText;
        if (command.CommandType is not null) dbCommand.CommandType = command.CommandType.Value;
        if (command.CommandTimeout is not null) dbCommand.CommandTimeout = command.CommandTimeout.Value;
        InquiryParameterBinder.Bind(dbCommand, command.ParametersArray);
        command.DbCommandBinder?.Invoke(dbCommand);
    }

    private async ValueTask InvokeInitializedAsync(DbCommand dbCommand, InquiryCommand command, CancellationToken ct)
    {
        var context = new InquiryCommandContext(command, dbCommand);
        foreach (var interceptor in _interceptors)
            await interceptor.CommandInitializedAsync(context, ct).ConfigureAwait(false);
    }

    private async ValueTask InvokeExecutingAsync(InquiryCommand cmd, DbCommand dbCmd, CancellationToken ct)
    {
        var context = new InquiryCommandContext(cmd, dbCmd);
        foreach (var interceptor in _interceptors)
            await interceptor.CommandExecutingAsync(context, ct).ConfigureAwait(false);
    }

    private async ValueTask InvokeExecutedAsync(InquiryCommand cmd, DbCommand dbCmd, int? rows, CancellationToken ct)
    {
        var context = new InquiryCommandExecutedContext(cmd, dbCmd, rows);
        foreach (var interceptor in _interceptors)
            await interceptor.CommandExecutedAsync(context, ct).ConfigureAwait(false);
    }

    private async ValueTask InvokeFailedAsync(InquiryCommand cmd, DbCommand dbCmd, Exception ex, CancellationToken ct)
    {
        var context = new InquiryCommandFailedContext(cmd, dbCmd, ex);
        foreach (var interceptor in _interceptors)
            await interceptor.CommandFailedAsync(context, ct).ConfigureAwait(false);
    }
}
