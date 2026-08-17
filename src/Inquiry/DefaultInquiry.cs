using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Pipeline;
using Inquiry.Transactions;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace Inquiry;

/// <summary>
/// Default implementation of the high-level Inquiry facade.
/// </summary>
/// <remarks>
/// <para>
/// Holds an <see cref="AsyncLocal{T}"/> slot for the active transactional pipeline. When
/// <see cref="BeginTransactionAsync"/> runs, it pushes a <see cref="TransactedInquiryRequestPipeline"/>
/// onto that slot; every query/execute method then routes through the ambient pipeline instead
/// of the default one. This is what makes generated stores — which hold a single <see cref="IInquiry"/>
/// reference from DI — automatically participate in any transaction the caller opens.
/// </para>
/// <para>
/// The current async flow is detached on transaction commit, rollback, or dispose; straggler
/// async work that captured the old transaction slot fails fast after the transaction closes.
/// </para>
/// </remarks>
internal sealed class DefaultInquiry : IInquiry
{
    private readonly IInquiryRequestPipeline _defaultPipeline;
    private readonly IInquiryConnectionFactory _connectionFactory;
    private readonly IInquiryCommandInterceptor[] _interceptors;
    private readonly IServiceProvider _serviceProvider;
    private readonly InquiryOptions? _options;

    // The slot stores a *holder* rather than the pipeline directly. Setting an AsyncLocal
    // value from inside an async callee does not propagate back to the caller (see
    // https://learn.microsoft.com/dotnet/api/system.threading.asynclocal-1), so we install
    // the holder synchronously at the top of BeginTransactionAsync — before any await —
    // and mutate the holder's Pipeline field once the connection/transaction is open.
    // The caller's async context already references the same holder, so the mutation is
    // visible across the await boundary.
    private readonly AsyncLocal<AmbientTransactionSlot?> _ambientSlot = new();

    // Monotonic counter for unique savepoint names. The savepoint name is only seen by the
    // database (and by debugging traces); a simple incrementing integer is sufficient and
    // ensures uniqueness even if multiple savepoints are nested or created back-to-back.
    private long _savepointCounter;

    private sealed class AmbientTransactionSlot
    {
        public TransactedInquiryRequestPipeline? Pipeline;
        public IsolationLevel IsolationLevel;
        public AmbientTransactionSlotState State;
    }

    private enum AmbientTransactionSlotState
    {
        Pending,
        Active,
        Detached,
        Closed,
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInquiry"/> class.
    /// </summary>
    public DefaultInquiry(
        IInquiryRequestPipeline requestPipeline,
        IInquiryConnectionFactory connectionFactory,
        IEnumerable<IInquiryCommandInterceptor> interceptors,
        IServiceProvider serviceProvider,
        InquiryOptions? options = null)
    {
        _defaultPipeline = requestPipeline ?? throw new ArgumentNullException(nameof(requestPipeline));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _interceptors = interceptors?.ToArray() ?? throw new ArgumentNullException(nameof(interceptors));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options;
    }

    private IInquiryRequestPipeline ActivePipeline
    {
        get
        {
            var slot = _ambientSlot.Value;
            if (slot is null)
            {
                return _defaultPipeline;
            }

            if (slot.Pipeline is not null)
            {
                return slot.Pipeline;
            }

            if (slot.State == AmbientTransactionSlotState.Closed)
            {
                throw CreateClosedAmbientTransactionException();
            }

            return _defaultPipeline;
        }
    }

    private static ObjectDisposedException CreateClosedAmbientTransactionException()
        => new(
            "Inquiry ambient transaction",
            "This async flow captured an Inquiry transaction that has already been committed, rolled back, or disposed. " +
            "Start a new operation after the transaction scope, or await child work before closing the transaction.");

    /// <inheritdoc />
    public bool ThrowOnConcurrencyConflict => _options?.ThrowOnConcurrencyConflict ?? false;

    /// <inheritdoc />
    public int MaxParametersPerCommand => _options?.MaxParametersPerCommand ?? InquiryOptions.DefaultMaxParametersPerCommand;

    /// <inheritdoc />
    public int MaxBatchSize => _options?.MaxBatchSize ?? InquiryOptions.DefaultMaxBatchSize;

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        FormattableString commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => QueryAsync<TEntity>(InquirySql.Sql(commandText), cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => ActivePipeline.QueryAsync(command, GetMaterializer<TEntity>(), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        FormattableString commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => QueryListAsync<TEntity>(InquirySql.Sql(commandText), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => ActivePipeline.QueryListAsync(command, GetMaterializer<TEntity>(), cancellationToken);

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        FormattableString commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => QuerySingleOrDefaultAsync<TEntity>(InquirySql.Sql(commandText), cancellationToken);

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => ActivePipeline.QuerySingleOrDefaultAsync(command, GetMaterializer<TEntity>(), cancellationToken);

    // ---- Struct-materializer overloads (generated-store path) ------------------------------

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QueryAsync<TEntity, TMaterializer>(command, materializer, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default,
        int capacityHint = -1)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QueryListAsync<TEntity, TMaterializer>(command, materializer, cancellationToken, capacityHint);

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QuerySingleOrDefaultAsync<TEntity, TMaterializer>(command, materializer, cancellationToken);

    /// <inheritdoc />
    public Task<InquiryGridReader> QueryMultipleAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        => ActivePipeline.QueryMultipleAsync(command, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QueryAsync<TEntity, TArgs, TMaterializer>(commandText, args, bindParameters, materializer, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QueryListAsync<TEntity, TArgs, TMaterializer>(commandText, args, bindParameters, materializer, cancellationToken);

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QuerySingleOrDefaultAsync<TEntity, TArgs, TMaterializer>(commandText, args, bindParameters, materializer, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity, TArgs, TMaterializer>(
        InquiryGeneratedCommand<TArgs> command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QueryAsync<TEntity, TArgs, TMaterializer>(command, materializer, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity, TArgs, TMaterializer>(
        InquiryGeneratedCommand<TArgs> command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default,
        int capacityHint = -1)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QueryListAsync<TEntity, TArgs, TMaterializer>(command, materializer, cancellationToken, capacityHint);

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity, TArgs, TMaterializer>(
        InquiryGeneratedCommand<TArgs> command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QuerySingleOrDefaultAsync<TEntity, TArgs, TMaterializer>(command, materializer, cancellationToken);

    /// <inheritdoc />
    public Task<TEntity?> QueryGeneratedSingleOrDefaultAsync<TEntity, TArgs, TMaterializer>(
        InquiryGeneratedCommand<TArgs> command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QueryGeneratedSingleOrDefaultAsync<TEntity, TArgs, TMaterializer>(command, materializer, cancellationToken);

    /// <inheritdoc />
    public Task<InquiryGridReader> QueryMultipleAsync<TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        CancellationToken cancellationToken = default)
        => ActivePipeline.QueryMultipleAsync(command, cancellationToken);

    /// <inheritdoc />
    public Task<long> BulkInsertAsync<TEntity>(
        Inquiry.BulkCopy.InquiryBulkInsertDefinition<TEntity> definition,
        IEnumerable<TEntity> rows,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => BulkInsertAsync(definition, rows, options: null, cancellationToken);

    /// <inheritdoc />
    public async Task<long> BulkInsertAsync<TEntity>(
        Inquiry.BulkCopy.InquiryBulkInsertDefinition<TEntity> definition,
        IEnumerable<TEntity> rows,
        Inquiry.BulkCopy.InquiryBulkInsertOptions? options,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (rows is null) throw new ArgumentNullException(nameof(rows));

        options ??= new Inquiry.BulkCopy.InquiryBulkInsertOptions();
        options.Validate();

        var ambientSlot = _ambientSlot.Value;
        if (ambientSlot?.State == AmbientTransactionSlotState.Closed)
        {
            throw CreateClosedAmbientTransactionException();
        }

        var pipeline = ambientSlot?.Pipeline;
        if (options.ConnectionBehavior == Inquiry.BulkCopy.InquiryBulkInsertConnectionBehavior.RequireAmbientTransaction
            && pipeline is null)
            throw new InvalidOperationException("Bulk insert option ConnectionBehavior requires an ambient Inquiry transaction, but none is active.");
        if (options.ConnectionBehavior == Inquiry.BulkCopy.InquiryBulkInsertConnectionBehavior.RequireDedicatedConnection
            && pipeline is not null)
            throw new InvalidOperationException("Bulk insert option ConnectionBehavior requires a dedicated connection, but an ambient Inquiry transaction is active.");

        var copier = _serviceProvider.GetService<Inquiry.BulkCopy.IInquiryBulkCopier>()
            ?? throw new InvalidOperationException(
                "No IInquiryBulkCopier is registered. Bulk insert needs a provider with a native bulk-copy API " +
                "(SQL Server, PostgreSQL, MySQL); on other providers use the [InquiryInsertAll] batch insert.");

        var enlisted = pipeline is not null;
        var (activity, startTimestamp, dbSystem) = StartBulkInsertActivity(definition.Table, enlisted);
        var context = new Inquiry.BulkCopy.InquiryBulkInsertContext(
            pipeline?.Connection,
            pipeline?.Transaction,
            options,
            duration => RecordBulkConnectionOpen(activity, dbSystem, enlisted, duration),
            (duration, rowCount) => RecordBulkCopy(activity, dbSystem, enlisted, duration, rowCount));
        IDisposable? operationLease = null;
        try
        {
            operationLease = pipeline?.EnterExclusiveOperation();
            if (enlisted) context.RecordConnectionOpened(TimeSpan.Zero);
            // Same cancellation contract as the command pipeline (#282/#283): a driver-native error
            // surfaced after the caller's token fired (MySqlConnector wraps the cancel in a
            // MySqlException during LOAD DATA LOCAL INFILE) normalizes to an OCE carrying the
            // caller's token, and completion that beat the token is trusted.
            var rowCount = await InquiryCancellation.AwaitEnforcingCallerToken(
                copier.BulkInsertAsync(definition, rows, context, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            RecordBulkInsertCompletion(activity, startTimestamp, dbSystem, enlisted, rowCount);
            return rowCount;
        }
        catch (OperationCanceledException exception) when (InquiryCancellation.RequiresCallerToken(exception, cancellationToken))
        {
            var normalized = InquiryCancellation.AssociateWithCallerToken(exception, cancellationToken);
            RecordBulkInsertFailure(activity, startTimestamp, dbSystem, enlisted, normalized);
            throw normalized;
        }
        catch (Exception exception)
        {
            RecordBulkInsertFailure(activity, startTimestamp, dbSystem, enlisted, exception);
            throw;
        }
        finally
        {
            operationLease?.Dispose();
        }
    }

    private (Activity? activity, long startTimestamp, string dbSystem) StartBulkInsertActivity(string table, bool enlisted)
    {
        if (!Diagnostics.InquiryTelemetry.ActivitySource.HasListeners()
            && !Diagnostics.InquiryTelemetry.CommandDuration.Enabled
            && !Diagnostics.InquiryTelemetry.BulkConnectionOpenDuration.Enabled
            && !Diagnostics.InquiryTelemetry.BulkCopyDuration.Enabled)
            return (null, 0, "");

        var dbSystem = Diagnostics.InquiryTelemetry.MapDbSystem(_connectionFactory);
        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        Activity? activity = null;
        if (Diagnostics.InquiryTelemetry.ActivitySource.HasListeners())
        {
            activity = Diagnostics.InquiryTelemetry.ActivitySource.StartActivity("BULK_INSERT", ActivityKind.Client);
            if (activity is not null)
            {
                activity.SetTag("db.system.name", dbSystem);
                activity.SetTag("db.operation.name", "BULK_INSERT");
                activity.SetTag("db.collection.name", table);
                activity.SetTag("inquiry.bulk_insert.connection_mode", enlisted ? "enlisted" : "dedicated");
            }
        }

        return (activity, startTimestamp, dbSystem);
    }

    private static void RecordBulkInsertCompletion(Activity? activity, long startTimestamp, string dbSystem, bool enlisted, long rowCount)
    {
        if (startTimestamp == 0) return;

        Diagnostics.InquiryTelemetry.CommandDuration.Record(
            System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
            new KeyValuePair<string, object?>("db.system.name", dbSystem),
            new KeyValuePair<string, object?>("db.operation.name", "BULK_INSERT"),
            new KeyValuePair<string, object?>("inquiry.bulk_insert.connection_mode", enlisted ? "enlisted" : "dedicated"));

        if (activity is not null)
        {
            activity.SetTag("db.response.affected_rows", rowCount);
            activity.Dispose();
        }
    }

    private static void RecordBulkInsertFailure(Activity? activity, long startTimestamp, string dbSystem, bool enlisted, Exception exception)
    {
        var errorType = exception.GetType().FullName ?? exception.GetType().Name;

        if (startTimestamp != 0)
        {
            Diagnostics.InquiryTelemetry.CommandDuration.Record(
                System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                new KeyValuePair<string, object?>("db.system.name", dbSystem),
                new KeyValuePair<string, object?>("db.operation.name", "BULK_INSERT"),
                new KeyValuePair<string, object?>("inquiry.bulk_insert.connection_mode", enlisted ? "enlisted" : "dedicated"),
                new KeyValuePair<string, object?>("error.type", errorType));
        }

        if (activity is not null)
        {
            activity.SetTag("error.type", errorType);
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.Dispose();
        }
    }

    private static void RecordBulkConnectionOpen(Activity? activity, string dbSystem, bool enlisted, TimeSpan duration)
    {
        Diagnostics.InquiryTelemetry.BulkConnectionOpenDuration.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>("db.system.name", dbSystem),
            new KeyValuePair<string, object?>("inquiry.bulk_insert.connection_mode", enlisted ? "enlisted" : "dedicated"));
        activity?.AddEvent(new ActivityEvent(
            enlisted ? "connection.reused" : "connection.opened",
            tags: new ActivityTagsCollection
            {
                { "inquiry.bulk_insert.connection_open.duration", duration.TotalSeconds },
            }));
    }

    private static void RecordBulkCopy(Activity? activity, string dbSystem, bool enlisted, TimeSpan duration, long? rowCount)
    {
        Diagnostics.InquiryTelemetry.BulkCopyDuration.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>("db.system.name", dbSystem),
            new KeyValuePair<string, object?>("inquiry.bulk_insert.connection_mode", enlisted ? "enlisted" : "dedicated"));
        activity?.AddEvent(new ActivityEvent(
            rowCount.HasValue ? "copy.completed" : "copy.failed",
            tags: new ActivityTagsCollection
            {
                { "inquiry.bulk_insert.copy.duration", duration.TotalSeconds },
                { "db.response.affected_rows", rowCount },
            }));
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(
        FormattableString commandText,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(InquirySql.Sql(commandText), cancellationToken);

    /// <inheritdoc />
    public Task<int> ExecuteAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        => ActivePipeline.ExecuteAsync(command, cancellationToken);

    /// <inheritdoc />
    public Task<int> ExecuteAsync<TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default)
        => ActivePipeline.ExecuteAsync(commandText, args, bindParameters, cancellationToken);

    /// <inheritdoc />
    public Task<int> ExecuteAsync<TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        CancellationToken cancellationToken = default)
        => ActivePipeline.ExecuteAsync(command, cancellationToken);

    /// <inheritdoc />
    public Task<int> ExecuteBatchAsync<TItem>(
        string commandText,
        IReadOnlyList<TItem> items,
        Action<InquiryParameterTarget, TItem> bindParameters,
        CancellationToken cancellationToken = default)
        => ExecuteBatchAsync(new InquiryBatchCommand<TItem>(commandText, bindParameters), items, cancellationToken);

    /// <inheritdoc />
    public Task<int> ExecuteBatchAsync<TItem>(
        InquiryBatchCommand<TItem> command,
        IEnumerable<TItem> items,
        CancellationToken cancellationToken = default)
        => ActivePipeline.ExecuteBatchAsync(command, items, cancellationToken);

    /// <inheritdoc />
    public Task<T> ExecuteScalarAsync<T>(
        FormattableString commandText,
        CancellationToken cancellationToken = default)
        => ActivePipeline.ExecuteScalarAsync<T>(InquirySql.Sql(commandText), cancellationToken);

    /// <inheritdoc />
    public Task<T> ExecuteScalarAsync<T>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        => ActivePipeline.ExecuteScalarAsync<T>(command, cancellationToken);

    /// <inheritdoc />
    public Task<T> ExecuteProcedureScalarAsync<T>(
        InquiryCommand command,
        string readBackParameterName,
        CancellationToken cancellationToken = default)
        => ActivePipeline.ExecuteProcedureScalarAsync<T>(command, readBackParameterName, cancellationToken);

    /// <inheritdoc />
    public Task<T> ExecuteProcedureScalarAsync<T, TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        string readBackParameterName,
        CancellationToken cancellationToken = default)
        => ActivePipeline.ExecuteProcedureScalarAsync<T, TArgs>(command, readBackParameterName, cancellationToken);

    /// <inheritdoc />
    public Task<T> ExecuteScalarAsync<T, TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default)
        => ActivePipeline.ExecuteScalarAsync<T, TArgs>(commandText, args, bindParameters, cancellationToken);

    /// <inheritdoc />
    public Task<T> ExecuteScalarAsync<T, TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        CancellationToken cancellationToken = default)
        => ActivePipeline.ExecuteScalarAsync<T, TArgs>(command, cancellationToken);

    /// <inheritdoc />
    public Task<IInquiryTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        // Nested call: an ambient transaction already exists on this async flow. Don't open a
        // second physical transaction; create a savepoint on the existing one. The supplied
        // isolation level is ignored — a savepoint inherits its outer transaction's isolation
        // (you can't change isolation mid-transaction in any provider we support).
        var ambient = _ambientSlot.Value?.Pipeline;
        if (ambient is not null)
        {
            return BeginSavepointAsync(ambient, _ambientSlot.Value!.IsolationLevel, cancellationToken);
        }

        // Install the slot *synchronously* (before any await) so the caller's async
        // control flow sees it. The Pipeline field is mutated later, after the
        // connection + transaction are open — the caller observes that mutation via
        // the shared slot reference.
        var slot = new AmbientTransactionSlot { IsolationLevel = isolationLevel };
        _ambientSlot.Value = slot;

        return BeginTransactionCoreAsync(slot, isolationLevel, cancellationToken);
    }

    private async Task<IInquiryTransaction> BeginTransactionCoreAsync(
        AmbientTransactionSlot slot,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        DbConnection? connection = null;
        try
        {
            connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
            var pipeline = new TransactedInquiryRequestPipeline(connection, transaction, _interceptors, _connectionFactory, _options);
            slot.Pipeline = pipeline;
            slot.State = AmbientTransactionSlotState.Active;
            return new InquiryTransaction(
                connection,
                transaction,
                pipeline,
                this,
                onDetach: () =>
                {
                    if (ReferenceEquals(_ambientSlot.Value, slot))
                    {
                        _ambientSlot.Value = null;
                    }
                },
                onClose: () =>
                {
                    slot.Pipeline = null;
                    slot.State = AmbientTransactionSlotState.Closed;
                });
        }
        catch
        {
            // Leave the slot installed but empty — Pipeline == null falls through to the
            // default pipeline and the user can begin a fresh transaction (a new slot will
            // replace this one synchronously next time).
            slot.Pipeline = null;
            slot.State = AmbientTransactionSlotState.Detached;
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    private async Task<IInquiryTransaction> BeginSavepointAsync(
        TransactedInquiryRequestPipeline outer,
        IsolationLevel inheritedIsolation,
        CancellationToken cancellationToken)
    {
        var name = "inquiry_sp_" + System.Threading.Interlocked.Increment(ref _savepointCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            await outer.SaveSavepointAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (NotSupportedException inner)
        {
            // The provider's DbTransaction does not implement savepoints (DbTransaction.Save). Wrap
            // with a clearer, actionable message rather than letting the bare provider exception
            // surface. Use top-level transactions instead, or switch to a provider/version that
            // supports savepoints.
            throw new NotSupportedException(
                "The current ADO.NET provider does not implement savepoints (DbTransaction.Save), " +
                "so Inquiry cannot create a nested transaction here. Use a top-level transaction " +
                "(IInquiry.BeginTransactionAsync without an ambient one), or use a provider/version " +
                "that supports savepoints.",
                inner);
        }
        return new SavepointInquiryTransaction(this, outer, name, inheritedIsolation);
    }

    private IInquiryEntityMaterializer<TEntity> GetMaterializer<TEntity>()
        where TEntity : class
        => _serviceProvider.GetRequiredService<IInquiryEntityMaterializer<TEntity>>();
}
