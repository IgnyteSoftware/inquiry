using Inquiry.BulkCopy;
using Inquiry.Connections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Inquiry.Tests.Shared;

/// <summary>
/// Drives a native <see cref="IInquiryBulkCopier"/> through its connection-acquisition phase without a
/// database (#330). The stub connection is not a provider connection, so the copier rejects it before
/// copying; each check observes only what happens around the open and its telemetry.
/// </summary>
internal static class BulkCopierConnectionOwnershipAssertions
{
    private static readonly InquiryBulkInsertDefinition<Row> Definition =
        new(null, "Rows", new[] { "Id" }, static (row, _) => row.Id);

    private static readonly Row[] Rows = { new(1) };

    internal static async Task ThrowingOpenTelemetryDisposesDedicatedConnectionAsync(
        Func<IInquiryConnectionFactory, IInquiryBulkCopier> createCopier)
    {
        var connection = new TrackingConnection();
        var listenerFailure = new InvalidOperationException("Telemetry listener failed.");
        var copier = createCopier(new StubConnectionFactory(_ => new ValueTask<DbConnection>(connection)));
        var context = new InquiryBulkInsertContext(
            null, null, new InquiryBulkInsertOptions(), (_, _) => throw listenerFailure, null);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => copier.BulkInsertAsync(Definition, Rows, context));

        Assert.Same(listenerFailure, thrown);
        Assert.True(connection.IsDisposed);
    }

    internal static async Task FailedOpenIsRecordedAsFailureAsync(
        Func<IInquiryConnectionFactory, IInquiryBulkCopier> createCopier)
    {
        var openFailure = new TimeoutException("Open failed.");
        var recorded = new List<Exception?>();
        var copier = createCopier(new StubConnectionFactory(_ => throw openFailure));
        var context = new InquiryBulkInsertContext(
            null, null, new InquiryBulkInsertOptions(), (_, error) => recorded.Add(error), null);

        var thrown = await Assert.ThrowsAsync<TimeoutException>(() => copier.BulkInsertAsync(Definition, Rows, context));

        Assert.Same(openFailure, thrown);
        Assert.Same(openFailure, Assert.Single(recorded));
    }

    internal static async Task FailedOpenErrorSurvivesThrowingTelemetryAsync(
        Func<IInquiryConnectionFactory, IInquiryBulkCopier> createCopier)
    {
        var openFailure = new TimeoutException("Open failed.");
        var copier = createCopier(new StubConnectionFactory(_ => throw openFailure));
        var context = new InquiryBulkInsertContext(
            null, null, new InquiryBulkInsertOptions(), (_, _) => throw new InvalidOperationException("Telemetry listener failed."), null);

        var thrown = await Assert.ThrowsAsync<TimeoutException>(() => copier.BulkInsertAsync(Definition, Rows, context));

        Assert.Same(openFailure, thrown);
    }

    internal static async Task RejectedBorrowedConnectionIsNotDisposedAsync(
        Func<IInquiryConnectionFactory, IInquiryBulkCopier> createCopier)
    {
        var borrowed = new TrackingConnection();
        var copier = createCopier(new StubConnectionFactory(_ => throw new InvalidOperationException("A borrowed connection must not open another.")));
        var context = new InquiryBulkInsertContext(borrowed, null, new InquiryBulkInsertOptions(), null, null);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => copier.BulkInsertAsync(Definition, Rows, context));

        Assert.Contains(nameof(TrackingConnection), thrown.Message);
        Assert.False(borrowed.IsDisposed);
    }

    private sealed record Row(int Id);

    private sealed class StubConnectionFactory : IInquiryConnectionFactory
    {
        private readonly Func<CancellationToken, ValueTask<DbConnection>> _open;

        public StubConnectionFactory(Func<CancellationToken, ValueTask<DbConnection>> open) => _open = open;

        public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) => _open(cancellationToken);
    }

    private sealed class TrackingConnection : DbConnection
    {
        public bool IsDisposed { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override ConnectionState State => IsDisposed ? ConnectionState.Closed : ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
