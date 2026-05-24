using Inquiry.Commands;
using Inquiry.Materialization;
using Inquiry.Parameters;
using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry;

/// <summary>
/// Default implementation of the high-level Inquiry facade.
/// </summary>
public sealed class DefaultInquiry : IInquiry
{
    private readonly IInquiryRequestPipeline _requestPipeline;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInquiry"/> class.
    /// </summary>
    public DefaultInquiry(IInquiryRequestPipeline requestPipeline, IServiceProvider serviceProvider)
    {
        _requestPipeline = requestPipeline ?? throw new ArgumentNullException(nameof(requestPipeline));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return QueryAsync<TEntity>(new InquiryCommandDefinition(commandText), cancellationToken);
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
        InquiryCommandDefinition command,
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
        return QuerySingleOrDefaultAsync<TEntity>(new InquiryCommandDefinition(commandText), cancellationToken);
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
        InquiryCommandDefinition command,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        var materializer = GetMaterializer<TEntity>();
        return _requestPipeline.QuerySingleOrDefaultAsync(command, materializer.Materialize, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(string commandText, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(new InquiryCommandDefinition(commandText), cancellationToken);
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
    public Task<int> ExecuteAsync(InquiryCommandDefinition command, CancellationToken cancellationToken = default)
    {
        return _requestPipeline.ExecuteAsync(command, cancellationToken);
    }

    private static InquiryCommandDefinition CreateCommand(string commandText, object? parameters)
    {
        return new InquiryCommandDefinition(
            commandText,
            InquiryParameterReader.Read(parameters));
    }

    private IInquiryEntityMaterializer<TEntity> GetMaterializer<TEntity>()
        where TEntity : class
    {
        return _serviceProvider.GetRequiredService<IInquiryEntityMaterializer<TEntity>>();
    }
}
