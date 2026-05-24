using System.Data.Common;
using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Parameters;

namespace Inquiry.Pipeline;

/// <summary>
/// Default implementation of the Inquiry request pipeline.
/// </summary>
public sealed partial class InquiryRequestPipeline : IInquiryRequestPipeline
{
    private readonly IInquiryConnectionFactory _connectionFactory;
    private readonly IInquiryCommandInterceptor[] _interceptors;

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryRequestPipeline"/> class.
    /// </summary>
    public InquiryRequestPipeline(
        IInquiryConnectionFactory connectionFactory,
        IEnumerable<IInquiryCommandInterceptor> interceptors)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _interceptors = interceptors?.ToArray() ?? throw new ArgumentNullException(nameof(interceptors));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<T> QueryAsync<T>(
        InquiryCommandDefinition command,
        Func<DbDataReader, T> materialize,
        CancellationToken cancellationToken = default)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (materialize is null)
        {
            throw new ArgumentNullException(nameof(materialize));
        }

        return new PipelineAsyncEnumerable<T>(this, command, materialize, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        InquiryCommandDefinition command,
        Func<DbDataReader, T> materialize,
        CancellationToken cancellationToken = default)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (materialize is null)
        {
            throw new ArgumentNullException(nameof(materialize));
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();

        try
        {
            await InitializeCommandAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
            await NotifyExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);

            await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? materialize(reader)
                : default;

            await NotifyExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            await NotifyFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(InquiryCommandDefinition command, CancellationToken cancellationToken = default)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();

        try
        {
            await InitializeCommandAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
            await NotifyExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);

            var recordsAffected = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await NotifyExecutedAsync(command, dbCommand, recordsAffected, cancellationToken).ConfigureAwait(false);
            return recordsAffected;
        }
        catch (Exception exception)
        {
            await NotifyFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask InitializeCommandAsync(
        DbCommand dbCommand,
        InquiryCommandDefinition command,
        CancellationToken cancellationToken)
    {
        dbCommand.CommandText = command.CommandText;

        if (command.CommandType is not null)
        {
            dbCommand.CommandType = command.CommandType.Value;
        }

        if (command.CommandTimeout is not null)
        {
            dbCommand.CommandTimeout = command.CommandTimeout.Value;
        }

        InquiryParameterBinder.Bind(dbCommand, command.Parameters);
        var context = new InquiryCommandContext(command, dbCommand);

        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandInitializedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask NotifyExecutingAsync(
        InquiryCommandDefinition commandDefinition,
        DbCommand command,
        CancellationToken cancellationToken)
    {
        var context = new InquiryCommandContext(commandDefinition, command);

        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandExecutingAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask NotifyExecutedAsync(
        InquiryCommandDefinition commandDefinition,
        DbCommand command,
        int? recordsAffected,
        CancellationToken cancellationToken)
    {
        var context = new InquiryCommandExecutedContext(commandDefinition, command, recordsAffected);

        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandExecutedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask NotifyFailedAsync(
        InquiryCommandDefinition commandDefinition,
        DbCommand command,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var context = new InquiryCommandFailedContext(commandDefinition, command, exception);

        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandFailedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
