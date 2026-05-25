using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Parameters;
using Inquiry.Pipeline;
using Inquiry.Transactions;
using Microsoft.Extensions.DependencyInjection;
using System.Data;

namespace Inquiry;

/// <summary>
/// Default implementation of the high-level Inquiry facade.
/// </summary>
public sealed class DefaultInquiry : IInquiry
{
    private readonly IInquiryRequestPipeline _requestPipeline;
    private readonly IInquiryConnectionFactory _connectionFactory;
    private readonly IInquiryCommandInterceptor[] _interceptors;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInquiry"/> class.
    /// </summary>
    public DefaultInquiry(
        IInquiryRequestPipeline requestPipeline,
        IInquiryConnectionFactory connectionFactory,
        IEnumerable<IInquiryCommandInterceptor> interceptors,
        IServiceProvider serviceProvider)
    {
        _requestPipeline = requestPipeline ?? throw new ArgumentNullException(nameof(requestPipeline));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _interceptors = interceptors?.ToArray() ?? throw new ArgumentNullException(nameof(interceptors));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return QueryAsync<TEntity>(new InquiryCommand(commandText), cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return QueryAsync<TEntity>(CreateCommand(commandText, parameters), cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        var materializer = GetMaterializer<TEntity>();
        return _requestPipeline.QueryAsync(command, materializer.Materialize, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return QuerySingleOrDefaultAsync<TEntity>(new InquiryCommand(commandText), cancellationToken);
    }

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return QuerySingleOrDefaultAsync<TEntity>(CreateCommand(commandText, parameters), cancellationToken);
    }

    /// <inheritdoc />
    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        var materializer = GetMaterializer<TEntity>();
        return _requestPipeline.QuerySingleOrDefaultAsync(command, materializer.Materialize, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(string commandText, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(new InquiryCommand(commandText), cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(CreateCommand(commandText, parameters), cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(InquiryCommand command, CancellationToken cancellationToken = default)
    {
        return _requestPipeline.ExecuteAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IInquiryTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
            var transactedPipeline = new TransactedInquiryRequestPipeline(connection, transaction, _interceptors);
            var transactedInquiry = new TransactedInquiry(transactedPipeline, _serviceProvider);
            return new InquiryTransaction(connection, transaction, transactedInquiry);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static InquiryCommand CreateCommand(string commandText, object? parameters)
    {
        return new InquiryCommand(commandText, InquiryParameterReader.Read(parameters));
    }

    private IInquiryEntityMaterializer<TEntity> GetMaterializer<TEntity>()
        where TEntity : class
    {
        return _serviceProvider.GetRequiredService<IInquiryEntityMaterializer<TEntity>>();
    }
}
