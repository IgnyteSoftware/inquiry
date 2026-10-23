using Inquiry.Entities;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.PostgreSql.Tests;

public sealed class PostgreSqlJsonPreferences
{
    public string Theme { get; set; } = "light";
    public PostgreSqlJsonAddress? Address { get; set; }
}

public sealed class PostgreSqlJsonAddress
{
    public string City { get; set; } = "";
}

[InquiryTable("PostgreSqlJsonDoc")]
public sealed class PostgreSqlJsonDoc
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(SqlType = "JSONB"), InquiryJson]
    public PostgreSqlJsonPreferences? Binary { get; set; }

    [InquiryColumn(SqlType = "json"), InquiryJson]
    public PostgreSqlJsonPreferences? Text { get; set; }

    [InquiryColumn(SqlType = "jsonb")]
    public string? RawBinary { get; set; }

    [InquiryColumn(SqlType = "JSON")]
    public string? RawText { get; set; }
}

public partial class PostgreSqlJsonDocStore : InquiryStore<PostgreSqlJsonDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(PostgreSqlJsonDoc doc, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<PostgreSqlJsonDoc?> InsertReturningAsync(PostgreSqlJsonDoc doc, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAllAsync(IEnumerable<PostgreSqlJsonDoc> docs, CancellationToken cancellationToken = default);

    [InquiryBulkInsert]
    public partial Task<long> BulkInsertAsync(IEnumerable<PostgreSqlJsonDoc> docs, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(PostgreSqlJsonDoc doc, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<int> UpdateAllAsync(IEnumerable<PostgreSqlJsonDoc> docs, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquiryWhere(nameof(PostgreSqlJsonDoc.Id))]
    public partial Task<int> SetBinaryAsync(PostgreSqlJsonPreferences? binary, string? rawBinary, int id, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquirySet(nameof(PostgreSqlJsonDoc.RawBinary), "@rawBinary")]
    [InquiryWhere(nameof(PostgreSqlJsonDoc.Id))]
    public partial Task<int> SetRawBinaryAsync(string? rawBinary, int id, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquirySet(nameof(PostgreSqlJsonDoc.RawBinary), "to_jsonb(@text)")]
    [InquiryWhere(nameof(PostgreSqlJsonDoc.Id))]
    public partial Task<int> SetRawBinaryToJsonStringAsync(string? text, int id, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public partial Task<int> UpsertAsync(PostgreSqlJsonDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<PostgreSqlJsonDoc?> GetAsync(int id, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(PostgreSqlJsonDoc.RawBinary), Compare.Equal, JsonPath = "$.Address.City")]
    public partial Task<IReadOnlyList<PostgreSqlJsonDoc>> ByRawBinaryCityAsync(string city, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(PostgreSqlJsonDoc.RawText), Compare.Equal, JsonPath = "$.Theme")]
    public partial Task<IReadOnlyList<PostgreSqlJsonDoc>> ByRawTextThemeAsync(string theme, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(PostgreSqlJsonDoc.Binary), Compare.Equal)]
    public partial Task<IReadOnlyList<PostgreSqlJsonDoc>> ByBinaryAsync(PostgreSqlJsonPreferences binary, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>[InquiryJson]</c> and plain string properties opted into a native <c>jsonb</c> or <c>json</c> column
/// through <c>SqlType</c> round-trip through every write path and through JSON predicates on real PostgreSQL.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlNativeJsonIntegrationTests
{
    private const string Ddl = """
        CREATE TABLE "PostgreSqlJsonDoc" (
            "Id" INTEGER NOT NULL PRIMARY KEY,
            "Binary" JSONB NULL,
            "Text" JSON NULL,
            "RawBinary" JSONB NULL,
            "RawText" JSON NULL
        );
        """;

    private readonly PostgreSqlContainerFixture _fixture;

    public PostgreSqlNativeJsonIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertSelectAndUpdateRoundTripNativeJsonColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "pgjsonrt");
        var store = harness.GetRequiredService<PostgreSqlJsonDocStore>();

        Assert.Equal(1, await store.InsertAsync(NewDoc(1, "dark", "Boston")));
        var returned = await store.InsertReturningAsync(NewDoc(2, "light", "Denver"));
        Assert.NotNull(returned);
        AssertDoc(returned, "light", "Denver");
        AssertDoc(await store.GetAsync(1), "dark", "Boston");

        Assert.True(await store.UpdateAsync(NewDoc(1, "solar", "Austin")));
        AssertDoc(await store.GetAsync(1), "solar", "Austin");

        Assert.Equal(1, await store.SetBinaryAsync(Preferences("lunar", "Omaha"), """{"Address":{"City":"Omaha"}}""", 2));
        var partial = await store.GetAsync(2);
        Assert.Equal("Omaha", partial?.Binary?.Address?.City);
        Assert.Equal("Denver", partial?.Text?.Address?.City);
        Assert.Single(await store.ByRawBinaryCityAsync("Omaha"));

        Assert.True(await store.UpdateAsync(new PostgreSqlJsonDoc { Id = 2 }));
        var cleared = await store.GetAsync(2);
        Assert.NotNull(cleared);
        Assert.Null(cleared.Binary);
        Assert.Null(cleared.Text);
        Assert.Null(cleared.RawBinary);
        Assert.Null(cleared.RawText);
    }

    [SkippableFact]
    public async Task UpsertInsertsThenUpdatesNativeJsonColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "pgjsonups");
        var store = harness.GetRequiredService<PostgreSqlJsonDocStore>();

        Assert.Equal(1, await store.UpsertAsync(NewDoc(1, "dark", "Boston")));
        AssertDoc(await store.GetAsync(1), "dark", "Boston");
        Assert.Equal(1, await store.UpsertAsync(NewDoc(1, "solar", "Austin")));
        AssertDoc(await store.GetAsync(1), "solar", "Austin");
    }

    [SkippableFact]
    public async Task BatchWritesRoundTripNativeJsonColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "pgjsonbatch");
        var store = harness.GetRequiredService<PostgreSqlJsonDocStore>();

        Assert.Equal(2, await store.InsertAllAsync([NewDoc(1, "dark", "Boston"), NewDoc(2, "light", "Denver")]));
        AssertDoc(await store.GetAsync(1), "dark", "Boston");
        AssertDoc(await store.GetAsync(2), "light", "Denver");

        Assert.Equal(2, await store.UpdateAllAsync([NewDoc(1, "solar", "Reno"), NewDoc(2, "lunar", "Omaha")]));
        AssertDoc(await store.GetAsync(1), "solar", "Reno");
        AssertDoc(await store.GetAsync(2), "lunar", "Omaha");
    }

    [SkippableFact]
    public async Task BulkInsertCopiesNativeJsonColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "pgjsonbulk");
        var store = harness.GetRequiredService<PostgreSqlJsonDocStore>();

        Assert.Equal(2, await store.BulkInsertAsync([NewDoc(1, "dim", "Austin"), new PostgreSqlJsonDoc { Id = 2 }]));
        AssertDoc(await store.GetAsync(1), "dim", "Austin");
        var empty = await store.GetAsync(2);
        Assert.NotNull(empty);
        Assert.Null(empty.Binary);
        Assert.Null(empty.Text);
        Assert.Null(empty.RawBinary);
        Assert.Null(empty.RawText);
        Assert.Single(await store.ByRawBinaryCityAsync("Austin"));
    }

    [SkippableFact]
    public async Task SetExpressionsBindBareAssignmentsAsJsonAndOperandsAsText()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "pgjsonset");
        var store = harness.GetRequiredService<PostgreSqlJsonDocStore>();
        await store.InsertAsync(NewDoc(1, "dark", "Boston"));

        Assert.Equal(1, await store.SetRawBinaryAsync("""{"Address":{"City":"Reno"}}""", 1));
        Assert.Single(await store.ByRawBinaryCityAsync("Reno"));

        Assert.Equal(1, await store.SetRawBinaryToJsonStringAsync("hello", 1));
        Assert.Equal("\"hello\"", (await store.GetAsync(1))?.RawBinary);
    }

    [SkippableFact]
    public async Task PredicatesFilterNativeJsonColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "pgjsonpred");
        var store = harness.GetRequiredService<PostgreSqlJsonDocStore>();
        await store.InsertAllAsync([NewDoc(1, "dark", "Boston"), NewDoc(2, "light", "Boston"), NewDoc(3, "dark", "Denver")]);

        Assert.Equal([1, 2], (await store.ByRawBinaryCityAsync("Boston")).Select(static d => d.Id).Order());
        Assert.Equal([1, 3], (await store.ByRawTextThemeAsync("dark")).Select(static d => d.Id).Order());
        var match = Assert.Single(await store.ByBinaryAsync(Preferences("light", "Boston")));
        Assert.Equal(2, match.Id);
    }

    private static PostgreSqlJsonDoc NewDoc(int id, string theme, string city)
    {
        var raw = $$$"""{"Theme":"{{{theme}}}","Address":{"City":"{{{city}}}"}}""";
        return new() { Id = id, Binary = Preferences(theme, city), Text = Preferences(theme, city), RawBinary = raw, RawText = raw };
    }

    private static PostgreSqlJsonPreferences Preferences(string theme, string city)
        => new() { Theme = theme, Address = new PostgreSqlJsonAddress { City = city } };

    private static void AssertDoc(PostgreSqlJsonDoc? doc, string theme, string city)
    {
        Assert.NotNull(doc);
        Assert.NotNull(doc.Binary);
        Assert.NotNull(doc.Text);
        Assert.Equal(theme, doc.Binary.Theme);
        Assert.Equal(city, doc.Binary.Address?.City);
        Assert.Equal(theme, doc.Text.Theme);
        Assert.Equal(city, doc.Text.Address?.City);
        Assert.Contains(city, doc.RawBinary, StringComparison.Ordinal);
        Assert.Contains(city, doc.RawText, StringComparison.Ordinal);
    }
}
