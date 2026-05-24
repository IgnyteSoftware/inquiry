using System.Data.Common;

namespace Inquiry;

public sealed partial class InquiryRequestPipeline
{
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
