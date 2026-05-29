using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Parameters;
using Inquiry.Pipeline;
using Inquiry.Transactions;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;

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
/// The slot is cleared on transaction commit, rollback, or dispose; straggler async work that
/// fires after the transaction has been closed silently falls back to the default pipeline.
/// </para>
/// </remarks>
public sealed class DefaultInquiry : IInquiry
{
    private readonly IInquiryRequestPipeline _defaultPipeline;
    private readonly IInquiryConnectionFactory _connectionFactory;
    private readonly IInquiryCommandInterceptor[] _interceptors;
    private readonly IServiceProvider _serviceProvider;

    // The slot stores a *holder* rather than the pipeline directly. Setting an AsyncLocal
    // value from inside an async callee does not propagate back to the caller (see
    // https://learn.microsoft.com/dotnet/api/system.threading.asynclocal-1), so we install
    // the holder synchronously at the top of BeginTransactionAsync — before any await —
    // and mutate the holder's Pipeline field once the connection/transaction is open.
    // The caller's async context already references the same holder, so the mutation is
    // visible across the await boundary.
    private readonly AsyncLocal<AmbientTransactionSlot?> _ambientSlot = new();

    private sealed class AmbientTransactionSlot
    {
        public TransactedInquiryRequestPipeline? Pipeline;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInquiry"/> class.
    /// </summary>
    public DefaultInquiry(
        IInquiryRequestPipeline requestPipeline,
        IInquiryConnectionFactory connectionFactory,
        IEnumerable<IInquiryCommandInterceptor> interceptors,
        IServiceProvider serviceProvider)
    {
        _defaultPipeline = requestPipeline ?? throw new ArgumentNullException(nameof(requestPipeline));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _interceptors = interceptors?.ToArray() ?? throw new ArgumentNullException(nameof(interceptors));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    private IInquiryRequestPipeline ActivePipeline => _ambientSlot.Value?.Pipeline ?? _defaultPipeline;

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => QueryAsync<TEntity>(new InquiryCommand(commandText), cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => QueryAsync<TEntity>(new InquiryCommand(commandText, InquiryParameterReader.Read(parameters)), cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => ActivePipeline.QueryAsync(command, GetMaterializer<TEntity>(), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => QueryListAsync<TEntity>(new InquiryCommand(commandText), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => QueryListAsync<TEntity>(new InquiryCommand(commandText, InquiryParameterReader.Read(parameters)), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => ActivePipeline.QueryListAsync(command, GetMaterializer<TEntity>(), cancellationToken);

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => QuerySingleOrDefaultAsync<TEntity>(new InquiryCommand(commandText), cancellationToken);

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => QuerySingleOrDefaultAsync<TEntity>(new InquiryCommand(commandText, InquiryParameterReader.Read(parameters)), cancellationToken);

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
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QueryListAsync<TEntity, TMaterializer>(command, materializer, cancellationToken);

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => ActivePipeline.QuerySingleOrDefaultAsync<TEntity, TMaterializer>(command, materializer, cancellationToken);

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
    public Task<int> ExecuteAsync(
        string commandText,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(new InquiryCommand(commandText), cancellationToken);

    /// <inheritdoc />
    public Task<int> ExecuteAsync(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(new InquiryCommand(commandText, InquiryParameterReader.Read(parameters)), cancellationToken);

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
    public Task<IInquiryTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        if (_ambientSlot.Value?.Pipeline is not null)
        {
            throw new InvalidOperationException("Nested transactions are not supported by Inquiry.");
        }

        // Install the slot *synchronously* (before any await) so the caller's async
        // control flow sees it. The Pipeline field is mutated later, after the
        // connection + transaction are open — the caller observes that mutation via
        // the shared slot reference.
        var slot = new AmbientTransactionSlot();
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
            slot.Pipeline = new TransactedInquiryRequestPipeline(connection, transaction, _interceptors);
            return new InquiryTransaction(connection, transaction, this, onClose: () => slot.Pipeline = null);
        }
        catch
        {
            // Leave the slot installed but empty — Pipeline == null falls through to the
            // default pipeline and the user can begin a fresh transaction (a new slot will
            // replace this one synchronously next time).
            slot.Pipeline = null;
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    private IInquiryEntityMaterializer<TEntity> GetMaterializer<TEntity>()
        where TEntity : class
        => _serviceProvider.GetRequiredService<IInquiryEntityMaterializer<TEntity>>();
}
