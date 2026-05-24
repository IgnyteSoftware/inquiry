using System.Data.Common;

namespace Inquiry;

/// <summary>
/// Default implementation of the Inquiry request pipeline.
/// </summary>
public sealed class InquiryRequestPipeline : IInquiryRequestPipeline
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

        command.BindParameters?.Invoke(dbCommand);
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

    private sealed class PipelineAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly InquiryRequestPipeline _pipeline;
        private readonly InquiryCommandDefinition _command;
        private readonly Func<DbDataReader, T> _materialize;
        private readonly CancellationToken _cancellationToken;

        public PipelineAsyncEnumerable(
            InquiryRequestPipeline pipeline,
            InquiryCommandDefinition command,
            Func<DbDataReader, T> materialize,
            CancellationToken cancellationToken)
        {
            _pipeline = pipeline;
            _command = command;
            _materialize = materialize;
            _cancellationToken = cancellationToken;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var linkedCancellationToken = cancellationToken == default
                ? _cancellationToken
                : cancellationToken;
            return new PipelineAsyncEnumerator<T>(_pipeline, _command, _materialize, linkedCancellationToken);
        }
    }

    private sealed class PipelineAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly InquiryRequestPipeline _pipeline;
        private readonly InquiryCommandDefinition _commandDefinition;
        private readonly Func<DbDataReader, T> _materialize;
        private readonly CancellationToken _cancellationToken;
        private DbConnection? _connection;
        private DbCommand? _command;
        private DbDataReader? _reader;
        private bool _initialized;
        private bool _completed;

        public PipelineAsyncEnumerator(
            InquiryRequestPipeline pipeline,
            InquiryCommandDefinition commandDefinition,
            Func<DbDataReader, T> materialize,
            CancellationToken cancellationToken)
        {
            _pipeline = pipeline;
            _commandDefinition = commandDefinition;
            _materialize = materialize;
            _cancellationToken = cancellationToken;
        }

        public T Current { get; private set; } = default!;

        public async ValueTask<bool> MoveNextAsync()
        {
            if (_completed)
            {
                return false;
            }

            try
            {
                if (!_initialized)
                {
                    await InitializeAsync().ConfigureAwait(false);
                }

                if (_reader is null)
                {
                    return false;
                }

                if (await _reader.ReadAsync(_cancellationToken).ConfigureAwait(false))
                {
                    Current = _materialize(_reader);
                    return true;
                }

                _completed = true;
                await _pipeline.NotifyExecutedAsync(_commandDefinition, _command!, recordsAffected: null, _cancellationToken).ConfigureAwait(false);
                await DisposeAsync().ConfigureAwait(false);
                return false;
            }
            catch (Exception exception)
            {
                if (_command is not null)
                {
                    await _pipeline.NotifyFailedAsync(_commandDefinition, _command, exception, _cancellationToken).ConfigureAwait(false);
                }

                _completed = true;
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_reader is not null)
            {
                await _reader.DisposeAsync().ConfigureAwait(false);
                _reader = null;
            }

            if (_command is not null)
            {
                await _command.DisposeAsync().ConfigureAwait(false);
                _command = null;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }

        private async ValueTask InitializeAsync()
        {
            _connection = await _pipeline._connectionFactory.OpenConnectionAsync(_cancellationToken).ConfigureAwait(false);
            _command = _connection.CreateCommand();

            await _pipeline.InitializeCommandAsync(_command, _commandDefinition, _cancellationToken).ConfigureAwait(false);
            await _pipeline.NotifyExecutingAsync(_commandDefinition, _command, _cancellationToken).ConfigureAwait(false);

            _reader = await _command.ExecuteReaderAsync(_cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
    }
}
