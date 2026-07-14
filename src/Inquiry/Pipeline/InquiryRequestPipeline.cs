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
/// Default implementation of the Inquiry request pipeline.
/// </summary>
/// <remarks>
/// Each read method exists in two flavours — one taking <see cref="IInquiryEntityMaterializer{T}"/>
/// (class), one taking a struct-constrained generic materializer. The struct variant is what
/// generated stores call; the JIT emits a separate body per concrete struct so the per-row
/// <c>materializer.Materialize(reader)</c> call inlines instead of going through an interface
/// dispatch.
///
/// The class-materializer read methods pass <see cref="CommandBehavior.SingleResult"/> (or
/// <c>SingleResult|SingleRow</c> for the single-or-default path) so the provider can release reader
/// state as soon as the single result set drains. The struct-materializer (generated-store) overloads
/// additionally pass <see cref="CommandBehavior.SequentialAccess"/> — generated materializers read each
/// column exactly once in ascending ordinal order, so the row can be streamed forward-only instead of
/// buffered, roughly halving allocation on large/wide result sets.
///
/// Parameter binding and the three interceptor-notification methods are inlined into each
/// query body. The fast path checks <see cref="HasInterceptors"/> directly; when no interceptors
/// are registered the three <c>InquiryCommandContext</c> allocations and three <c>ValueTask</c>
/// awaits are eliminated entirely (matching Dapper's "nothing between reader and materializer"
/// loop).
/// </remarks>
internal sealed class InquiryRequestPipeline : IInquiryRequestPipeline
{
    private const CommandBehavior ReadBehavior = CommandBehavior.SingleResult;

    // Single-row reads deliberately omit CommandBehavior.SingleRow. The QuerySingleOrDefaultAsync
    // contract throws if the query returns more than one row, and that detection requires a second
    // ReadAsync call to observe the extra row. SingleRow gives providers permission to stop after the
    // first row, silently suppressing the detection on providers that honour the hint (audit P2 #5).
    private const CommandBehavior SingleRowBehavior = CommandBehavior.SingleResult;

    // The struct-materializer (generated-store) overloads add SequentialAccess: generated materializers
    // read every column exactly once in ascending ordinal order, so the provider can stream the row
    // forward-only instead of buffering it — roughly halving allocation on large/wide result sets
    // (matching Dapper). The class-materializer overloads above keep the buffered behaviours, because a
    // caller-supplied IInquiryEntityMaterializer<T> may read columns out of order, which SequentialAccess forbids.
    private const CommandBehavior SequentialReadBehavior = CommandBehavior.SingleResult | CommandBehavior.SequentialAccess;
    private const CommandBehavior SequentialSingleRowBehavior = CommandBehavior.SingleResult | CommandBehavior.SequentialAccess;

    private readonly IInquiryConnectionFactory _connectionFactory;
    private readonly IInquiryCommandInterceptor[] _interceptors;

    // True when Auto preparation is configured AND the provider's prepared state survives the
    // connection lifecycle. The per-command StoredProcedure check is applied at the call site.
    private readonly bool _prepareEnabled;

    // Whole seconds from InquiryOptions.DefaultCommandTimeout; 0 = not configured (provider default).
    private readonly int _defaultCommandTimeoutSeconds;

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryRequestPipeline"/> class.
    /// </summary>
    public InquiryRequestPipeline(
        IInquiryConnectionFactory connectionFactory,
        IEnumerable<IInquiryCommandInterceptor> interceptors)
        : this(connectionFactory, interceptors, options: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryRequestPipeline"/> class with options.
    /// </summary>
    public InquiryRequestPipeline(
        IInquiryConnectionFactory connectionFactory,
        IEnumerable<IInquiryCommandInterceptor> interceptors,
        InquiryOptions? options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _interceptors = interceptors?.ToArray() ?? throw new ArgumentNullException(nameof(interceptors));
        _prepareEnabled = (options?.PrepareStatements ?? PreparedStatementMode.Auto) == PreparedStatementMode.Auto
            && _connectionFactory.SupportsPersistentPreparedStatements;
        _defaultCommandTimeoutSeconds = options?.DefaultCommandTimeout is { } timeout
            ? (int)Math.Ceiling(timeout.TotalSeconds)
            : 0;
    }

    private bool HasInterceptors => _interceptors.Length > 0;

    /// <summary>
    /// Creates a command on <paramref name="connection"/> and runs the factory's
    /// <see cref="IInquiryConnectionFactory.InitializeCommand"/> hook.
    /// </summary>
    private DbCommand CreateCommand(DbConnection connection)
    {
        var dbCommand = connection.CreateCommand();
        // Applied here (the chokepoint every path passes through) so the TArgs fast paths get it
        // too; an explicit InquiryCommand.CommandTimeout overrides it in InitializeCommandSync.
        if (_defaultCommandTimeoutSeconds > 0) dbCommand.CommandTimeout = _defaultCommandTimeoutSeconds;
        _connectionFactory.InitializeCommand(dbCommand);
        return dbCommand;
    }

    private DbCommand CreateCommandOrDisposeConnection(DbConnection connection)
    {
        try
        {
            return CreateCommand(connection);
        }
        catch (Exception primaryException)
        {
            List<Exception>? cleanupExceptions = null;
            try { connection.Dispose(); }
            catch (Exception cleanupException) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, cleanupException); }
            InquiryCleanup.ThrowIfCleanupFailed(primaryException, cleanupExceptions);
            throw;
        }
    }

    // Prepares the command when enabled and it is not a stored procedure. Kept as a single guarded
    // statement so unsupported providers and explicit opt-outs stay branch-cheap.
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        DbDataReader? reader = null;
        try
        {
            try { InitializeCommandSync(dbCommand, command); }
            catch (Exception exception) { commandResources.Capture(exception); throw; }
            if (HasInterceptors)
            {
                try
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    commandResources.Capture(exception);
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
                commandResources.OwnReader(reader);
            }
            catch (Exception exception)
            {
                commandResources.Capture(exception);
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
                    commandResources.Capture(exception);
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
                    commandResources.Capture(exception);
                    if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                yield return item;
            }

            if (HasInterceptors)
            {
                try
                {
                    await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    commandResources.Capture(exception);
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            await commandResources.DisposeAsync().ConfigureAwait(false);
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            var reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
            commandResources.OwnReader(reader);
            var list = new List<T>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(materializer.Materialize(reader));
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        InquiryCommand command,
        IInquiryEntityMaterializer<T> materializer,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (materializer is null) throw new ArgumentNullException(nameof(materializer));

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            var reader = await dbCommand.ExecuteReaderAsync(SingleRowBehavior, cancellationToken).ConfigureAwait(false);
            commandResources.OwnReader(reader);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                return default;
            }

            var result = materializer.Materialize(reader);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.");
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task<InquiryGridReader> QueryMultipleAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        DbCommand? dbCommand = null;
        DbDataReader? reader = null;
        try
        {
            dbCommand = CreateCommand(connection);
            InitializeCommandSync(dbCommand, command);
            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            // SequentialAccess (forward-only per row) but NOT SingleResult — the grid reader needs
            // NextResult. Interceptors are bypassed (the lifetime spans multiple reads, like bulk insert).
            reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            return new InquiryGridReader(reader, dbCommand, ownedConnection: connection, lease: null);
        }
        catch (Exception primaryException)
        {
            var exceptions = new List<Exception> { primaryException };
            try
            {
                if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) { exceptions.Add(exception); }
            if (dbCommand is not null)
            {
                try { InquiryCommandResources.Dispose(dbCommand); }
                catch (Exception exception) { exceptions.Add(exception); }
                try { await dbCommand.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception) { exceptions.Add(exception); }
            }
            try { await connection.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { exceptions.Add(exception); }
            InquiryCleanup.ThrowIfAny(exceptions);
            throw;
        }
    }

    // ---- Struct-materializer overloads (generated-store path) -----------------------------
    //
    // Bodies mirror the class overloads but `materializer.Materialize(reader)` inlines because
    // the JIT specializes per TMaterializer. Keeping the bodies separate (rather than calling
    // a shared inner method) preserves that inlining.

    /// <inheritdoc />
    public async IAsyncEnumerable<T> QueryAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        DbDataReader? reader = null;
        try
        {
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
                    commandResources.Capture(exception);
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
                commandResources.OwnReader(reader);
            }
            catch (Exception exception)
            {
                commandResources.Capture(exception);
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
                    commandResources.Capture(exception);
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
                    commandResources.Capture(exception);
                    if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                yield return item;
            }

            if (HasInterceptors)
            {
                try
                {
                    await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    commandResources.Capture(exception);
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            await commandResources.DisposeAsync().ConfigureAwait(false);
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);

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

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);

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
                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                return default;
            }

            var result = materializer.Materialize(reader);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.");
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        InquiryCommand? interceptorCommand = null;
        DbDataReader? reader = null;
        try
        {
            try
            {
                // Lazy: only allocate the InquiryCommand if interceptors need to observe the command.
                interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;
                dbCommand.CommandText = commandText;
                bindParameters(dbCommand, args);
                _connectionFactory.FinalizeCommand(dbCommand);
            }
            catch (Exception exception) { commandResources.Capture(exception); throw; }
            if (interceptorCommand is not null)
            {
                try
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    commandResources.Capture(exception);
                    await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
                commandResources.OwnReader(reader);
            }
            catch (Exception exception)
            {
                commandResources.Capture(exception);
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
                    commandResources.Capture(exception);
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
                    commandResources.Capture(exception);
                    if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                yield return item;
            }

            if (interceptorCommand is not null)
            {
                try
                {
                    await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    commandResources.Capture(exception);
                    await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            await commandResources.DisposeAsync().ConfigureAwait(false);
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        InquiryCommand? interceptorCommand = null;

        try
        {
            // Lazy: only allocate the InquiryCommand if interceptors need to observe the command.
            interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;
            dbCommand.CommandText = commandText;
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
            var list = new List<T>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(materializer.Materialize(reader));
            }

            if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        InquiryCommand? interceptorCommand = null;

        try
        {
            interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;
            dbCommand.CommandText = commandText;
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
                if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                return default;
            }

            var result = materializer.Materialize(reader);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.");
            }

            if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(InquiryCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);

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

    /// <inheritdoc />
    public async Task<T> ExecuteScalarAsync<T>(InquiryCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);

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

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return ScalarConvert.From<T>(value);
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

    /// <inheritdoc />
    public async Task<T> ExecuteProcedureScalarAsync<T>(InquiryCommand command, string readBackParameterName, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (string.IsNullOrWhiteSpace(readBackParameterName)) throw new ArgumentException("Read-back parameter name cannot be empty.", nameof(readBackParameterName));

        // Normalize the lookup name the same way the binder normalizes the bound parameter's name,
        // so a caller-supplied "Total" still matches the bound "@Total".
        readBackParameterName = InquiryParameterBinder.NormalizeName(readBackParameterName);

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            // No Prepare: a procedure call with output parameters runs once; preparing it adds a
            // round trip with no reuse benefit.
            var recordsAffected = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // ADO.NET populates output / return-value DbParameters after ExecuteNonQuery; read the
            // named one back and convert it the same way as a scalar result.
            var readBack = ScalarConvert.From<T>(dbCommand.Parameters[readBackParameterName].Value);

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

    /// <inheritdoc />
    public async Task<T> ExecuteScalarAsync<T, TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        InquiryCommand? interceptorCommand = null;

        try
        {
            interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;
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

            if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return ScalarConvert.From<T>(value);
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

    /// <inheritdoc />
    public async Task<int> ExecuteAsync<TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        InquiryCommand? interceptorCommand = null;

        try
        {
            // Lazy: only allocate the InquiryCommand if interceptors are present (and only for the
            // failure path if execution throws before the first interceptor call).
            interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;
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
            commandResources.Capture(exception);
            if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await commandResources.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// When the connection factory allows batching and the provider supports
    /// <see cref="DbConnection.CanCreateBatch"/> with parameter creation on
    /// <see cref="DbBatchCommand"/>, all items execute in a single <see cref="DbBatch"/> round
    /// trip. Interceptors do NOT fire on the DbBatch path — there is no <see cref="DbCommand"/>
    /// to expose to them. The sequential fallback (one connection, one command per item) fires
    /// interceptors per command as usual.
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        DbBatch? batch = null;
        Exception? primaryException = null;
        try
        {
            if (_connectionFactory.SupportsBatchExecution && connection.CanCreateBatch)
            {
                // Probe: some providers expose DbBatch but not DbBatchCommand.CreateParameter; those
                // fall back to the sequential path below.
                batch = connection.CreateBatch();
                var firstCommand = batch.CreateBatchCommand();
                if (firstCommand.CanCreateParameter)
                {
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

            // Sequential fallback: one connection, one command per item (mirrors ExecuteAsync<TArgs>).
            var total = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var dbCommand = CreateCommand(connection);
                var commandResources = InquiryCommandResources.CreateScope(dbCommand);
                InquiryCommand? interceptorCommand = null;

                try
                {
                    // Lazy: only allocate the InquiryCommand if interceptors are present.
                    interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;
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
                    commandResources.Capture(exception);
                    if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
                finally
                {
                    await commandResources.DisposeAsync().ConfigureAwait(false);
                }
            }

            return total;
        }
        catch (Exception exception)
        {
            primaryException = exception;
            throw;
        }
        finally
        {
            List<Exception>? cleanupExceptions = null;
            try { if (batch is not null) await batch.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, exception); }
            try { await connection.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, exception); }
            if (primaryException is not null) InquiryCleanup.ThrowIfCleanupFailed(primaryException, cleanupExceptions);
            else InquiryCleanup.ThrowIfAny(cleanupExceptions);
        }
    }

    // ---- Synchronous setup + interceptor slow paths --------------------------------------

    private void InitializeCommandSync(DbCommand dbCommand, InquiryCommand command)
    {
        dbCommand.CommandText = command.CommandText;
        if (command.CommandType is not null) dbCommand.CommandType = command.CommandType.Value;
        if (command.CommandTimeout is not null) dbCommand.CommandTimeout = command.CommandTimeout.Value;
        InquiryParameterBinder.Bind(dbCommand, command.ParametersArray);
        command.DbCommandBinder?.Invoke(dbCommand);
        _connectionFactory.FinalizeCommand(dbCommand);
    }

    private async ValueTask InvokeInitializedAsync(DbCommand dbCommand, InquiryCommand command, CancellationToken cancellationToken)
    {
        var context = new InquiryCommandContext(command, dbCommand);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandInitializedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeExecutingAsync(InquiryCommand command, DbCommand dbCommand, CancellationToken cancellationToken)
    {
        var context = new InquiryCommandContext(command, dbCommand);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandExecutingAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeExecutedAsync(InquiryCommand command, DbCommand dbCommand, int? recordsAffected, CancellationToken cancellationToken)
    {
        var context = new InquiryCommandExecutedContext(command, dbCommand, recordsAffected);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandExecutedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeFailedAsync(InquiryCommand command, DbCommand dbCommand, Exception exception, CancellationToken cancellationToken)
    {
        var context = new InquiryCommandFailedContext(command, dbCommand, exception);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandFailedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
