using Inquiry.Entities;
using Inquiry.Generated;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

// The facets here are unique in this assembly, so the artifact assertions see only these columns.
[InquiryTable("JsonInItem")]
public sealed class JsonInItem
{
    [InquiryKey] public int Id { get; set; }
    [InquiryColumn] public long Big { get; set; }
    [InquiryColumn(Length = 23)] public string CaseSensitiveCode { get; set; } = string.Empty;
    [InquiryColumn(Length = 23)] public string CaseInsensitiveCode { get; set; } = string.Empty;
    [InquiryColumn(SqlType = "VARCHAR(29)")] public string AnsiCode { get; set; } = string.Empty;
    [InquiryColumn] public Guid ExternalId { get; set; }
    [InquiryColumn(Scale = 3)] public DateTime OccurredAt { get; set; }
    [InquiryColumn(SqlType = "datetime")] public DateTime LegacyAt { get; set; }
    [InquiryColumn(Precision = 21, Scale = 5)] public decimal Amount { get; set; }
    [InquiryColumn(Scale = 4)] public DateTimeOffset CapturedAt { get; set; }
    [InquiryColumn(Scale = 3)] public TimeOnly LocalTime { get; set; }
    [InquiryColumn(Length = 11)] public byte[] Payload { get; set; } = [];
    [InquiryColumn] public int? OptionalNumber { get; set; }
}

public partial class JsonInItemStore : InquiryStore<JsonInItem>
{
    [InquiryCount, InquiryWhere("Id", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByIdsAsync(IReadOnlyList<int> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("Big", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByBigAsync(IReadOnlyList<long> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("CaseSensitiveCode", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByCaseSensitiveAsync(IReadOnlyList<string> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("CaseInsensitiveCode", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByCaseInsensitiveAsync(IReadOnlyList<string> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("AnsiCode", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByAnsiAsync(IReadOnlyList<string> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("ExternalId", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByGuidAsync(IReadOnlyList<Guid> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("OccurredAt", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByDateTime2Async(IReadOnlyList<DateTime> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("LegacyAt", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByLegacyDateTimeAsync(IReadOnlyList<DateTime> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("Amount", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByAmountAsync(IReadOnlyList<decimal> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("CapturedAt", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByCapturedAtAsync(IReadOnlyList<DateTimeOffset> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("LocalTime", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByTimeAsync(IReadOnlyList<TimeOnly> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("Payload", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByPayloadAsync(IReadOnlyList<byte[]> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("OptionalNumber", Compare.In, InListBinding = InListBinding.Json)]
    public partial Task<long> CountByOptionalNumberAsync(IReadOnlyList<int?> values, CancellationToken cancellationToken = default);

    [InquiryCount, InquiryWhere("Id", Compare.In, InListBinding = InListBinding.Json), InquiryWhere("Big", Compare.NotIn)]
    public partial Task<long> CountByIdsExcludingBigAsync(IReadOnlyList<int> ids, IReadOnlyList<long> excluded, CancellationToken cancellationToken = default);
}

[Collection(SqlServerCollection.Name)]
public sealed class JsonInListIntegrationTests
{
    private const string Ddl = """
        CREATE TABLE [JsonInItem]
        (
            [Id] INT NOT NULL PRIMARY KEY,
            [Big] BIGINT NOT NULL,
            [CaseSensitiveCode] NVARCHAR(23) COLLATE Latin1_General_CS_AS NOT NULL,
            [CaseInsensitiveCode] NVARCHAR(23) COLLATE Latin1_General_CI_AS NOT NULL,
            [AnsiCode] VARCHAR(29) COLLATE Latin1_General_CS_AS NOT NULL,
            [ExternalId] UNIQUEIDENTIFIER NOT NULL,
            [OccurredAt] DATETIME2(3) NOT NULL,
            [LegacyAt] DATETIME NOT NULL,
            [Amount] DECIMAL(21,5) NOT NULL,
            [CapturedAt] DATETIMEOFFSET(4) NOT NULL,
            [LocalTime] TIME(3) NOT NULL,
            [Payload] VARBINARY(11) NOT NULL,
            [OptionalNumber] INT NULL
        );
        INSERT [JsonInItem]
            ([Id], [Big], [CaseSensitiveCode], [CaseInsensitiveCode], [AnsiCode], [ExternalId], [OccurredAt], [LegacyAt],
             [Amount], [CapturedAt], [LocalTime], [Payload], [OptionalNumber])
        VALUES
            (1, 9223372036854775807, N'Alpha', N'Alpha', 'Alpha', '11111111-2222-3333-4444-555555555555',
             '2026-07-13T12:34:56.123', '2026-07-13T12:34:56.003', 1234567890123456.12345,
             '2026-07-13T12:34:56.1234+02:00', '12:34:56.123', 0x0102030405, 42),
            (2, -9223372036854775808, N'Beta', N'Beta', 'Beta', '66666666-7777-8888-9999-000000000000',
             '2026-07-14T00:00:00.000', '2026-07-14T00:00:00.000', 0.00001,
             '2026-07-14T00:00:00.0000+00:00', '00:00:00.000', 0x, NULL);
        """;

    private readonly SqlServerContainerFixture _fixture;
    public JsonInListIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task LongIntegerStringAndGuidListsRunWithoutAnyTableType()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "jsoninnotype", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<JsonInItemStore>();
        Assert.Equal(0, await CountRowsAsync(harness.ConnectionString, "SELECT COUNT(*) FROM sys.table_types"));

        var ids = Enumerable.Range(1, 1000).ToList();
        Assert.Equal(2, await store.CountByIdsAsync(ids));
        Assert.Equal(1, await store.CountByIdsAsync(Enumerable.Range(2, 1000).ToList()));
        // Short integer lists still bind as scalar parameters.
        Assert.Equal(1, await store.CountByIdsAsync([1]));
        Assert.Equal(0, await store.CountByIdsAsync([]));

        var codes = Enumerable.Range(0, 999).Select(static i => "code-" + i).Append("Beta").ToList();
        Assert.Equal(1, await store.CountByCaseInsensitiveAsync(codes));

        var guids = Enumerable.Range(0, 999).Select(static _ => Guid.NewGuid()).Append(Guid.Parse("11111111-2222-3333-4444-555555555555")).ToList();
        Assert.Equal(1, await store.CountByGuidAsync(guids));

        Assert.Equal(2, await store.CountByBigAsync([long.MaxValue, long.MinValue]));
        Assert.Equal(0, await CountRowsAsync(harness.ConnectionString, "SELECT COUNT(*) FROM sys.table_types"));
    }

    [SkippableFact]
    public async Task StringComparisonsUseTheColumnCollation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "jsonincollation", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<JsonInItemStore>();
        var longList = Enumerable.Range(0, 40).Select(static i => "x" + i).ToList();

        // The database default is case-insensitive, so only the column collation can reject "ALPHA".
        Assert.Equal(0, await store.CountByCaseSensitiveAsync([.. longList, "ALPHA"]));
        Assert.Equal(1, await store.CountByCaseSensitiveAsync([.. longList, "Alpha"]));
        Assert.Equal(1, await store.CountByCaseInsensitiveAsync([.. longList, "ALPHA"]));
        Assert.Equal(0, await store.CountByAnsiAsync(["ALPHA", "BETA"]));
        Assert.Equal(2, await store.CountByAnsiAsync(["Alpha", "Beta"]));
        // Trailing spaces compare equal under SQL Server padding rules, as with a table type.
        Assert.Equal(1, await store.CountByCaseSensitiveAsync(["Alpha  "]));
    }

    [SkippableFact]
    public async Task TypedElementsMatchTableTypeSemantics()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "jsonintyped", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<JsonInItemStore>();

        // SqlClient truncates datetime2 to the column scale; a value at .1239999 s still matches .123.
        var occurred = new DateTime(2026, 7, 13, 12, 34, 56, DateTimeKind.Utc).AddTicks(1_239_999);
        Assert.Equal(1, await store.CountByDateTime2Async([occurred]));
        Assert.Equal(0, await store.CountByDateTime2Async([occurred.AddMilliseconds(1)]));
        Assert.Equal(1, await store.CountByDateTime2Async([new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Local)]));

        // datetime keeps its 1/300 s grid: 2 ms rounds to the stored .003.
        Assert.Equal(1, await store.CountByLegacyDateTimeAsync([new DateTime(2026, 7, 13, 12, 34, 56, 2, DateTimeKind.Unspecified)]));
        Assert.Equal(0, await store.CountByLegacyDateTimeAsync([new DateTime(2026, 7, 13, 12, 34, 56, 10, DateTimeKind.Unspecified)]));

        Assert.Equal(2, await store.CountByAmountAsync([1234567890123456.12345m, 0.00001m]));
        Assert.Equal(0, await store.CountByAmountAsync([1234567890123456.12346m]));

        var captured = new DateTimeOffset(2026, 7, 13, 12, 34, 56, TimeSpan.FromHours(2)).AddTicks(1_234_999);
        Assert.Equal(1, await store.CountByCapturedAtAsync([captured]));
        // datetimeoffset compares the UTC instant.
        Assert.Equal(1, await store.CountByCapturedAtAsync([captured.ToOffset(TimeSpan.Zero)]));

        Assert.Equal(1, await store.CountByTimeAsync([new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1_239_999))]));
        Assert.Equal(2, await store.CountByPayloadAsync([[1, 2, 3, 4, 5], []]));
        Assert.Equal(0, await store.CountByPayloadAsync([[1, 2, 3, 4]]));
        Assert.Equal(1, await store.CountByOptionalNumberAsync([null, 42]));
        Assert.Equal(1, await store.CountByGuidAsync([Guid.Parse("66666666-7777-8888-9999-000000000000")]));
    }

    [SkippableFact]
    public async Task JsonInComposesWithNotIn()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "jsoninnotin", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<JsonInItemStore>();

        Assert.Equal(1, await store.CountByIdsExcludingBigAsync([1, 2], [long.MinValue]));
        Assert.Equal(2, await store.CountByIdsExcludingBigAsync([.. Enumerable.Range(1, 100)], []));
    }

    [Fact]
    public void GeneratedArtifactsNeedNoTableTypeForJsonColumns()
    {
        string[] signatures = ["nvarchar(23)", "varchar(29)", "decimal(21,5)", "datetime2(3)", "datetimeoffset(4)", "time(3)", "varbinary(11)"];
        foreach (var signature in signatures)
        {
            Assert.False(InquiryGeneratedSchema.ProviderArtifactsValidationSql.Contains("N'" + signature + "|nullable=", StringComparison.Ordinal), signature);
            Assert.False(InquiryGeneratedSchema.ProviderArtifactsDdl.Contains(signature, StringComparison.OrdinalIgnoreCase), signature);
        }
    }

    private static async Task<int> CountRowsAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
