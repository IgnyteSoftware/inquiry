using System.Data.Common;

namespace Inquiry;

internal sealed class InquiryTransaction : IInquiryTransaction
{
    private readonly DbTransaction _transaction;
    private readonly DbConnection _connection;
    private readonly bool _disposeConnection;
    private bool _completed;

    public InquiryTransaction(IInquiryClient client, DbTransaction transaction, DbConnection connection, bool disposeConnection)
    {
        Client = client;
        _transaction = transaction;
        _connection = connection;
        _disposeConnection = disposeConnection;
    }

    public IInquiryClient Client { get; }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public void Dispose()
    {
        if (!_completed)
        {
            _transaction.Rollback();
        }

        _transaction.Dispose();
        if (_disposeConnection)
        {
            _connection.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            await _transaction.RollbackAsync().ConfigureAwait(false);
        }

        await _transaction.DisposeAsync().ConfigureAwait(false);
        if (_disposeConnection)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
