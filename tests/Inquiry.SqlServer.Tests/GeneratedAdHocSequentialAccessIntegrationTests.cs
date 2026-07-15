using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

[InquiryAdHoc]
public sealed class SqlServerSequentialAdHocRow
{
    public int Id { get; set; }
    public int C01 { get; set; }
    public int C02 { get; set; }
    public int C03 { get; set; }
    public int C04 { get; set; }
    public int C05 { get; set; }
    public int C06 { get; set; }
    public int C07 { get; set; }
    public int C08 { get; set; }
    public int C09 { get; set; }
    public int C10 { get; set; }
    public int C11 { get; set; }
    public int C12 { get; set; }
    public byte[] Payload { get; set; } = [];
}

[Collection(SqlServerCollection.Name)]
public sealed class GeneratedAdHocSequentialAccessIntegrationTests
{
    private const int RowCount = 8;
    private const int PayloadSize = 128 * 1024;
    private const string SelectSql =
        "SELECT [Id], [C01], [C02], [C03], [C04], [C05], [C06], [C07], [C08], [C09], [C10], [C11], [C12], [Payload] " +
        "FROM [SequentialAdHocRow] ORDER BY [Id]";
    private const string Ddl = """
        CREATE TABLE [SequentialAdHocRow] (
            [Id] INT NOT NULL PRIMARY KEY,
            [C01] INT NOT NULL, [C02] INT NOT NULL, [C03] INT NOT NULL,
            [C04] INT NOT NULL, [C05] INT NOT NULL, [C06] INT NOT NULL,
            [C07] INT NOT NULL, [C08] INT NOT NULL, [C09] INT NOT NULL,
            [C10] INT NOT NULL, [C11] INT NOT NULL, [C12] INT NOT NULL,
            [Payload] VARBINARY(MAX) NOT NULL);
        """;

    private readonly SqlServerContainerFixture _fixture;

    public GeneratedAdHocSequentialAccessIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GeneratedAdHocWideRowsStreamAndReleaseResources()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "seqadhoc");
        await SeedAsync(harness.ConnectionString);
        var inquiry = harness.GetRequiredService<IInquiry>();

        var buffered = await inquiry.QueryListAsync<SqlServerSequentialAdHocRow>(new Inquiry.Commands.InquiryCommand(SelectSql));
        AssertRows(buffered);

        var streamed = new List<SqlServerSequentialAdHocRow>();
        await foreach (var row in inquiry.QueryAsync<SqlServerSequentialAdHocRow>(new Inquiry.Commands.InquiryCommand(SelectSql)))
        {
            streamed.Add(row);
        }
        AssertRows(streamed);

        var firstOnly = 0;
        await foreach (var row in inquiry.QueryAsync<SqlServerSequentialAdHocRow>(new Inquiry.Commands.InquiryCommand(SelectSql)))
        {
            AssertRow(row, 1);
            firstOnly++;
            break;
        }
        Assert.Equal(1, firstOnly);
        Assert.Equal(RowCount, await inquiry.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [SequentialAdHocRow]"));

        await using var transaction = await inquiry.BeginTransactionAsync();
        await foreach (var row in transaction.QueryAsync<SqlServerSequentialAdHocRow>(new Inquiry.Commands.InquiryCommand(SelectSql)))
        {
            AssertRow(row, 1);
            break;
        }
        Assert.Equal(RowCount, await transaction.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [SequentialAdHocRow]"));
        await transaction.CommitAsync();
    }

    private static async Task SeedAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        for (var id = 1; id <= RowCount; id++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO [SequentialAdHocRow] ([Id], [C01], [C02], [C03], [C04], [C05], [C06], [C07], [C08], [C09], [C10], [C11], [C12], [Payload]) " +
                "VALUES (@Id, @C01, @C02, @C03, @C04, @C05, @C06, @C07, @C08, @C09, @C10, @C11, @C12, @Payload)";
            AddParameters(command, id);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static void AddParameters(SqlCommand command, int id)
    {
        command.Parameters.AddWithValue("@Id", id);
        for (var ordinal = 1; ordinal <= 12; ordinal++)
        {
            command.Parameters.AddWithValue($"@C{ordinal:00}", id * 100 + ordinal);
        }
        command.Parameters.AddWithValue("@Payload", CreatePayload(id));
    }

    private static byte[] CreatePayload(int id)
    {
        var payload = new byte[PayloadSize];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)((id * 31 + index) & 0xff);
        }
        return payload;
    }

    private static void AssertRows(IReadOnlyList<SqlServerSequentialAdHocRow> rows)
    {
        Assert.Equal(RowCount, rows.Count);
        for (var i = 0; i < rows.Count; i++) AssertRow(rows[i], i + 1);
    }

    private static void AssertRow(SqlServerSequentialAdHocRow row, int id)
    {
        Assert.Equal(id, row.Id);
        Assert.Equal(id * 100 + 1, row.C01);
        Assert.Equal(id * 100 + 2, row.C02);
        Assert.Equal(id * 100 + 3, row.C03);
        Assert.Equal(id * 100 + 4, row.C04);
        Assert.Equal(id * 100 + 5, row.C05);
        Assert.Equal(id * 100 + 6, row.C06);
        Assert.Equal(id * 100 + 7, row.C07);
        Assert.Equal(id * 100 + 8, row.C08);
        Assert.Equal(id * 100 + 9, row.C09);
        Assert.Equal(id * 100 + 10, row.C10);
        Assert.Equal(id * 100 + 11, row.C11);
        Assert.Equal(id * 100 + 12, row.C12);
        Assert.Equal(CreatePayload(id), row.Payload);
    }
}
