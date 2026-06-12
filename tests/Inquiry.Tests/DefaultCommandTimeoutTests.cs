using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Pipeline;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Inquiry.Tests;

public sealed class DefaultCommandTimeoutTests
{
    [Fact]
    public async Task DefaultTimeoutAppliesToCommandsWithoutExplicitTimeout()
    {
        var (pipeline, interceptor, keeper) = await CreateAsync(new InquiryOptions { DefaultCommandTimeout = TimeSpan.FromSeconds(5) });
        await using var _ = keeper;

        await pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id) VALUES (1)"));

        Assert.Equal(5, Assert.Single(interceptor.ObservedTimeouts));
    }

    [Fact]
    public async Task ExplicitCommandTimeoutOverridesDefault()
    {
        var (pipeline, interceptor, keeper) = await CreateAsync(new InquiryOptions { DefaultCommandTimeout = TimeSpan.FromSeconds(5) });
        await using var _ = keeper;

        await pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id) VALUES (1)", commandTimeout: 42));

        Assert.Equal(42, Assert.Single(interceptor.ObservedTimeouts));
    }

    [Fact]
    public async Task DefaultTimeoutAppliesToTArgsFastPath()
    {
        var (pipeline, interceptor, keeper) = await CreateAsync(new InquiryOptions { DefaultCommandTimeout = TimeSpan.FromSeconds(7) });
        await using var _ = keeper;

        await pipeline.ExecuteAsync(
            "INSERT INTO Items (Id) VALUES (@id)",
            1,
            static (cmd, id) =>
            {
                var p = cmd.CreateParameter();
                p.ParameterName = "@id";
                p.Value = id;
                cmd.Parameters.Add(p);
            });

        Assert.Equal(7, Assert.Single(interceptor.ObservedTimeouts));
    }

    [Fact]
    public void SubSecondTimeoutIsAccepted()
    {
        var options = new InquiryOptions { DefaultCommandTimeout = TimeSpan.FromMilliseconds(250) };
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.DefaultCommandTimeout);
    }

    [Fact]
    public void NonPositiveTimeoutIsRejected()
    {
        var options = new InquiryOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DefaultCommandTimeout = TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DefaultCommandTimeout = TimeSpan.FromSeconds(-1));
    }

    private static async Task<(IInquiryRequestPipeline Pipeline, TimeoutRecordingInterceptor Interceptor, SqliteConnection Keeper)> CreateAsync(InquiryOptions options)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "InquiryTimeout_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };
        var connectionString = builder.ToString();

        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var command = keeper.CreateCommand())
        {
            command.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        var interceptor = new TimeoutRecordingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new TimeoutTestConnectionFactory(connectionString),
            new IInquiryCommandInterceptor[] { interceptor },
            options);
        return (pipeline, interceptor, keeper);
    }

    private sealed class TimeoutRecordingInterceptor : IInquiryCommandInterceptor
    {
        public List<int> ObservedTimeouts { get; } = new();

        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            ObservedTimeouts.Add(context.Command.CommandTimeout);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TimeoutTestConnectionFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;

        public TimeoutTestConnectionFactory(string connectionString) => _connectionString = connectionString;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}
