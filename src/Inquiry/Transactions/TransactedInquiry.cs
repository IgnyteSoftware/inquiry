using Inquiry.Commands;
using Inquiry.Materialization;
using Inquiry.Parameters;
using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using System.Data;

namespace Inquiry.Transactions;

/// <summary>
/// An <see cref="IInquiry"/> implementation backed by a transacted request pipeline.
/// Does not support nested transactions; <see cref="BeginTransactionAsync"/> throws.
/// </summary>
internal sealed class TransactedInquiry : IInquiry
{
    private readonly IInquiryRequestPipeline _pipeline;
    private readonly IServiceProvider _serviceProvider;

    internal TransactedInquiry(IInquiryRequestPipeline pipeline, IServiceProvider serviceProvider)
    {
        _pipeline = pipeline;
        _serviceProvider = serviceProvider;
    }

    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(string commandText, CancellationToken cancellationToken = default)
        where TEntity : class
        => QueryAsync<TEntity>(new InquiryCommand(commandText), cancellationToken);

    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(string commandText, object? parameters, CancellationToken cancellationToken = default)
        where TEntity : class
        => QueryAsync<TEntity>(CreateCommand(commandText, parameters), cancellationToken);

    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(InquiryCommand command, CancellationToken cancellationToken = default)
        where TEntity : class
        => _pipeline.QueryAsync(command, GetMaterializer<TEntity>().Materialize, cancellationToken);

    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(string commandText, CancellationToken cancellationToken = default)
        where TEntity : class
        => QuerySingleOrDefaultAsync<TEntity>(new InquiryCommand(commandText), cancellationToken);

    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(string commandText, object? parameters, CancellationToken cancellationToken = default)
        where TEntity : class
        => QuerySingleOrDefaultAsync<TEntity>(CreateCommand(commandText, parameters), cancellationToken);

    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(InquiryCommand command, CancellationToken cancellationToken = default)
        where TEntity : class
        => _pipeline.QuerySingleOrDefaultAsync(command, GetMaterializer<TEntity>().Materialize, cancellationToken);

    public Task<int> ExecuteAsync(string commandText, CancellationToken cancellationToken = default)
        => ExecuteAsync(new InquiryCommand(commandText), cancellationToken);

    public Task<int> ExecuteAsync(string commandText, object? parameters, CancellationToken cancellationToken = default)
        => ExecuteAsync(CreateCommand(commandText, parameters), cancellationToken);

    public Task<int> ExecuteAsync(InquiryCommand command, CancellationToken cancellationToken = default)
        => _pipeline.ExecuteAsync(command, cancellationToken);

    public Task<IInquiryTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Nested transactions are not supported by Inquiry.");

    private static InquiryCommand CreateCommand(string commandText, object? parameters)
        => new(commandText, InquiryParameterReader.Read(parameters));

    private IInquiryEntityMaterializer<TEntity> GetMaterializer<TEntity>()
        where TEntity : class
        => _serviceProvider.GetRequiredService<IInquiryEntityMaterializer<TEntity>>();
}
