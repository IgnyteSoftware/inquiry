using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Pipeline;
using Inquiry.Transactions;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;

namespace Inquiry.Tests;

public sealed class GeneratedCommandRuntimeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void ConstructorRejectsEmptyOrWhitespaceCommandText(string commandText)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new InquiryGeneratedCommand<byte>(commandText, default, static (_, _) => { }));

        Assert.Equal("commandText", exception.ParamName);
    }

    [Fact]
    public void ConstructorRejectsNullCommandTextAndBinder()
    {
        Assert.Equal(
            "commandText",
            Assert.Throws<ArgumentException>(() =>
                new InquiryGeneratedCommand<byte>(null!, default, static (_, _) => { })).ParamName);
        Assert.Equal(
            "bindParameters",
            Assert.Throws<ArgumentNullException>(() =>
                new InquiryGeneratedCommand<byte>("SELECT 1", default, null!)).ParamName);
    }

    [Fact]
    public async Task BuiltInPipelineRejectsDefaultDefinitionAcrossEveryDirectOverload()
    {
        var pipeline = new InquiryRequestPipeline(
            new SqliteConnectionFactory("Data Source=:memory:"),
            Array.Empty<IInquiryCommandInterceptor>());
        var command = default(InquiryGeneratedCommand<byte>);

        Assert.Throws<ArgumentException>(() => pipeline.QueryAsync<object, byte, NullMaterializer>(command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.QueryListAsync<object, byte, NullMaterializer>(command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.QuerySingleOrDefaultAsync<object, byte, NullMaterializer>(command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.QueryGeneratedSingleOrDefaultAsync<object, byte, NullMaterializer>(command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.ExecuteAsync(command));
        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.ExecuteScalarAsync<int, byte>(command));
        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.QueryMultipleAsync(command));
        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.ExecuteProcedureScalarAsync<int, byte>(command, "@result"));

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var transacted = new TransactedInquiryRequestPipeline(
            connection,
            transaction,
            Array.Empty<IInquiryCommandInterceptor>(),
            new SqliteConnectionFactory("Data Source=:memory:"),
            options: null);

        Assert.Throws<ArgumentException>(() => transacted.QueryAsync<object, byte, NullMaterializer>(command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => transacted.QueryListAsync<object, byte, NullMaterializer>(command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => transacted.QuerySingleOrDefaultAsync<object, byte, NullMaterializer>(command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => transacted.QueryGeneratedSingleOrDefaultAsync<object, byte, NullMaterializer>(command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => transacted.ExecuteAsync(command));
        await Assert.ThrowsAsync<ArgumentException>(() => transacted.ExecuteScalarAsync<int, byte>(command));
        await Assert.ThrowsAsync<ArgumentException>(() => transacted.QueryMultipleAsync(command));
        await Assert.ThrowsAsync<ArgumentException>(() => transacted.ExecuteProcedureScalarAsync<int, byte>(command, "@result"));
    }

    [Fact]
    public async Task DefaultInterfaceFallbackRejectsDefaultDefinitionBeforeDispatch()
    {
        IInquiry inquiry = new FallbackInquiry();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            inquiry.QueryGeneratedSingleOrDefaultAsync<object, byte, NullMaterializer>(default, default));

        Assert.Equal("commandText", exception.ParamName);
    }

    [Fact]
    public async Task BuiltInPipelineAppliesGeneratedBinderAndCommandType()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = "InquiryGeneratedCommand_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();

        var interceptor = new CommandTypeInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new SqliteConnectionFactory(connectionString),
            new[] { interceptor });
        var command = new InquiryGeneratedCommand<int>(
            "SELECT @value",
            42,
            static (dbCommand, value) =>
            {
                var parameter = dbCommand.CreateParameter();
                parameter.ParameterName = "@value";
                parameter.Value = value;
                dbCommand.Parameters.Add(parameter);
            },
            CommandType.Text);

        Assert.Equal(42, await pipeline.ExecuteScalarAsync<int, int>(command));
        Assert.Equal(CommandType.Text, interceptor.CommandType);
        Assert.Equal(42L, interceptor.ParameterValue);
    }

    [Fact]
    public async Task DefaultInterfaceFallbackPreservesDefinitionAndValidationPath()
    {
        IInquiry inquiry = new FallbackInquiry();
        var command = new InquiryGeneratedCommand<int>(
            "SELECT @value",
            17,
            static (dbCommand, value) =>
            {
                var parameter = dbCommand.CreateParameter();
                parameter.ParameterName = "@value";
                parameter.Value = value;
                dbCommand.Parameters.Add(parameter);
            });

        var result = await inquiry.QueryGeneratedSingleOrDefaultAsync<object, int, NullMaterializer>(
            command,
            default);

        Assert.Null(result);
        var fallback = Assert.IsType<FallbackInquiry>(inquiry);
        Assert.Equal("SELECT @value", fallback.CommandText);
        Assert.Equal(17L, fallback.ParameterValue);
        Assert.True(fallback.UsedValidatingSinglePath);
    }

    [Fact]
    public async Task DefaultInterfaceBatchFallbackUsesExecuteWithoutTakingTransactionOwnership()
    {
        IInquiry inquiry = new FallbackInquiry();
        var command = new InquiryBatchCommand<int>(
            "UPDATE Items SET Value = @value",
            static (target, value) =>
            {
                var parameter = target.CreateParameter();
                parameter.ParameterName = "@value";
                parameter.Value = value;
                target.AddParameter(parameter);
            });

        var affected = await inquiry.ExecuteBatchAsync(command, new[] { 11, 12, 13 });

        var fallback = Assert.IsType<FallbackInquiry>(inquiry);
        Assert.Equal(3, affected);
        Assert.Equal(3, fallback.ExecuteCallCount);
        Assert.Equal(0, fallback.BeginTransactionCallCount);
        Assert.Equal(13L, fallback.ParameterValue);
    }

    [Fact]
    public async Task DefaultInterfaceBatchFallbackPreservesExecutionFailureWhenSourceDisposeAlsoFails()
    {
        var primary = new InvalidOperationException("execute failed");
        IInquiry inquiry = new FallbackInquiry(primary);
        var command = new InquiryBatchCommand<int>(
            "UPDATE Items SET Value = @value",
            static (target, value) =>
            {
                var parameter = target.CreateParameter();
                parameter.ParameterName = "@value";
                parameter.Value = value;
                target.AddParameter(parameter);
            });

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => inquiry.ExecuteBatchAsync(command, new ThrowingDisposeEnumerable<int>([11])));

        Assert.Collection(
            exception.InnerExceptions,
            item => Assert.Same(primary, item),
            item => Assert.Equal("enumerator dispose failed", item.Message));
        Assert.Equal(0, Assert.IsType<FallbackInquiry>(inquiry).BeginTransactionCallCount);
    }

    private readonly struct NullMaterializer : IInquiryEntityMaterializer<object>
    {
        public object Materialize(DbDataReader reader) => new();
    }

    private sealed class ThrowingDisposeEnumerable<T>(IReadOnlyList<T> items) : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator() => new Enumerator(items);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator(IReadOnlyList<T> items) : IEnumerator<T>
        {
            private int _index = -1;

            public T Current => items[_index];
            object System.Collections.IEnumerator.Current => Current!;
            public bool MoveNext() => ++_index < items.Count;
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => throw new InvalidOperationException("enumerator dispose failed");
        }
    }

    private sealed class CommandTypeInterceptor : IInquiryCommandInterceptor
    {
        public CommandType? CommandType { get; private set; }
        public long? ParameterValue { get; private set; }

        public ValueTask CommandInitializedAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            CommandType = context.Command.CommandType;
            ParameterValue = Convert.ToInt64(context.Command.Parameters[0].Value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SqliteConnectionFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;

        public SqliteConnectionFactory(string connectionString) => _connectionString = connectionString;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }

    private sealed class FallbackInquiry : IInquiry
    {
        private readonly Exception? _executeException;

        internal FallbackInquiry(Exception? executeException = null) => _executeException = executeException;

        public string? CommandText { get; private set; }
        public long? ParameterValue { get; private set; }
        public bool UsedValidatingSinglePath { get; private set; }
        public int ExecuteCallCount { get; private set; }
        public int BeginTransactionCallCount { get; private set; }

        public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(InquiryCommand command, CancellationToken cancellationToken = default)
            where TEntity : class => throw new NotSupportedException();

        public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(InquiryCommand command, CancellationToken cancellationToken = default)
            where TEntity : class => throw new NotSupportedException();

        public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(InquiryCommand command, CancellationToken cancellationToken = default)
            where TEntity : class => throw new NotSupportedException();

        public IAsyncEnumerable<TEntity> QueryAsync<TEntity, TMaterializer>(InquiryCommand command, TMaterializer materializer, CancellationToken cancellationToken = default)
            where TEntity : class
            where TMaterializer : struct, IInquiryEntityMaterializer<TEntity> => throw new NotSupportedException();

        public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity, TMaterializer>(InquiryCommand command, TMaterializer materializer, CancellationToken cancellationToken = default, int capacityHint = -1)
            where TEntity : class
            where TMaterializer : struct, IInquiryEntityMaterializer<TEntity> => throw new NotSupportedException();

        public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity, TMaterializer>(InquiryCommand command, TMaterializer materializer, CancellationToken cancellationToken = default)
            where TEntity : class
            where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        {
            UsedValidatingSinglePath = true;
            CommandText = command.CommandText;
            using var dbCommand = new SqliteCommand();
            command.DbCommandBinder?.Invoke(dbCommand);
            ParameterValue = Convert.ToInt64(dbCommand.Parameters[0].Value);
            return Task.FromResult<TEntity?>(default);
        }

        public Task<int> ExecuteAsync(InquiryCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_executeException is not null) return Task.FromException<int>(_executeException);
            CommandText = command.CommandText;
            using var dbCommand = new SqliteCommand();
            command.DbCommandBinder?.Invoke(dbCommand);
            ParameterValue = Convert.ToInt64(dbCommand.Parameters[0].Value);
            ExecuteCallCount++;
            return Task.FromResult(1);
        }

        public Task<IInquiryTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
        {
            BeginTransactionCallCount++;
            throw new NotSupportedException();
        }
    }
}
