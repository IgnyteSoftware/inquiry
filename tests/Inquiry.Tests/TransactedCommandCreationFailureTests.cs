using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Pipeline;
using System.Data;
using System.Data.Common;

namespace Inquiry.Tests;

/// <summary>
/// The transacted pipeline enlists each freshly created command in the ambient transaction. A
/// provider whose <c>Transaction</c> setter throws must not leak that command: the assignment has to
/// happen inside the guarded region whose finally releases the command resources.
/// </summary>
public sealed class TransactedCommandCreationFailureTests
{
    [Fact]
    public async Task ExecuteDisposesCommandWhenTransactionAssignmentFails()
        => await AssertCommandDisposedAsync(pipeline =>
            pipeline.ExecuteAsync(new InquiryCommand("UPDATE T SET X = 1")));

    [Fact]
    public async Task ExecuteScalarDisposesCommandWhenTransactionAssignmentFails()
        => await AssertCommandDisposedAsync(pipeline =>
            pipeline.ExecuteScalarAsync<int>(new InquiryCommand("SELECT 1")));

    [Fact]
    public async Task QueryListDisposesCommandWhenTransactionAssignmentFails()
        => await AssertCommandDisposedAsync(pipeline =>
            pipeline.QueryListAsync(new InquiryCommand("SELECT 1"), new ThrowingMaterializer()));

    [Fact]
    public async Task QuerySingleOrDefaultDisposesCommandWhenTransactionAssignmentFails()
        => await AssertCommandDisposedAsync(pipeline =>
            pipeline.QuerySingleOrDefaultAsync(new InquiryCommand("SELECT 1"), new ThrowingMaterializer()));

    [Fact]
    public async Task ExecuteProcedureScalarDisposesCommandWhenTransactionAssignmentFails()
        => await AssertCommandDisposedAsync(pipeline =>
            pipeline.ExecuteProcedureScalarAsync<int>(
                new InquiryCommand("proc", CommandType.StoredProcedure),
                "@out"));

    private static async Task AssertCommandDisposedAsync(Func<TransactedInquiryRequestPipeline, Task> operation)
    {
        var command = new TransactionRejectingCommand();
        var connection = new StubConnection(command);
        var transaction = new StubTransaction(connection);
        var pipeline = new TransactedInquiryRequestPipeline(
            connection,
            transaction,
            Array.Empty<IInquiryCommandInterceptor>(),
            new StubConnectionFactory(),
            options: null);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => operation(pipeline));

        Assert.Equal(TransactionRejectingCommand.FailureMessage, failure.Message);
        Assert.Equal(1, command.DisposeCount);
    }

    private sealed class ThrowingMaterializer : IInquiryEntityMaterializer<object>
    {
        public object Materialize(DbDataReader reader) => throw new NotSupportedException();
    }

    private sealed class StubConnectionFactory : IInquiryConnectionFactory
    {
        public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubConnection : DbConnection
    {
        private readonly DbCommand _command;

        public StubConnection(DbCommand command) => _command = command;

        protected override DbCommand CreateDbCommand() => _command;

        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    }

    private sealed class StubTransaction : DbTransaction
    {
        private readonly DbConnection _connection;

        public StubTransaction(DbConnection connection) => _connection = connection;

        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => _connection;
        public override void Commit() { }
        public override void Rollback() { }
    }

    private sealed class TransactionRejectingCommand : DbCommand
    {
        internal const string FailureMessage = "this provider rejects transaction enlistment";

        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
        }

        protected override DbTransaction? DbTransaction
        {
            get => null;
            set => throw new InvalidOperationException(FailureMessage);
        }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new StubParameterCollection();
        public override void Cancel() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object? ExecuteScalar() => throw new NotSupportedException();
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new StubParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class StubParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class StubParameterCollection : DbParameterCollection
    {
        private readonly List<object> _items = new();
        public override int Count => _items.Count;
        public override object SyncRoot => _items;
        public override int Add(object value) { _items.Add(value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var value in values) _items.Add(value); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains(value);
        public override bool Contains(string value) => false;
        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf(value);
        public override int IndexOf(string parameterName) => -1;
        public override void Insert(int index, object value) => _items.Insert(index, value);
        public override void Remove(object value) => _items.Remove(value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) { }
        protected override DbParameter GetParameter(int index) => (DbParameter)_items[index];
        protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) { }
    }
}
