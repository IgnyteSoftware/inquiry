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

    // Single-row reads deliberately omit CommandBehavior.SingleRow. The QuerySingleOrDefaultAsync
    // contract throws if the query returns more than one row, and that detection requires a second
    // ReadAsync call to observe the extra row. SingleRow gives providers permission to stop after the
    // first row, silently suppressing the detection on providers that honour the hint (audit P2 #5).
    private const CommandBehavior SingleRowBehavior = CommandBehavior.SingleResult;

    // The struct-materializer (generated-store) overloads add SequentialAccess so the provider streams
    // each row forward-only instead of buffering it — generated materializers read every column once in
    // ascending ordinal order, so this is safe and roughly halves large-result allocation. The
    // class-materializer overloads keep the buffered behaviours, since a caller-supplied materializer may
    // read columns out of order, which SequentialAccess forbids.
    private const CommandBehavior SequentialReadBehavior = CommandBehavior.SingleResult | CommandBehavior.SequentialAccess;
    private const CommandBehavior SequentialSingleRowBehavior = CommandBehavior.SingleResult | CommandBehavior.SequentialAccess;

    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly IInquiryCommandInterceptor[] _interceptors;
    private readonly IInquiryConnectionFactory _connectionFactory;
    private readonly bool _prepareEnabled;

    // Whole seconds from InquiryOptions.DefaultCommandTimeout; 0 = not configured (provider default).
    private readonly int _defaultCommandTimeoutSeconds;
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
        _prepareEnabled = (options?.PrepareStatements ?? PreparedStatementMode.Auto) == PreparedStatementMode.Auto
            && _connectionFactory.SupportsPersistentPreparedStatements;
        _defaultCommandTimeoutSeconds = options?.DefaultCommandTimeout is { } timeout
            ? (int)Math.Ceiling(timeout.TotalSeconds)
            : 0;
    }

    /// <summary>
    /// The underlying database transaction. Internal-only: consumed by
    /// <see cref="Inquiry.Transactions.SavepointInquiryTransaction"/> for savepoint creation and
    /// for its <c>IInquiryTransaction.Transaction</c> interop surface; regular query/execute
    /// paths go through the pipeline methods that enlist commands automatically.
    /// </summary>
    internal DbTransaction Transaction => _transaction;

    /// <summary>
    /// The open connection this pipeline executes on. Internal-only: consumed by
    /// <see cref="Inquiry.Transactions.SavepointInquiryTransaction"/> for its
    /// <c>IInquiryTransaction.Connection</c> interop surface.
    /// </summary>
    internal DbConnection Connection => _connection;

    /// <summary>
    /// True once the owning transaction has been committed, rolled back, or disposed (set by
    /// <see cref="Inquiry.Transactions.InquiryTransaction"/> when it closes). Savepoint handles
    /// consult this so their members — including the Connection/Transaction interop surface —
    /// fail fast after the outer transaction is gone instead of handing out a disposed pair.
    /// </summary>
    internal bool IsClosed => _isClosed;

    internal void MarkClosed() => _isClosed = true;

    private volatile bool _isClosed;

    internal InFlightLease EnterExclusiveOperation()
    {
        EnterInFlight();
        return new InFlightLease(this);
    }

    internal readonly struct InFlightLease : IDisposable
    {
        private readonly TransactedInquiryRequestPipeline _pipeline;

        internal InFlightLease(TransactedInquiryRequestPipeline pipeline)
            => _pipeline = pipeline;

        public void Dispose() => _pipeline.ExitInFlight();
    }

    // ---- Savepoint primitives ------------------------------------------------------------
    //
    // Wrap DbTransaction.SaveAsync / RollbackAsync(name) / ReleaseAsync(name) with the same
    // _inFlight guard as data operations: a savepoint is a SQL statement on the connection, so it
    // would corrupt an in-flight reader / writer if two ops touched the connection at once. The
    // try/finally ensures _inFlight is always released even if the provider throws.

    internal async Task SaveSavepointAsync(string savepointName, CancellationToken cancellationToken)
    {
        EnterInFlight();
        try
        {
            await _transaction.SaveAsync(savepointName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitInFlight();
        }
    }

    internal async Task ReleaseSavepointAsync(string savepointName, CancellationToken cancellationToken)
    {
        EnterInFlight();
        try
        {
            await _transaction.ReleaseAsync(savepointName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitInFlight();
        }
    }

    internal async Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken)
    {
        EnterInFlight();
        try
        {
            await _transaction.RollbackAsync(savepointName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitInFlight();
        }
    }

    private bool HasInterceptors => _interceptors.Length > 0;

    /// <summary>
    /// Creates a transaction-enlisted command and runs the factory's
    /// <see cref="IInquiryConnectionFactory.InitializeCommand"/> hook.
    /// </summary>
    private DbCommand CreateCommand()
    {
        var dbCommand = _connection.CreateCommand();
        // Applied here (the chokepoint every path passes through) so the TArgs fast paths get it
        // too; an explicit InquiryCommand.CommandTimeout overrides it in InitializeCommandSync.
        if (_defaultCommandTimeoutSeconds > 0) dbCommand.CommandTimeout = _defaultCommandTimeoutSeconds;
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

    /// <inheritdoc />
    public async Task<InquiryGridReader> QueryMultipleAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // Hold the in-flight lease for the grid reader's whole lifetime: it owns the shared connection
        // across multiple reads, so no other op may touch the connection until it is disposed.
        var lease = EnterExclusiveOperation();
        DbCommand? dbCommand = null;
        DbDataReader? reader = null;
        try
        {
            dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;
            InitializeCommandSync(dbCommand, command);
            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            // Shared connection — the grid does NOT own/dispose it; it releases the lease on dispose.
            return new InquiryGridReader(reader, dbCommand, ownedConnection: null, lease: lease);
        }
        catch
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
            if (dbCommand is not null) await dbCommand.DisposeAsync().ConfigureAwait(false);
            lease.Dispose();
            throw;
        }
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
        // Create the command INSIDE the try so a throw from CreateCommand()/InitializeCommand still runs
        // the finally and releases the in-flight slot — otherwise the slot leaks and the transaction
        // becomes permanently un-committable. A command that was created is disposed in the finally;
        // matches the buffered overloads.
        DbCommand? dbCommand = null;
        try
        {
            dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                try
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
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

            if (HasInterceptors)
            {
                try
                {
                    await InvokeExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
            if (dbCommand is not null) await dbCommand.DisposeAsync().ConfigureAwait(false);
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
        // Create the command INSIDE the try so a throw from CreateCommand()/InitializeCommand still runs
        // the finally and releases the in-flight slot — otherwise the slot leaks and the transaction
        // becomes permanently un-committable. A command that was created is disposed in the finally;
        // matches the buffered overloads.
        DbCommand? dbCommand = null;
        try
        {
            dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                try
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
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

            if (HasInterceptors)
            {
                try
                {
                    await InvokeExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
            if (dbCommand is not null) await dbCommand.DisposeAsync().ConfigureAwait(false);
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> QueryListAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default,
        int capacityHint = -1)
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
                await using var reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
                var list = capacityHint > 0 ? new List<T>(capacityHint) : new List<T>();
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
                await using var reader = await dbCommand.ExecuteReaderAsync(SequentialSingleRowBehavior, cancellationToken).ConfigureAwait(false);
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
    public async IAsyncEnumerable<T> QueryAsync<T, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        EnterInFlight();
        DbDataReader? reader = null;
        // Create the command INSIDE the try so a throw from CreateCommand()/InitializeCommand still runs
        // the finally and releases the in-flight slot — otherwise the slot leaks and the transaction
        // becomes permanently un-committable. A command that was created is disposed in the finally;
        // matches the buffered overloads.
        DbCommand? dbCommand = null;
        try
        {
            // interceptorCommand is built inside the try too: new InquiryCommand(commandText) can throw
            // (empty/whitespace SQL), and it runs after EnterInFlight() — keeping it guarded ensures the
            // finally still releases the in-flight slot.
            var interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;
            dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;
            dbCommand.CommandText = commandText;
            bindParameters(dbCommand, args);
            _connectionFactory.FinalizeCommand(dbCommand);
            if (interceptorCommand is not null)
            {
                try
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
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
                    if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
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
                    if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                yield return item;
            }

            if (interceptorCommand is not null)
            {
                try
                {
                    await InvokeExecutedAsync(interceptorCommand, dbCommand, null, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
            if (dbCommand is not null) await dbCommand.DisposeAsync().ConfigureAwait(false);
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
                _connectionFactory.FinalizeCommand(dbCommand);
                if (interceptorCommand is not null)
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                await using var reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
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
                _connectionFactory.FinalizeCommand(dbCommand);
                if (interceptorCommand is not null)
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                await using var reader = await dbCommand.ExecuteReaderAsync(SequentialSingleRowBehavior, cancellationToken).ConfigureAwait(false);
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
                _connectionFactory.FinalizeCommand(dbCommand);

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

    /// <inheritdoc />
    public async Task<T> ExecuteScalarAsync<T>(InquiryCommand command, CancellationToken cancellationToken = default)
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
                var value = await dbCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, null, cancellationToken).ConfigureAwait(false);
                return ScalarConvert.From<T>(value);
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
    public async Task<T> ExecuteProcedureScalarAsync<T>(InquiryCommand command, string readBackParameterName, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (string.IsNullOrWhiteSpace(readBackParameterName)) throw new ArgumentException("Read-back parameter name cannot be empty.", nameof(readBackParameterName));

        // Match the binder's normalization so a caller-supplied "Total" finds the bound "@Total".
        readBackParameterName = InquiryParameterBinder.NormalizeName(readBackParameterName);

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

                var recordsAffected = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                var readBack = ScalarConvert.From<T>(dbCommand.Parameters[readBackParameterName].Value);

                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected, cancellationToken).ConfigureAwait(false);
                return readBack;
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
    public async Task<T> ExecuteScalarAsync<T, TArgs>(
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
                _connectionFactory.FinalizeCommand(dbCommand);

                if (interceptorCommand is not null)
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                var value = await dbCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, null, cancellationToken).ConfigureAwait(false);
                return ScalarConvert.From<T>(value);
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
    /// <remarks>
    /// When the connection factory allows batching and the provider supports
    /// <see cref="DbConnection.CanCreateBatch"/> with parameter creation on
    /// <see cref="DbBatchCommand"/>, all items execute in a single transaction-enlisted
    /// <see cref="DbBatch"/> round trip. Interceptors do NOT fire on the DbBatch path — there is
    /// no <see cref="DbCommand"/> to expose to them. The sequential fallback (one command per
    /// item on the transaction's connection) fires interceptors per command as usual.
    /// </remarks>
    public async Task<int> ExecuteBatchAsync<TItem>(
        string commandText,
        IReadOnlyList<TItem> items,
        Action<InquiryParameterTarget, TItem> bindParameters,
        CancellationToken cancellationToken = default)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));
        if (items.Count == 0) return 0;

        EnterInFlight();
        try
        {
            if (_connectionFactory.SupportsBatchExecution && _connection.CanCreateBatch)
            {
                // Probe: some providers expose DbBatch but not DbBatchCommand.CreateParameter;
                // those fall back to the sequential path below (the probe batch is disposed by
                // await using at the end of this block).
                await using var batch = _connection.CreateBatch();
                var firstCommand = batch.CreateBatchCommand();
                if (firstCommand.CanCreateParameter)
                {
                    batch.Transaction = _transaction;
                    if (_defaultCommandTimeoutSeconds > 0) batch.Timeout = _defaultCommandTimeoutSeconds;
                    firstCommand.CommandText = commandText;
                    bindParameters(new InquiryParameterTarget(firstCommand), items[0]);
                    batch.BatchCommands.Add(firstCommand);
                    for (var i = 1; i < items.Count; i++)
                    {
                        var batchCommand = batch.CreateBatchCommand();
                        batchCommand.CommandText = commandText;
                        bindParameters(new InquiryParameterTarget(batchCommand), items[i]);
                        batch.BatchCommands.Add(batchCommand);
                    }

                    // DbBatch.ExecuteNonQueryAsync returns the summed rows affected across commands.
                    return await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            // Sequential fallback: one command per item on the transaction's connection
            // (mirrors ExecuteAsync<TArgs>).
            var total = 0;
            for (var i = 0; i < items.Count; i++)
            {
                await using var dbCommand = CreateCommand();
                dbCommand.Transaction = _transaction;
                var interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;

                try
                {
                    dbCommand.CommandText = commandText;
                    bindParameters(new InquiryParameterTarget(dbCommand), items[i]);
                    _connectionFactory.FinalizeCommand(dbCommand);

                    if (interceptorCommand is not null)
                    {
                        await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                        await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                    }

                    await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                    var recordsAffected = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected, cancellationToken).ConfigureAwait(false);
                    total += recordsAffected;
                }
                catch (Exception exception)
                {
                    if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            return total;
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

    private void InitializeCommandSync(DbCommand dbCommand, InquiryCommand command)
    {
        dbCommand.CommandText = command.CommandText;
        if (command.CommandType is not null) dbCommand.CommandType = command.CommandType.Value;
        if (command.CommandTimeout is not null) dbCommand.CommandTimeout = command.CommandTimeout.Value;
        InquiryParameterBinder.Bind(dbCommand, command.ParametersArray);
        command.DbCommandBinder?.Invoke(dbCommand);
        _connectionFactory.FinalizeCommand(dbCommand);
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
