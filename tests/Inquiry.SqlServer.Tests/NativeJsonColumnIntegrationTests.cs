using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

public sealed class SqlServerJsonSettings
{
    public string Theme { get; set; } = "light";
    public SqlServerJsonAddress? Address { get; set; }
}

public sealed class SqlServerJsonAddress
{
    public string City { get; set; } = "";
}

/// <summary>
/// Mirrors the shared <c>NativeJsonDoc</c> fixture without <c>SqlType = "JSON"</c>. On SQL Server the override
/// changes only the generated DDL, and the generated-schema test runs on SQL Server 2022, which has no json type.
/// </summary>
[InquiryTable("SqlServerJsonDoc")]
public sealed class SqlServerJsonDoc
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn, InquiryJson]
    public SqlServerJsonSettings? Settings { get; set; }

    [InquiryColumn]
    public string? Payload { get; set; }

    public static SqlServerJsonDoc Create(int id, string theme, string city)
        => new()
        {
            Id = id,
            Settings = new SqlServerJsonSettings { Theme = theme, Address = new SqlServerJsonAddress { City = city } },
            Payload = $$$"""{"Theme":"{{{theme}}}","Address":{"City":"{{{city}}}"}}""",
        };
}

public partial class SqlServerJsonDocStore : InquiryStore<SqlServerJsonDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(SqlServerJsonDoc doc, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAllAsync(IEnumerable<SqlServerJsonDoc> docs, CancellationToken cancellationToken = default);

    [InquiryBulkInsert]
    public partial Task<long> BulkInsertAsync(IEnumerable<SqlServerJsonDoc> docs, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(SqlServerJsonDoc doc, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<int> UpdateAllAsync(IEnumerable<SqlServerJsonDoc> docs, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquiryWhere(nameof(SqlServerJsonDoc.Id))]
    public partial Task<int> SetSettingsAsync(SqlServerJsonSettings? settings, int id, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public partial Task<int> UpsertAsync(SqlServerJsonDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<SqlServerJsonDoc?> GetAsync(int id, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(SqlServerJsonDoc.Payload), Compare.Equal, JsonPath = "$.Address.City")]
    public partial Task<IReadOnlyList<SqlServerJsonDoc>> ByPayloadCityAsync(string city, CancellationToken cancellationToken = default);
}

/// <summary>
/// An <c>[InquiryJson]</c> property and a plain string property stored in native json columns on real SQL Server
/// 2025 round-trip through every write path and a JSON-path predicate. The tests skip on servers without the type.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class NativeJsonColumnIntegrationTests
{
    private const string Ddl = """
        CREATE TABLE SqlServerJsonDoc (Id INT NOT NULL PRIMARY KEY, Settings JSON NULL, Payload JSON NULL);
        """;

    private readonly SqlServerContainerFixture _fixture;
    public NativeJsonColumnIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SingleRowWritesAndPredicatesRoundTripNativeJsonColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        Skip.IfNot(await SupportsJsonTypeAsync(), "SQL Server's native json type requires SQL Server 2025 or Azure SQL.");
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "nativejson");
        var store = harness.GetRequiredService<SqlServerJsonDocStore>();

        Assert.Equal(1, await store.InsertAsync(SqlServerJsonDoc.Create(1, "dark", "Boston")));
        Assert.Equal(1, await store.UpsertAsync(SqlServerJsonDoc.Create(2, "light", "Boston")));
        AssertDoc(await store.GetAsync(1), "dark", "Boston");
        AssertDoc(await store.GetAsync(2), "light", "Boston");

        Assert.True(await store.UpdateAsync(SqlServerJsonDoc.Create(1, "solar", "Austin")));
        await store.UpsertAsync(SqlServerJsonDoc.Create(2, "lunar", "Denver"));
        AssertDoc(await store.GetAsync(1), "solar", "Austin");
        AssertDoc(await store.GetAsync(2), "lunar", "Denver");

        Assert.Equal(1, await store.SetSettingsAsync(new SqlServerJsonSettings { Theme = "dim" }, 2));
        Assert.Equal("dim", (await store.GetAsync(2))?.Settings?.Theme);
        Assert.Equal([1], (await store.ByPayloadCityAsync("Austin")).Select(static d => d.Id));

        Assert.True(await store.UpdateAsync(new SqlServerJsonDoc { Id = 1 }));
        var cleared = await store.GetAsync(1);
        Assert.NotNull(cleared);
        Assert.Null(cleared.Settings);
        Assert.Null(cleared.Payload);
    }

    [SkippableFact]
    public async Task BatchAndBulkWritesRoundTripNativeJsonColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        Skip.IfNot(await SupportsJsonTypeAsync(), "SQL Server's native json type requires SQL Server 2025 or Azure SQL.");
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "nativejsonbatch");
        var store = harness.GetRequiredService<SqlServerJsonDocStore>();

        Assert.Equal(2, await store.InsertAllAsync([SqlServerJsonDoc.Create(1, "dark", "Boston"), SqlServerJsonDoc.Create(2, "light", "Denver")]));
        Assert.Equal(2, await store.BulkInsertAsync([SqlServerJsonDoc.Create(3, "dim", "Austin"), new SqlServerJsonDoc { Id = 4 }]));
        Assert.Equal(2, await store.UpdateAllAsync([SqlServerJsonDoc.Create(1, "solar", "Reno"), SqlServerJsonDoc.Create(2, "lunar", "Omaha")]));

        AssertDoc(await store.GetAsync(1), "solar", "Reno");
        AssertDoc(await store.GetAsync(2), "lunar", "Omaha");
        AssertDoc(await store.GetAsync(3), "dim", "Austin");
        var empty = await store.GetAsync(4);
        Assert.NotNull(empty);
        Assert.Null(empty.Settings);
        Assert.Null(empty.Payload);
    }

    private async Task<bool> SupportsJsonTypeAsync()
    {
        await using var connection = new SqlConnection(_fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT TYPE_ID('json')", connection);
        return await command.ExecuteScalarAsync() is not (null or DBNull);
    }

    private static void AssertDoc(SqlServerJsonDoc? doc, string theme, string city)
    {
        Assert.NotNull(doc);
        Assert.Equal(theme, doc.Settings?.Theme);
        Assert.Equal(city, doc.Settings?.Address?.City);
        Assert.Contains(city, doc.Payload, StringComparison.Ordinal);
    }
}
