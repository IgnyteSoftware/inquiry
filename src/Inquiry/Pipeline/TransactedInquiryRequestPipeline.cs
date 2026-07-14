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

    // Struct-materializer overloads always add SequentialAccess. Class-materializer overloads add it only
    // when the materializer declares forward-only ordinal safety; arbitrary custom materializers default to
    // buffered behavior because they may read columns out of order.
    private const CommandBehavior SequentialReadBehavior = CommandBehavior.SingleResult | CommandBehavior.SequentialAccess;
    private const CommandBehavior SequentialSingleRowBehavior = CommandBehavior.SingleResult | CommandBehavior.SequentialAccess;

    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly IInquiryCommandInterceptor[] _interceptors;
    private readonly IInquiryConnectionFactory _connectionFactory;
    private readonly bool _prepareEnabled;
    private readonly bool _autoPrepareConfigured;

    // Whole seconds from InquiryOptions.DefaultCommandTimeout; 0 = not configured (provider default).
    private readonly int _defaultCommandTimeoutSeconds;
    private readonly int _maxBatchSize;
    private readonly int _maxParametersPerCommand;
    // One atomic lifecycle: 0=open/idle, 1=open/busy, 2=terminal owner, 3=closed.
    private int _state;

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
        _autoPrepareConfigured = (options?.PrepareStatements ?? PreparedStatementMode.Auto) == PreparedStatementMode.Auto;
        _prepareEnabled = _autoPrepareConfigured
            && _connectionFactory.SupportsPersistentPreparedStatements;
        _defaultCommandTimeoutSeconds = options?.DefaultCommandTimeout is { } timeout
            ? (int)Math.Ceiling(timeout.TotalSeconds)
            : 0;
        _maxBatchSize = options?.MaxBatchSize ?? InquiryOptions.DefaultMaxBatchSize;
        _maxParametersPerCommand = options?.MaxParametersPerCommand ?? InquiryOptions.DefaultMaxParametersPerCommand;
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
    /// True once a terminal operation owns the transaction, including while provider commit,
    /// rollback, or disposal is still completing. Savepoint handles
    /// consult this so their members — including the Connection/Transaction interop surface —
    /// fail fast after the outer transaction is gone instead of handing out a disposed pair.
    /// </summary>
    internal bool IsClosed => System.Threading.Volatile.Read(ref _state) >= 2;

    internal void MarkClosed() => System.Threading.Interlocked.Exchange(ref _state, 3);

    internal InFlightLease EnterExclusiveOperation()
    {
        EnterInFlight();
        return new InFlightLease(this);
    }

    internal InFlightLease EnterTerminalOperation()
    {
        var observed = System.Threading.Interlocked.CompareExchange(ref _state, 2, 0);
        if (observed == 0) return new InFlightLease(this, terminal: true);
        ThrowForUnavailableState(observed);
        throw new System.InvalidOperationException();
    }

    internal readonly struct InFlightLease : IDisposable
    {
        private readonly TransactedInquiryRequestPipeline _pipeline;
        private readonly bool _terminal;

        internal InFlightLease(TransactedInquiryRequestPipeline pipeline, bool terminal = false)
        {
            _pipeline = pipeline;
            _terminal = terminal;
        }

        public void Dispose()
        {
            if (_terminal) _pipeline.MarkClosed();
            else _pipeline.ExitInFlight();
        }
    }

    // ---- Savepoint primitives ------------------------------------------------------------
    //
    // Wrap DbTransaction.SaveAsync / RollbackAsync(name) / ReleaseAsync(name) with the same
    // atomic operation gate as data operations: a savepoint is a SQL statement on the connection, so it
    // would corrupt an in-flight reader / writer if two ops touched the connection at once. The
    // try/finally ensures the lease is always released even if the provider throws.

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
        var lease = EnterExclusiveOperation();
        using (lease) await ReleaseSavepointAsync(savepointName, lease, cancellationToken).ConfigureAwait(false);
    }

    internal async Task ReleaseSavepointAsync(string savepointName, InFlightLease lease, CancellationToken cancellationToken)
    {
        await _transaction.ReleaseAsync(savepointName, cancellationToken).ConfigureAwait(false);
    }

    internal async Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken)
    {
        var lease = EnterExclusiveOperation();
        using (lease) await RollbackToSavepointAsync(savepointName, lease, cancellationToken).ConfigureAwait(false);
    }

    internal async Task RollbackToSavepointAsync(string savepointName, InFlightLease lease, CancellationToken cancellationToken)
    {
        await _transaction.RollbackAsync(savepointName, cancellationToken).ConfigureAwait(false);
    }

    private bool HasInterceptors => _interceptors.Length > 0;

    private bool HasActiveInterceptors
    {
        get
        {
            foreach (var interceptor in _interceptors)
            {
                if (interceptor is not IInquiryInterceptorActivation activation || activation.IsActive)
                {
                    return true;
                }
            }

            return false;
        }
    }

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
        catch (Exception primaryException)
        {
            var exceptions = new List<Exception> { primaryException };
            try { await DisposeReaderAndCommandAsync(reader, dbCommand).ConfigureAwait(false); }
            catch (Exception exception) { exceptions.Add(exception); }
            try { lease.Dispose(); }
            catch (Exception exception) { exceptions.Add(exception); }
            InquiryCleanup.ThrowIfAny(exceptions);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<InquiryGridReader> QueryMultipleAsync<TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        CancellationToken cancellationToken = default)
    {
        command.Validate();
        var lease = EnterExclusiveOperation();
        DbCommand? dbCommand = null;
        DbDataReader? reader = null;
        try
        {
            dbCommand = CreateCommand();
            dbCommand.Transaction = _transaction;
            dbCommand.CommandText = command.CommandText;
            dbCommand.CommandType = command.CommandType;
            command.BindParameters(dbCommand, command.Args);
            _connectionFactory.FinalizeCommand(dbCommand);
            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            return new InquiryGridReader(reader, dbCommand, ownedConnection: null, lease: lease);
        }
        catch (Exception primaryException)
        {
            var exceptions = new List<Exception> { primaryException };
            try { await DisposeReaderAndCommandAsync(reader, dbCommand).ConfigureAwait(false); }
            catch (Exception exception) { exceptions.Add(exception); }
            try { lease.Dispose(); }
            catch (Exception exception) { exceptions.Add(exception); }
            InquiryCleanup.ThrowIfAny(exceptions);
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
        Exception? primaryException = null;
        try
        {
            var readBehavior = materializer.IsInquirySequentialAccessSafe ? SequentialReadBehavior : ReadBehavior;
            try
            {
                dbCommand = CreateCommand();
                dbCommand.Transaction = _transaction;
                InitializeCommandSync(dbCommand, command);
            }
            catch (Exception exception) { primaryException ??= exception; throw; }
            if (dbCommand is null) throw new InvalidOperationException("Command creation completed without a command.");
            if (HasInterceptors)
            {
                try
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    primaryException ??= exception;
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                reader = await dbCommand.ExecuteReaderAsync(readBehavior, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                primaryException ??= exception;
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
                    primaryException ??= exception;
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
                    primaryException ??= exception;
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
                    primaryException ??= exception;
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            try { await DisposeReaderAndCommandAsync(reader, dbCommand, primaryException).ConfigureAwait(false); }
            finally { ExitInFlight(); }
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
            var readBehavior = materializer.IsInquirySequentialAccessSafe ? SequentialReadBehavior : ReadBehavior;
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }

            try
            {
                InitializeCommandSync(dbCommand, command);
                if (HasInterceptors)
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                var reader = await dbCommand.ExecuteReaderAsync(readBehavior, cancellationToken).ConfigureAwait(false);
                commandResources.OwnReader(reader);
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
                commandResources.Capture(exception);
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
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
            var readBehavior = materializer.IsInquirySequentialAccessSafe ? SequentialSingleRowBehavior : SingleRowBehavior;
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }

            try
            {
                InitializeCommandSync(dbCommand, command);
                if (HasInterceptors)
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                var reader = await dbCommand.ExecuteReaderAsync(readBehavior, cancellationToken).ConfigureAwait(false);
                commandResources.OwnReader(reader);
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
                commandResources.Capture(exception);
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
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
        Exception? primaryException = null;
        try
        {
            try
            {
                dbCommand = CreateCommand();
                dbCommand.Transaction = _transaction;
                InitializeCommandSync(dbCommand, command);
            }
            catch (Exception exception) { primaryException ??= exception; throw; }
            if (dbCommand is null) throw new InvalidOperationException("Command creation completed without a command.");
            if (HasInterceptors)
            {
                try
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    primaryException ??= exception;
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
                primaryException ??= exception;
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
                    primaryException ??= exception;
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
                    primaryException ??= exception;
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
                    primaryException ??= exception;
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            try { await DisposeReaderAndCommandAsync(reader, dbCommand, primaryException).ConfigureAwait(false); }
            finally { ExitInFlight(); }
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
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }

            try
            {
                InitializeCommandSync(dbCommand, command);
                if (HasInterceptors)
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                var reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
                commandResources.OwnReader(reader);
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
                commandResources.Capture(exception);
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
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
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }

            try
            {
                InitializeCommandSync(dbCommand, command);
                if (HasInterceptors)
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                var reader = await dbCommand.ExecuteReaderAsync(SequentialSingleRowBehavior, cancellationToken).ConfigureAwait(false);
                commandResources.OwnReader(reader);
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
                commandResources.Capture(exception);
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<T> QueryAsync<T, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
        => QueryGeneratedCore<T, TArgs, TMaterializer>(commandText, CommandType.Text, args, bindParameters, materializer, cancellationToken);

    public IAsyncEnumerable<T> QueryAsync<T, TArgs, TMaterializer>(
        InquiryGeneratedCommand<TArgs> command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        command.Validate();
        return QueryGeneratedCore<T, TArgs, TMaterializer>(command.CommandText, command.CommandType, command.Args, command.BindParameters, materializer, cancellationToken);
    }

    private async IAsyncEnumerable<T> QueryGeneratedCore<T, TArgs, TMaterializer>(
        string commandText,
        CommandType commandType,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
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
        Exception? primaryException = null;
        try
        {
            // interceptorCommand is built inside the try too: new InquiryCommand(commandText) can throw
            // (empty/whitespace SQL), and it runs after EnterInFlight() — keeping it guarded ensures the
            // finally still releases the in-flight slot.
            InquiryCommand? interceptorCommand = null;
            try
            {
                interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;
                dbCommand = CreateCommand();
                dbCommand.Transaction = _transaction;
                dbCommand.CommandText = commandText;
                dbCommand.CommandType = commandType;
                bindParameters(dbCommand, args);
                _connectionFactory.FinalizeCommand(dbCommand);
            }
            catch (Exception exception) { primaryException ??= exception; throw; }
            if (dbCommand is null) throw new InvalidOperationException("Command creation completed without a command.");
            if (interceptorCommand is not null)
            {
                try
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    primaryException ??= exception;
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
                primaryException ??= exception;
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
                    primaryException ??= exception;
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
                    primaryException ??= exception;
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
                    primaryException ??= exception;
                    await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            try { await DisposeReaderAndCommandAsync(reader, dbCommand, primaryException).ConfigureAwait(false); }
            finally { ExitInFlight(); }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<T>> QueryListAsync<T, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
        => QueryListGeneratedCore<T, TArgs, TMaterializer>(commandText, CommandType.Text, args, bindParameters, materializer, cancellationToken, -1);

    public Task<IReadOnlyList<T>> QueryListAsync<T, TArgs, TMaterializer>(
        InquiryGeneratedCommand<TArgs> command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default,
        int capacityHint = -1)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        command.Validate();
        return QueryListGeneratedCore<T, TArgs, TMaterializer>(command.CommandText, command.CommandType, command.Args, command.BindParameters, materializer, cancellationToken, capacityHint);
    }

    private async Task<IReadOnlyList<T>> QueryListGeneratedCore<T, TArgs, TMaterializer>(
        string commandText,
        CommandType commandType,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken,
        int capacityHint)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        EnterInFlight();
        try
        {
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }
            var interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;

            try
            {
                dbCommand.CommandText = commandText;
                dbCommand.CommandType = commandType;
                bindParameters(dbCommand, args);
                _connectionFactory.FinalizeCommand(dbCommand);
                if (interceptorCommand is not null)
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                var reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
                commandResources.OwnReader(reader);
                var list = capacityHint >= 0 ? new List<T>(capacityHint) : new List<T>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    list.Add(materializer.Materialize(reader));
                }

                if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, null, cancellationToken).ConfigureAwait(false);
                return list;
            }
            catch (Exception exception)
            {
                commandResources.Capture(exception);
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public Task<T?> QuerySingleOrDefaultAsync<T, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
        => QueryValidatingSingleGeneratedCore<T, TArgs, TMaterializer>(commandText, CommandType.Text, args, bindParameters, materializer, cancellationToken);

    public Task<T?> QuerySingleOrDefaultAsync<T, TArgs, TMaterializer>(
        InquiryGeneratedCommand<TArgs> command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        command.Validate();
        return QueryValidatingSingleGeneratedCore<T, TArgs, TMaterializer>(command.CommandText, command.CommandType, command.Args, command.BindParameters, materializer, cancellationToken);
    }

    public Task<T?> QueryGeneratedSingleOrDefaultAsync<T, TArgs, TMaterializer>(
        InquiryGeneratedCommand<TArgs> command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        command.Validate();
        return QueryKnownSingleGeneratedCore<T, TArgs, TMaterializer>(command.CommandText, command.CommandType, command.Args, command.BindParameters, materializer, cancellationToken);
    }

    private async Task<T?> QueryValidatingSingleGeneratedCore<T, TArgs, TMaterializer>(
        string commandText,
        CommandType commandType,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        EnterInFlight();
        try
        {
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }
            var interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;

            try
            {
                dbCommand.CommandText = commandText;
                dbCommand.CommandType = commandType;
                bindParameters(dbCommand, args);
                _connectionFactory.FinalizeCommand(dbCommand);
                if (interceptorCommand is not null)
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                var reader = await dbCommand.ExecuteReaderAsync(SequentialSingleRowBehavior, cancellationToken).ConfigureAwait(false);
                commandResources.OwnReader(reader);
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
                commandResources.Capture(exception);
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
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
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }

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
                commandResources.Capture(exception);
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync<TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default)
        => ExecuteGeneratedCore(commandText, CommandType.Text, args, bindParameters, cancellationToken);

    public Task<int> ExecuteAsync<TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        CancellationToken cancellationToken = default)
    {
        command.Validate();
        return ExecuteGeneratedCore(command.CommandText, command.CommandType, command.Args, command.BindParameters, cancellationToken);
    }

    private async Task<int> ExecuteGeneratedCore<TArgs>(
        string commandText,
        CommandType commandType,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        EnterInFlight();
        try
        {
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }
            var interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;

            try
            {
                dbCommand.CommandText = commandText;
                dbCommand.CommandType = commandType;
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
                commandResources.Capture(exception);
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
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
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }

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
            catch (OperationCanceledException exception)
                when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
            {
                var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, normalized, cancellationToken).ConfigureAwait(false);
                throw normalized;
            }
            catch (Exception exception)
            {
                commandResources.Capture(exception);
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
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
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }

            try
            {
                InitializeCommandSync(dbCommand, command);
                if (HasInterceptors)
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                var recordsAffected = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                var readBack = ScalarConvert.From<T>(InquiryParameterBinder.FindByLogicalName(dbCommand.Parameters, readBackParameterName).Value);

                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected, cancellationToken).ConfigureAwait(false);
                return readBack;
            }
            catch (Exception exception)
            {
                commandResources.Capture(exception);
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    private async Task<T?> QueryKnownSingleGeneratedCore<T, TArgs, TMaterializer>(
        string commandText,
        CommandType commandType,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        EnterInFlight();
        try
        {
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }
            var interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;

            try
            {
                dbCommand.CommandText = commandText;
                dbCommand.CommandType = commandType;
                bindParameters(dbCommand, args);
                _connectionFactory.FinalizeCommand(dbCommand);
                if (interceptorCommand is not null)
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                var reader = await dbCommand.ExecuteReaderAsync(
                    CommandBehavior.SingleResult | CommandBehavior.SingleRow | CommandBehavior.SequentialAccess,
                    cancellationToken).ConfigureAwait(false);
                commandResources.OwnReader(reader);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, null, cancellationToken).ConfigureAwait(false);
                    return default;
                }

                var result = materializer.Materialize(reader);
                if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, null, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception)
            {
                commandResources.Capture(exception);
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public async Task<T> ExecuteProcedureScalarAsync<T, TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        string readBackParameterName,
        CancellationToken cancellationToken = default)
    {
        command.Validate();
        if (string.IsNullOrWhiteSpace(readBackParameterName)) throw new ArgumentException("Read-back parameter name cannot be empty.", nameof(readBackParameterName));
        readBackParameterName = InquiryParameterBinder.NormalizeName(readBackParameterName);

        EnterInFlight();
        try
        {
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }
            var interceptorCommand = HasActiveInterceptors ? new InquiryCommand(command.CommandText, command.CommandType) : null;

            try
            {
                dbCommand.CommandText = command.CommandText;
                dbCommand.CommandType = command.CommandType;
                command.BindParameters(dbCommand, command.Args);
                _connectionFactory.FinalizeCommand(dbCommand);
                if (interceptorCommand is not null)
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }

                var recordsAffected = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                var readBack = ScalarConvert.From<T>(InquiryParameterBinder.FindByLogicalName(dbCommand.Parameters, readBackParameterName).Value);
                if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected, cancellationToken).ConfigureAwait(false);
                return readBack;
            }
            catch (Exception exception)
            {
                commandResources.Capture(exception);
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    public Task<T> ExecuteScalarAsync<T, TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default)
        => ExecuteScalarGeneratedCore<T, TArgs>(commandText, CommandType.Text, args, bindParameters, cancellationToken);

    public Task<T> ExecuteScalarAsync<T, TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        CancellationToken cancellationToken = default)
    {
        command.Validate();
        return ExecuteScalarGeneratedCore<T, TArgs>(command.CommandText, command.CommandType, command.Args, command.BindParameters, cancellationToken);
    }

    private async Task<T> ExecuteScalarGeneratedCore<T, TArgs>(
        string commandText,
        CommandType commandType,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        EnterInFlight();
        try
        {
            var dbCommand = CreateCommand();
            var commandResources = InquiryCommandResources.CreateScope(dbCommand);
            try { dbCommand.Transaction = _transaction; }
            catch (Exception exception) { commandResources.Capture(exception); throw; }
            var interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;

            try
            {
                dbCommand.CommandText = commandText;
                dbCommand.CommandType = commandType;
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
            catch (OperationCanceledException exception)
                when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
            {
                var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, normalized, cancellationToken).ConfigureAwait(false);
                throw normalized;
            }
            catch (Exception exception)
            {
                commandResources.Capture(exception);
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await commandResources.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            ExitInFlight();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// When available, bounded chunks execute through transaction-enlisted <see cref="DbBatch"/>
    /// instances. Otherwise the pipeline reuses one command and parameter set. The transaction
    /// operation lease is held across the entire batch.
    /// </remarks>
    public Task<int> ExecuteBatchAsync<TItem>(
        string commandText,
        IReadOnlyList<TItem> items,
        Action<InquiryParameterTarget, TItem> bindParameters,
        CancellationToken cancellationToken = default)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));
        return ExecuteBatchAsync(new InquiryBatchCommand<TItem>(commandText, bindParameters), items, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> ExecuteBatchAsync<TItem>(
        InquiryBatchCommand<TItem> command,
        IEnumerable<TItem> items,
        CancellationToken cancellationToken = default)
    {
        command.Validate();
        if (items is null) throw new ArgumentNullException(nameof(items));
        var executionMode = _connectionFactory.BatchExecutionMode;

        using var chunks = new InquiryBatchChunkReader<TItem>(items,
            command.GetEffectiveChunkSize(_maxBatchSize, _maxParametersPerCommand), cancellationToken);
        if (!chunks.MoveNext(out var firstChunk)) return 0;

        EnterInFlight();
        try
        {
            var hasActiveInterceptors = HasActiveInterceptors;
            Func<IReadOnlyList<TItem>, CancellationToken, Task<int>>? interceptedRows = hasActiveInterceptors
                ? ExecuteInterceptedChunkAsync
                : null;
            Func<IReadOnlyList<TItem>, CancellationToken, Task<int>>? interceptedChunk = hasActiveInterceptors
                ? ExecuteInterceptedWholeChunkAsync
                : null;
            var total = await InquiryBatchCommandExecutor.ExecuteAsync(
                _connection, _transaction, _connectionFactory, executionMode, _defaultCommandTimeoutSeconds,
                _prepareEnabled,
                _autoPrepareConfigured && command.PreferPrepareOnce,
                command, chunks, firstChunk, interceptedRows, interceptedChunk, cancellationToken).ConfigureAwait(false);
            chunks.Dispose();
            return total;

            async Task<int> ExecuteInterceptedChunkAsync(IReadOnlyList<TItem> chunk, CancellationToken token)
            {
                var total = 0;
                for (var i = 0; i < chunk.Count; i++)
                {
                    var dbCommand = CreateCommand();
                    var resources = InquiryCommandResources.CreateScope(dbCommand);
                    var interceptorCommand = new InquiryCommand(command.CommandText!, command.CommandType);
                    try
                    {
                        dbCommand.Transaction = _transaction;
                        dbCommand.CommandText = command.CommandText;
                        dbCommand.CommandType = command.CommandType;
                        command.BindItem!(new InquiryParameterTarget(dbCommand), chunk[i]);
                        _connectionFactory.FinalizeCommand(dbCommand);
                        await InvokeInitializedAsync(dbCommand, interceptorCommand, token).ConfigureAwait(false);
                        await InvokeExecutingAsync(interceptorCommand, dbCommand, token).ConfigureAwait(false);
                        await MaybePrepareAsync(dbCommand, token).ConfigureAwait(false);
                        var affected = await dbCommand.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        await InvokeExecutedAsync(interceptorCommand, dbCommand, affected, token).ConfigureAwait(false);
                        total += affected;
                    }
                    catch (Exception exception)
                    {
                        resources.Capture(exception);
                        await InvokeFailedAsync(interceptorCommand, dbCommand, exception, token).ConfigureAwait(false);
                        throw;
                    }
                    finally
                    {
                        await resources.DisposeAsync().ConfigureAwait(false);
                    }
                }

                return total;
            }

            async Task<int> ExecuteInterceptedWholeChunkAsync(IReadOnlyList<TItem> chunk, CancellationToken token)
            {
                var commandText = command.GetChunkCommandText(chunk.Count);
                var dbCommand = CreateCommand();
                var resources = InquiryCommandResources.CreateScope(dbCommand);
                var interceptorCommand = new InquiryCommand(commandText, command.CommandType);
                try
                {
                    dbCommand.Transaction = _transaction;
                    dbCommand.CommandText = commandText;
                    dbCommand.CommandType = command.CommandType;
                    _connectionFactory.InitializeBatchChunkCommand(dbCommand, chunk.Count);
                    command.BindChunk!(dbCommand, chunk);
                    _connectionFactory.FinalizeCommand(dbCommand);
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, token).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, token).ConfigureAwait(false);
                    await MaybePrepareAsync(dbCommand, token).ConfigureAwait(false);
                    var affected = await dbCommand.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await InvokeExecutedAsync(interceptorCommand, dbCommand, affected, token).ConfigureAwait(false);
                    return affected;
                }
                catch (Exception exception)
                {
                    resources.Capture(exception);
                    await InvokeFailedAsync(interceptorCommand, dbCommand, exception, token).ConfigureAwait(false);
                    throw;
                }
                finally
                {
                    await resources.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception primaryException)
        {
            try { chunks.Dispose(); }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Inquiry batch execution failed and its source enumerator also failed to dispose.",
                    primaryException,
                    cleanupException);
            }

            throw;
        }
        finally
        {
            ExitInFlight();
        }
    }

    // ---- Shared helpers --------------------------------------------------------------

    private static async ValueTask DisposeReaderAndCommandAsync(
        DbDataReader? reader,
        DbCommand? command,
        Exception? primaryException = null)
    {
        List<Exception>? exceptions = null;
        try
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            exceptions = InquiryCleanup.Add(exceptions, exception);
        }
        if (command is not null)
        {
            try { InquiryCommandResources.Dispose(command); }
            catch (Exception exception) { exceptions = InquiryCleanup.Add(exceptions, exception); }
            try { await command.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { exceptions = InquiryCleanup.Add(exceptions, exception); }
        }
        if (primaryException is not null) InquiryCleanup.ThrowIfCleanupFailed(primaryException, exceptions);
        else InquiryCleanup.ThrowIfAny(exceptions);
    }

    private void EnterInFlight()
    {
        var observed = System.Threading.Interlocked.CompareExchange(ref _state, 1, 0);
        if (observed != 0) ThrowForUnavailableState(observed);
    }

    private void ExitInFlight() => System.Threading.Interlocked.CompareExchange(ref _state, 0, 1);

    private static void ThrowForUnavailableState(int state)
    {
        if (state >= 2)
            throw new ObjectDisposedException(nameof(TransactedInquiryRequestPipeline), "This Inquiry transaction is closing or closed.");

        throw new InvalidOperationException(
            "Cannot start a new Inquiry operation while another operation is in flight on the same transaction. " +
            "DbConnection is not thread-safe; serialize operations within a single transaction (no Task.WhenAll, no concurrent foreach).");
    }

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
        {
            if (interceptor is IInquiryInterceptorActivation { IsActive: false }) continue;
            await interceptor.CommandInitializedAsync(context, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeExecutingAsync(InquiryCommand cmd, DbCommand dbCmd, CancellationToken ct)
    {
        var context = new InquiryCommandContext(cmd, dbCmd);
        foreach (var interceptor in _interceptors)
        {
            if (interceptor is IInquiryInterceptorActivation { IsActive: false }) continue;
            await interceptor.CommandExecutingAsync(context, ct).ConfigureAwait(false);
        }
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
