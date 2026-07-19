using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Diagnostics;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Parameters;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
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
/// The class-materializer read methods always pass <see cref="CommandBehavior.SingleResult"/> and add
/// <see cref="CommandBehavior.SequentialAccess"/> only when the materializer declares that its reads are
/// forward-only. Struct-materializer overloads always add <see cref="CommandBehavior.SequentialAccess"/>
/// because generated materializers read each column in ascending ordinal order.
///
/// Generated query bodies snapshot <see cref="HasActiveInterceptors"/> before allocating interceptor
/// state. With no active interceptor, command-context allocations and notification awaits are omitted.
/// </remarks>
internal sealed class InquiryRequestPipeline : IInquiryRequestPipeline
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

    private readonly IInquiryConnectionFactory _connectionFactory;
    private readonly IInquiryCommandInterceptor[] _interceptors;

    // True when Auto preparation is configured AND the provider's prepared state survives the
    // connection lifecycle. The per-command StoredProcedure check is applied at the call site.
    private readonly bool _prepareEnabled;
    private readonly bool _autoPrepareConfigured;

    // Whole seconds from InquiryOptions.DefaultCommandTimeout; 0 = not configured (provider default).
    private readonly int _defaultCommandTimeoutSeconds;
    private readonly int _maxBatchSize;
    private readonly int _maxParametersPerCommand;

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
        _autoPrepareConfigured = (options?.PrepareStatements ?? PreparedStatementMode.Auto) == PreparedStatementMode.Auto;
        _prepareEnabled = _autoPrepareConfigured
            && _connectionFactory.SupportsPersistentPreparedStatements;
        _defaultCommandTimeoutSeconds = options?.DefaultCommandTimeout is { } timeout
            ? (int)Math.Ceiling(timeout.TotalSeconds)
            : 0;
        _maxBatchSize = options?.MaxBatchSize ?? InquiryOptions.DefaultMaxBatchSize;
        _maxParametersPerCommand = options?.MaxParametersPerCommand ?? InquiryOptions.DefaultMaxParametersPerCommand;
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

    private static (Activity? activity, long startTimestamp) StartGridActivity(DbCommand dbCommand)
    {
        if (!InquiryTelemetry.ActivitySource.HasListeners() && !InquiryTelemetry.CommandDuration.Enabled)
            return (null, 0);

        var startTimestamp = Stopwatch.GetTimestamp();
        Activity? activity = null;
        if (InquiryTelemetry.ActivitySource.HasListeners())
        {
            activity = InquiryTelemetry.ActivitySource.StartActivity("QUERY_MULTIPLE", ActivityKind.Client);
            if (activity is not null)
            {
                var dbSystem = InquiryTelemetry.MapDbSystem(dbCommand);
                activity.SetTag("db.system.name", dbSystem);
                activity.SetTag("db.operation.name", "QUERY_MULTIPLE");
            }
        }

        return (activity, startTimestamp);
    }

    private static void RecordGridFailure(Activity? activity, DbCommand? dbCommand, long startTimestamp, Exception exception)
    {
        var errorType = exception.GetType().FullName ?? exception.GetType().Name;

        if (startTimestamp != 0 && dbCommand is not null)
        {
            InquiryTelemetry.CommandDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                new KeyValuePair<string, object?>("db.system.name", InquiryTelemetry.MapDbSystem(dbCommand)),
                new KeyValuePair<string, object?>("db.operation.name", "QUERY_MULTIPLE"),
                new KeyValuePair<string, object?>("error.type", errorType));
        }

        if (activity is not null)
        {
            activity.SetTag("error.type", errorType);
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.Dispose();
        }
    }

    private static (Activity? activity, long startTimestamp, string dbSystem) StartBatchActivity(
        DbConnection connection, string? commandText)
    {
        if (!InquiryTelemetry.ActivitySource.HasListeners() && !InquiryTelemetry.CommandDuration.Enabled)
            return (null, 0, "");

        var dbSystem = InquiryTelemetry.MapDbSystem(connection);
        var startTimestamp = Stopwatch.GetTimestamp();
        Activity? activity = null;
        if (InquiryTelemetry.ActivitySource.HasListeners())
        {
            activity = InquiryTelemetry.ActivitySource.StartActivity("EXECUTE_BATCH", ActivityKind.Client);
            if (activity is not null)
            {
                activity.SetTag("db.system.name", dbSystem);
                activity.SetTag("db.operation.name", "EXECUTE_BATCH");
                var table = InquiryTelemetry.ExtractTableName(commandText);
                if (table is not null)
                {
                    activity.SetTag("db.collection.name", table);
                }
            }
        }

        return (activity, startTimestamp, dbSystem);
    }

    private static void RecordBatchCompletion(Activity? activity, long startTimestamp, string dbSystem, int total)
    {
        if (startTimestamp == 0) return;

        InquiryTelemetry.CommandDuration.Record(
            Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
            new KeyValuePair<string, object?>("db.system.name", dbSystem),
            new KeyValuePair<string, object?>("db.operation.name", "EXECUTE_BATCH"));

        if (activity is not null)
        {
            activity.SetTag("db.response.affected_rows", total);
            activity.Dispose();
        }
    }

    private static void RecordBatchFailure(Activity? activity, long startTimestamp, string dbSystem, Exception exception)
    {
        var errorType = exception.GetType().FullName ?? exception.GetType().Name;

        if (startTimestamp != 0)
        {
            InquiryTelemetry.CommandDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                new KeyValuePair<string, object?>("db.system.name", dbSystem),
                new KeyValuePair<string, object?>("db.operation.name", "EXECUTE_BATCH"),
                new KeyValuePair<string, object?>("error.type", errorType));
        }

        if (activity is not null)
        {
            activity.SetTag("error.type", errorType);
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.Dispose();
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
        var readBehavior = materializer.IsInquirySequentialAccessSafe ? SequentialReadBehavior : ReadBehavior;

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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
                    throw;
                }
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                reader = await dbCommand.ExecuteReaderAsync(readBehavior, cancellationToken).ConfigureAwait(false);
                commandResources.OwnReader(reader);
            }
            catch (Exception exception)
            {
                commandResources.Capture(exception);
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
        var readBehavior = materializer.IsInquirySequentialAccessSafe ? SequentialReadBehavior : ReadBehavior;

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
            var reader = await dbCommand.ExecuteReaderAsync(readBehavior, cancellationToken).ConfigureAwait(false);
            commandResources.OwnReader(reader);
            var list = new List<T>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(materializer.Materialize(reader));
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return list;
        }
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        InquiryCommand command,
        IInquiryEntityMaterializer<T> materializer,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (materializer is null) throw new ArgumentNullException(nameof(materializer));
        var readBehavior = materializer.IsInquirySequentialAccessSafe ? SequentialSingleRowBehavior : SingleRowBehavior;

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
            var reader = await dbCommand.ExecuteReaderAsync(readBehavior, cancellationToken).ConfigureAwait(false);
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
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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

    /// <inheritdoc />
    public async Task<InquiryGridReader> QueryMultipleAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        DbCommand? dbCommand = null;
        DbDataReader? reader = null;
        Activity? activity = null;
        long startTimestamp = 0;
        try
        {
            dbCommand = CreateCommand(connection);
            InitializeCommandSync(dbCommand, command);
            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            (activity, startTimestamp) = StartGridActivity(dbCommand);
            reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            var grid = new InquiryGridReader(reader, dbCommand, ownedConnection: connection, lease: null,
                activity, startTimestamp);
            activity = null;
            return grid;
        }
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            RecordGridFailure(activity, dbCommand, startTimestamp, normalized);
            var exceptions = new List<Exception> { normalized };
            try
            {
                if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { exceptions.Add(ex); }
            if (dbCommand is not null)
            {
                try { InquiryCommandResources.Dispose(dbCommand); }
                catch (Exception ex) { exceptions.Add(ex); }
                try { await dbCommand.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { exceptions.Add(ex); }
            }
            try { await connection.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { exceptions.Add(ex); }
            InquiryCleanup.ThrowIfAny(exceptions);
            throw;
        }
        catch (Exception primaryException)
        {
            RecordGridFailure(activity, dbCommand, startTimestamp, primaryException);
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

    /// <inheritdoc />
    public async Task<InquiryGridReader> QueryMultipleAsync<TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        CancellationToken cancellationToken = default)
    {
        command.Validate();
        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        DbCommand? dbCommand = null;
        DbDataReader? reader = null;
        Activity? activity = null;
        long startTimestamp = 0;
        try
        {
            dbCommand = CreateCommand(connection);
            dbCommand.CommandText = command.CommandText;
            dbCommand.CommandType = command.CommandType;
            command.BindParameters(dbCommand, command.Args);
            _connectionFactory.FinalizeCommand(dbCommand);
            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            (activity, startTimestamp) = StartGridActivity(dbCommand);
            reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            var grid = new InquiryGridReader(reader, dbCommand, ownedConnection: connection, lease: null,
                activity, startTimestamp);
            activity = null;
            return grid;
        }
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            RecordGridFailure(activity, dbCommand, startTimestamp, normalized);
            var exceptions = new List<Exception> { normalized };
            try { if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { exceptions.Add(ex); }
            if (dbCommand is not null)
            {
                try { InquiryCommandResources.Dispose(dbCommand); }
                catch (Exception ex) { exceptions.Add(ex); }
                try { await dbCommand.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { exceptions.Add(ex); }
            }
            try { await connection.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { exceptions.Add(ex); }
            InquiryCleanup.ThrowIfAny(exceptions);
            throw;
        }
        catch (Exception primaryException)
        {
            RecordGridFailure(activity, dbCommand, startTimestamp, primaryException);
            var exceptions = new List<Exception> { primaryException };
            try { if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false); }
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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
                InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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
                interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;
                dbCommand.CommandText = commandText;
                dbCommand.CommandType = commandType;
                bindParameters(dbCommand, args);
                _connectionFactory.FinalizeCommand(dbCommand);
            }
            catch (Exception exception) { commandResources.Capture(exception); InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken); throw; }
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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
                InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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
                    InquiryCancellation.ThrowIfRequiresCallerToken(exception, cancellationToken);
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        InquiryCommand? interceptorCommand = null;

        try
        {
            // Lazy: only allocate the InquiryCommand if interceptors need to observe the command.
            interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;
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

            if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return list;
        }
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        InquiryCommand? interceptorCommand = null;

        try
        {
            interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;
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
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            // Some providers (notably ODP.NET) translate their native cancellation error into an
            // OperationCanceledException carrying an internal/default token. Preserve the public
            // Inquiry contract by associating the failure with the caller token that reached ADO.NET.
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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
            var readBack = ScalarConvert.From<T>(InquiryParameterBinder.FindByLogicalName(dbCommand.Parameters, readBackParameterName).Value);

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected, cancellationToken).ConfigureAwait(false);
            return readBack;
        }
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        InquiryCommand? interceptorCommand = null;

        try
        {
            interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;
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
                if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                return default;
            }

            var result = materializer.Materialize(reader);
            if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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

    /// <inheritdoc />
    public async Task<T> ExecuteProcedureScalarAsync<T, TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        string readBackParameterName,
        CancellationToken cancellationToken = default)
    {
        command.Validate();
        if (string.IsNullOrWhiteSpace(readBackParameterName)) throw new ArgumentException("Read-back parameter name cannot be empty.", nameof(readBackParameterName));
        readBackParameterName = InquiryParameterBinder.NormalizeName(readBackParameterName);

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        InquiryCommand? interceptorCommand = null;
        try
        {
            interceptorCommand = HasActiveInterceptors ? new InquiryCommand(command.CommandText, command.CommandType) : null;
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
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        InquiryCommand? interceptorCommand = null;

        try
        {
            interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;
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

            if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return ScalarConvert.From<T>(value);
        }
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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

        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dbCommand = CreateCommandOrDisposeConnection(connection);
        var commandResources = InquiryCommandResources.CreateScope(dbCommand, connection);
        InquiryCommand? interceptorCommand = null;

        try
        {
            // Lazy: only allocate the InquiryCommand if interceptors are present (and only for the
            // failure path if execution throws before the first interceptor call).
            interceptorCommand = HasActiveInterceptors ? new InquiryCommand(commandText, commandType) : null;
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
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            commandResources.Capture(normalized);
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

    /// <inheritdoc />
    /// <remarks>
    /// When available, bounded chunks execute through <see cref="DbBatch"/>. Otherwise the pipeline
    /// reuses one command and parameter set. The whole operation owns one transaction; active
    /// interceptors retain a per-physical-command lifecycle.
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

        DbConnection? connection = null;
        DbTransaction? transaction = null;
        var committed = false;
        Exception? primaryException = null;
        List<Exception>? cleanupExceptions = null;
        Activity? batchActivity = null;
        long batchStartTimestamp = 0;
        string batchDbSystem = "";
        try
        {
            connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            (batchActivity, batchStartTimestamp, batchDbSystem) =
                StartBatchActivity(connection, command.CommandText);
            transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            var hasActiveInterceptors = HasActiveInterceptors;
            Func<IReadOnlyList<TItem>, CancellationToken, Task<int>>? interceptedRows = hasActiveInterceptors
                ? ExecuteInterceptedChunkAsync
                : null;
            Func<IReadOnlyList<TItem>, CancellationToken, Task<int>>? interceptedChunk = hasActiveInterceptors
                ? ExecuteInterceptedWholeChunkAsync
                : null;
            var total = await InquiryBatchCommandExecutor.ExecuteAsync(
                connection, transaction, _connectionFactory, executionMode, _defaultCommandTimeoutSeconds,
                _prepareEnabled,
                _autoPrepareConfigured && command.PreferPrepareOnce,
                _maxParametersPerCommand,
                command, chunks, firstChunk, interceptedRows, interceptedChunk, cancellationToken).ConfigureAwait(false);
            chunks.Dispose();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            committed = true;
            RecordBatchCompletion(batchActivity, batchStartTimestamp, batchDbSystem, total);
            batchActivity = null;
            return total;

            async Task<int> ExecuteInterceptedChunkAsync(IReadOnlyList<TItem> chunk, CancellationToken token)
            {
                var totalAffected = 0;
                for (var i = 0; i < chunk.Count; i++)
                {
                    var dbCommand = CreateCommand(connection);
                    var resources = InquiryCommandResources.CreateScope(dbCommand);
                    InquiryCommand? interceptorCommand = null;
                    try
                    {
                        interceptorCommand = new InquiryCommand(command.CommandText!, command.CommandType);
                        dbCommand.Transaction = transaction;
                        dbCommand.CommandText = command.CommandText;
                        dbCommand.CommandType = command.CommandType;
                        command.BindItem!(new InquiryParameterTarget(dbCommand), chunk[i]);
                        _connectionFactory.FinalizeCommand(dbCommand);
                        await InvokeInitializedAsync(dbCommand, interceptorCommand, token).ConfigureAwait(false);
                        await InvokeExecutingAsync(interceptorCommand, dbCommand, token).ConfigureAwait(false);
                        await MaybePrepareAsync(dbCommand, token).ConfigureAwait(false);
                        var affected = await dbCommand.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        await InvokeExecutedAsync(interceptorCommand, dbCommand, affected, token).ConfigureAwait(false);
                        totalAffected += affected;
                    }
                    catch (Exception exception)
                    {
                        resources.Capture(exception);
                        if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, token).ConfigureAwait(false);
                        throw;
                    }
                    finally
                    {
                        await resources.DisposeAsync().ConfigureAwait(false);
                    }
                }

                return totalAffected;
            }

            async Task<int> ExecuteInterceptedWholeChunkAsync(IReadOnlyList<TItem> chunk, CancellationToken token)
            {
                var commandText = command.GetChunkCommandText(chunk.Count);
                var dbCommand = CreateCommand(connection);
                var resources = InquiryCommandResources.CreateScope(dbCommand);
                InquiryCommand? interceptorCommand = null;
                try
                {
                    interceptorCommand = new InquiryCommand(commandText, command.CommandType);
                    dbCommand.Transaction = transaction;
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
                    if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, token).ConfigureAwait(false);
                    throw;
                }
                finally
                {
                    await resources.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException exception)
            when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            primaryException = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            RecordBatchFailure(batchActivity, batchStartTimestamp, batchDbSystem, primaryException);
            batchActivity = null;
            throw primaryException;
        }
        catch (Exception exception)
        {
            primaryException = exception;
            RecordBatchFailure(batchActivity, batchStartTimestamp, batchDbSystem, exception);
            batchActivity = null;
            throw;
        }
        finally
        {
            batchActivity?.Dispose();
            try { chunks.Dispose(); }
            catch (Exception exception) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, exception); }
            try
            {
                if (transaction is not null && !committed && primaryException is not null)
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, exception); }
            try { if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, exception); }
            try { if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false); }
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
            if (interceptor is IInquiryInterceptorActivation { IsActive: false }) continue;
            await interceptor.CommandInitializedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeExecutingAsync(InquiryCommand command, DbCommand dbCommand, CancellationToken cancellationToken)
    {
        var context = new InquiryCommandContext(command, dbCommand);
        foreach (var interceptor in _interceptors)
        {
            if (interceptor is IInquiryInterceptorActivation { IsActive: false }) continue;
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
