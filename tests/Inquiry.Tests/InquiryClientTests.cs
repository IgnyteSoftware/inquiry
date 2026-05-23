using System.Data;
using Inquiry.Tests.Fakes;

namespace Inquiry.Tests;

public sealed class InquiryClientTests
{
    [Fact]
    public async Task FindAsync_ExecutesSelectByKeyAndMaterializesEntity()
    {
        var id = Guid.NewGuid();
        var connection = new RecordingDbConnection();
        connection.QueueResultSet(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["email"] = "user@example.com",
            ["display_name"] = "User",
            ["version"] = 2
        });
        var client = InquiryClient.Create(connection, InquirySqliteProvider.Instance);

        var user = await client.FindAsync<ClientUser, Guid>(id);

        Assert.NotNull(user);
        Assert.Equal(id, user.Id);
        Assert.Equal("user@example.com", user.Email);
        Assert.Single(connection.Commands);
        Assert.Equal("SELECT \"id\", \"email\", \"display_name\", \"version\" FROM \"users\" WHERE \"id\" = @Id", connection.Commands[0].CommandText);
        Assert.Equal("@Id", connection.Commands[0].RecordedParameters[0].ParameterName);
        Assert.Equal(id, connection.Commands[0].RecordedParameters[0].Value);
    }

    [Fact]
    public async Task InsertAsync_ExecutesInsertWithMappedParameters()
    {
        var id = Guid.NewGuid();
        var connection = new RecordingDbConnection();
        var client = InquiryClient.Create(connection, InquirySqliteProvider.Instance);

        var rows = await client.InsertAsync(new ClientUser
        {
            Id = id,
            Email = "insert@example.com",
            DisplayName = null,
            Version = 1
        });

        Assert.Equal(1, rows);
        Assert.Equal("INSERT INTO \"users\" (\"id\", \"email\", \"display_name\", \"version\") VALUES (@Id, @Email, @DisplayName, @Version)", connection.Commands[0].CommandText);
        Assert.Equal(DBNull.Value, connection.Commands[0].RecordedParameters.Single(parameter => parameter.ParameterName == "@DisplayName").Value);
    }

    [Fact]
    public async Task Pipeline_InvokesMiddlewareInRegistrationOrder()
    {
        var calls = new List<string>();
        var connection = new RecordingDbConnection();
        var client = new InquiryClient(
            (_, _) => ValueTask.FromResult<System.Data.Common.DbConnection>(connection),
            InquirySqliteProvider.Instance,
            middleware: new IInquiryMiddleware[]
            {
                new ProbeMiddleware("outer", calls),
                new ProbeMiddleware("inner", calls)
            },
            ownsConnections: false);

        var rows = await client.ExecuteAsync("UPDATE users SET email = @email", new { email = "next@example.com" });

        Assert.Equal(1, rows);
        Assert.Equal(new[] { "outer:before", "inner:before", "inner:after", "outer:after" }, calls);
    }

    [Fact]
    public async Task QueryStoredProcedureAsync_UsesStoredProcedureCommandTypeAndMaterializesRows()
    {
        var id = Guid.NewGuid();
        var connection = new RecordingDbConnection();
        connection.QueueResultSet(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["email"] = "stored@example.com",
            ["display_name"] = "Stored",
            ["version"] = 4
        });
        var client = InquiryClient.Create(connection, InquirySqliteProvider.Instance);

        var users = await client.QueryStoredProcedureAsync<ClientUser>(
            "dbo.GetUsers",
            new { domain = "example.com" });

        Assert.Single(users);
        Assert.Equal("stored@example.com", users[0].Email);
        Assert.Equal("dbo.GetUsers", connection.Commands[0].CommandText);
        Assert.Equal(CommandType.StoredProcedure, connection.Commands[0].CommandType);
        Assert.Equal("@domain", connection.Commands[0].RecordedParameters[0].ParameterName);
        Assert.Equal("example.com", connection.Commands[0].RecordedParameters[0].Value);
    }

    [Fact]
    public async Task ExecuteStoredProcedureAsync_SupportsOutputParameters()
    {
        var connection = new RecordingDbConnection();
        connection.OutputParameterValues["@total"] = 42;
        var client = InquiryClient.Create(connection, InquirySqliteProvider.Instance);
        var total = InquiryParameter.Output("total", DbType.Int32);

        var rows = await client.ExecuteStoredProcedureAsync(
            "dbo.CountUsers",
            new[]
            {
                InquiryParameter.Input("domain", "example.com", DbType.String),
                total
            });

        Assert.Equal(1, rows);
        Assert.Equal(42, total.Value);
        Assert.Equal("dbo.CountUsers", connection.Commands[0].CommandText);
        Assert.Equal(CommandType.StoredProcedure, connection.Commands[0].CommandType);
        var output = connection.Commands[0].RecordedParameters.Single(parameter => parameter.ParameterName == "@total");
        Assert.Equal(ParameterDirection.Output, output.Direction);
        Assert.Equal(DbType.Int32, output.DbType);
    }

    [Fact]
    public async Task ExecuteStoredProcedureAsync_UsesCurrentTransactionAndPipeline()
    {
        var observed = new List<(InquiryOperation Operation, CommandType CommandType)>();
        var connection = new RecordingDbConnection();
        var client = new InquiryClient(
            (_, _) => ValueTask.FromResult<System.Data.Common.DbConnection>(connection),
            InquirySqliteProvider.Instance,
            middleware: new[] { new ContextProbeMiddleware(observed) },
            ownsConnections: false);

        await using var transaction = await client.BeginTransactionAsync();
        var rows = await transaction.Client.ExecuteStoredProcedureAsync("dbo.TouchUser", new { id = 1 });
        await transaction.CommitAsync();

        Assert.Equal(1, rows);
        Assert.NotNull(connection.Commands[0].RecordedTransaction);
        Assert.Equal((InquiryOperation.StoredProcedureExecute, CommandType.StoredProcedure), observed.Single());
    }

    [InquiryTable("users")]
    internal sealed class ClientUser
    {
        [InquiryKey]
        [InquiryColumn("id")]
        public Guid Id { get; set; }

        [InquiryColumn("email")]
        public string Email { get; set; } = string.Empty;

        [InquiryColumn("display_name")]
        public string? DisplayName { get; set; }

        [InquiryConcurrencyToken]
        [InquiryColumn("version")]
        public int Version { get; set; }
    }

    private sealed class ProbeMiddleware : IInquiryMiddleware
    {
        private readonly string _name;
        private readonly List<string> _calls;

        public ProbeMiddleware(string name, List<string> calls)
        {
            _name = name;
            _calls = calls;
        }

        public async ValueTask<InquiryResponse> InvokeAsync(
            InquiryRequestContext context,
            InquiryRequestDelegate next,
            CancellationToken cancellationToken)
        {
            _calls.Add($"{_name}:before");
            var response = await next(context);
            _calls.Add($"{_name}:after");
            return response;
        }
    }

    private sealed class ContextProbeMiddleware : IInquiryMiddleware
    {
        private readonly List<(InquiryOperation Operation, CommandType CommandType)> _observed;

        public ContextProbeMiddleware(List<(InquiryOperation Operation, CommandType CommandType)> observed)
        {
            _observed = observed;
        }

        public ValueTask<InquiryResponse> InvokeAsync(
            InquiryRequestContext context,
            InquiryRequestDelegate next,
            CancellationToken cancellationToken)
        {
            _observed.Add((context.Operation, context.CommandType));
            return next(context);
        }
    }
}
