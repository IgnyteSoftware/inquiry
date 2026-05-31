using Inquiry;
using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Pipeline;
using System.Data;
using System.Data.Common;

namespace Inquiry.Tests;

/// <summary>
/// W4: asserts <c>PrepareAsync</c> is called exactly when
/// (mode == Auto) AND (factory capability) AND (CommandType != StoredProcedure), via a fake
/// <see cref="DbCommand"/> that records preparation, plus that the factory <c>InitializeCommand</c>
/// hook (F4) fires for every created command.
/// </summary>
public sealed class PreparedStatementTests
{
    [Theory]
    // mode, capability, commandType, expectPrepare
    [InlineData(PreparedStatementMode.Auto, true, null, true)]
    [InlineData(PreparedStatementMode.None, true, null, false)]
    [InlineData(PreparedStatementMode.Auto, false, null, false)]
    [InlineData(PreparedStatementMode.None, false, null, false)]
    [InlineData(PreparedStatementMode.Auto, true, CommandType.StoredProcedure, false)]
    [InlineData(PreparedStatementMode.Auto, true, CommandType.Text, true)]
    public async Task PreparesOnlyWhenAutoAndCapableAndNotStoredProcedure(
        PreparedStatementMode mode,
        bool capability,
        CommandType? commandType,
        bool expectPrepare)
    {
        var command = new FakeDbCommand();
        var factory = new FakeConnectionFactory(command, capability);
        var options = new InquiryOptions { PrepareStatements = mode };
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>(), options);

        await pipeline.ExecuteAsync(new InquiryCommand("UPDATE T SET X = 1", commandType));

        Assert.Equal(expectPrepare, command.PrepareCalled);
        Assert.True(command.InitializeCommandHookFired);
    }

    [Fact]
    public async Task DefaultOptionsDoNotPrepare()
    {
        var command = new FakeDbCommand();
        var factory = new FakeConnectionFactory(command, capability: true);
        // No options argument => default None.
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>());

        await pipeline.ExecuteAsync(new InquiryCommand("UPDATE T SET X = 1"));

        Assert.False(command.PrepareCalled);
    }

    private sealed class FakeConnectionFactory : IInquiryConnectionFactory
    {
        private readonly FakeDbCommand _command;
        private readonly bool _capability;

        public FakeConnectionFactory(FakeDbCommand command, bool capability)
        {
            _command = command;
            _capability = capability;
        }

        public bool SupportsPersistentPreparedStatements => _capability;

        public void InitializeCommand(DbCommand command) => ((FakeDbCommand)command).InitializeCommandHookFired = true;

        public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
            => new(new FakeDbConnection(_command));
    }

    private sealed class FakeDbConnection : DbConnection
    {
        private readonly FakeDbCommand _command;
        public FakeDbConnection(FakeDbCommand command) => _command = command;

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

    private sealed class FakeDbCommand : DbCommand
    {
        public bool PrepareCalled { get; private set; }
        public bool InitializeCommandHookFired { get; set; }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => Task.FromResult(1);
        public override int ExecuteNonQuery() => 1;

        public override Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            PrepareCalled = true;
            return Task.CompletedTask;
        }

        public override void Prepare() => PrepareCalled = true;

        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeDbParameterCollection();
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override object? ExecuteScalar() => null;
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class FakeDbParameter : DbParameter
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

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<object> _items = new();
        public override int Count => _items.Count;
        public override object SyncRoot => _items;
        public override int Add(object value) { _items.Add(value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) _items.Add(v); }
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
