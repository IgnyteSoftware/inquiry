using System.Data.Common;
using Inquiry.Commands;

namespace Inquiry.Pipeline;

public sealed partial class InquiryRequestPipeline
{
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
}
