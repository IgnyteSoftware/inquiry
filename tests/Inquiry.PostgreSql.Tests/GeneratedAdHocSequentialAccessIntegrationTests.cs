using Inquiry.Entities;
using Inquiry.PostgreSql.Tests.Fixtures;
using Npgsql;

namespace Inquiry.PostgreSql.Tests;

[InquiryAdHoc]
public sealed class PostgreSqlSequentialAdHocRow
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

[Collection(PostgreSqlCollection.Name)]
public sealed class GeneratedAdHocSequentialAccessIntegrationTests
{
    private const int RowCount = 8;
    private const int PayloadSize = 128 * 1024;
    private const string SelectSql =
        "SELECT id, c01, c02, c03, c04, c05, c06, c07, c08, c09, c10, c11, c12, payload " +
        "FROM sequential_ad_hoc_row ORDER BY id";
    private const string Ddl = """
        CREATE TABLE sequential_ad_hoc_row (
            id integer NOT NULL PRIMARY KEY,
            c01 integer NOT NULL, c02 integer NOT NULL, c03 integer NOT NULL,
            c04 integer NOT NULL, c05 integer NOT NULL, c06 integer NOT NULL,
            c07 integer NOT NULL, c08 integer NOT NULL, c09 integer NOT NULL,
            c10 integer NOT NULL, c11 integer NOT NULL, c12 integer NOT NULL,
            payload bytea NOT NULL);
        """;

    private readonly PostgreSqlContainerFixture _fixture;

    public GeneratedAdHocSequentialAccessIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GeneratedAdHocWideRowsStreamAndReleaseResources()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "seqadhoc");
        await SeedAsync(harness.ConnectionString);
        var inquiry = harness.GetRequiredService<IInquiry>();

        var buffered = await inquiry.QueryListAsync<PostgreSqlSequentialAdHocRow>(new Inquiry.Commands.InquiryCommand(SelectSql));
        AssertRows(buffered);

        var streamed = new List<PostgreSqlSequentialAdHocRow>();
        await foreach (var row in inquiry.QueryAsync<PostgreSqlSequentialAdHocRow>(new Inquiry.Commands.InquiryCommand(SelectSql)))
        {
            streamed.Add(row);
        }
        AssertRows(streamed);

        var firstOnly = 0;
        await foreach (var row in inquiry.QueryAsync<PostgreSqlSequentialAdHocRow>(new Inquiry.Commands.InquiryCommand(SelectSql)))
        {
            AssertRow(row, 1);
            firstOnly++;
            break;
        }
        Assert.Equal(1, firstOnly);
        Assert.Equal(RowCount, await inquiry.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM sequential_ad_hoc_row"));

        await using var transaction = await inquiry.BeginTransactionAsync();
        await foreach (var row in transaction.QueryAsync<PostgreSqlSequentialAdHocRow>(new Inquiry.Commands.InquiryCommand(SelectSql)))
        {
            AssertRow(row, 1);
            break;
        }
        Assert.Equal(RowCount, await transaction.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM sequential_ad_hoc_row"));
        await transaction.CommitAsync();
    }

    private static async Task SeedAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        for (var id = 1; id <= RowCount; id++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO sequential_ad_hoc_row (id, c01, c02, c03, c04, c05, c06, c07, c08, c09, c10, c11, c12, payload) " +
                "VALUES (@Id, @C01, @C02, @C03, @C04, @C05, @C06, @C07, @C08, @C09, @C10, @C11, @C12, @Payload)";
            AddParameters(command, id);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static void AddParameters(NpgsqlCommand command, int id)
    {
        command.Parameters.AddWithValue("Id", id);
        for (var ordinal = 1; ordinal <= 12; ordinal++)
        {
            command.Parameters.AddWithValue($"C{ordinal:00}", id * 100 + ordinal);
        }
        command.Parameters.AddWithValue("Payload", CreatePayload(id));
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

    private static void AssertRows(IReadOnlyList<PostgreSqlSequentialAdHocRow> rows)
    {
        Assert.Equal(RowCount, rows.Count);
        for (var i = 0; i < rows.Count; i++) AssertRow(rows[i], i + 1);
    }

    private static void AssertRow(PostgreSqlSequentialAdHocRow row, int id)
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
