using Inquiry.Entities;
using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Stores;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

public readonly record struct OracleBusinessDate(DateOnly Value);

public sealed class OracleBusinessDateConverter : IInquiryValueConverter<OracleBusinessDate, DateOnly>
{
    public DateOnly ToProvider(OracleBusinessDate model) => model.Value;
    public OracleBusinessDate FromProvider(DateOnly provider) => new(provider);
}

public readonly record struct OracleExternalId(Guid Value);

public sealed class OracleExternalIdConverter : IInquiryValueConverter<OracleExternalId, Guid>
{
    public Guid ToProvider(OracleExternalId model) => model.Value;
    public OracleExternalId FromProvider(Guid provider) => new(provider);
}

public readonly record struct OracleToggle(bool Value);

public sealed class OracleToggleConverter : IInquiryValueConverter<OracleToggle, bool>
{
    public bool ToProvider(OracleToggle model) => model.Value;
    public OracleToggle FromProvider(bool provider) => new(provider);
}

[InquiryTable("ProviderReadItem")]
public sealed class ProviderReadItem
{
    [InquiryKey(IsGenerated = true)] public int Id { get; set; }
    [InquiryColumn] public DateTimeOffset OffsetAt { get; set; }
    [InquiryColumn] public DateOnly EventDate { get; set; }
    [InquiryColumn] public TimeOnly EventTime { get; set; }
    [InquiryColumn(Converter = typeof(OracleBusinessDateConverter))]
    public OracleBusinessDate ConvertedDate { get; set; }
    [InquiryColumn] public DateOnly? OptionalDate { get; set; }
    [InquiryColumn] public TimeOnly? OptionalTime { get; set; }
    [InquiryColumn] public DateTimeOffset? OptionalOffset { get; set; }
}

public partial class ProviderReadItemStore : InquiryStore<ProviderReadItem>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<ProviderReadItem?> InsertReturningAsync(ProviderReadItem item, CancellationToken ct = default);

    [InquirySelectOneByKey]
    public partial Task<ProviderReadItem?> SelectByKeyAsync(int id, CancellationToken ct = default);

    [InquiryUpdate(ReturnEntity = true)]
    public partial Task<ProviderReadItem?> UpdateReturningAsync(ProviderReadItem item, CancellationToken ct = default);

    [InquirySelectAllByField("EventDate")]
    public partial Task<IReadOnlyList<ProviderReadItem>> ByDateAsync(DateOnly date, CancellationToken ct = default);

    [InquirySelectAllByField("EventTime")]
    public partial Task<IReadOnlyList<ProviderReadItem>> ByTimeAsync(TimeOnly time, CancellationToken ct = default);

    [InquirySelectAllByField("OffsetAt")]
    public partial Task<IReadOnlyList<ProviderReadItem>> ByOffsetAsync(DateTimeOffset value, CancellationToken ct = default);

    [InquiryInsertAll]
    public partial Task<int> InsertAllAsync(IEnumerable<ProviderReadItem> items, CancellationToken ct = default);

    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IEnumerable<ProviderReadItem> items, CancellationToken ct = default);
}

[InquiryTable("ProviderReadAllTypes")]
public sealed class ProviderReadAllTypes
{
    [InquiryKey] public int Id { get; set; }
    [InquiryColumn] public int NumberValue { get; set; }
    [InquiryColumn] public bool Enabled { get; set; }
    [InquiryColumn] public Guid Token { get; set; }
    [InquiryColumn] public bool? OptionalEnabled { get; set; }
    [InquiryColumn] public Guid? OptionalToken { get; set; }
    [InquiryColumn(Converter = typeof(OracleExternalIdConverter))]
    public OracleExternalId ConvertedToken { get; set; }
    [InquiryColumn(Converter = typeof(OracleToggleConverter))]
    public OracleToggle ConvertedEnabled { get; set; }
    [InquiryColumn] public byte[] Payload { get; set; } = [];
    [InquiryColumn(Length = 100)] public string Name { get; set; } = string.Empty;
    [InquiryColumn] public DateTime OccurredAt { get; set; }
}

public partial class ProviderReadAllTypesStore : InquiryStore<ProviderReadAllTypes>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<ProviderReadAllTypes?> InsertReturningAsync(ProviderReadAllTypes item, CancellationToken ct = default);

    [InquirySelectOneByKey]
    public partial Task<ProviderReadAllTypes?> SelectByKeyAsync(int id, CancellationToken ct = default);

    [InquiryUpdate(ReturnEntity = true)]
    public partial Task<ProviderReadAllTypes?> UpdateReturningAsync(ProviderReadAllTypes item, CancellationToken ct = default);

    [InquirySelectAllByField("Token")]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> ByTokenAsync(Guid token, CancellationToken ct = default);

    [InquirySelectAllByField("Enabled")]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> ByEnabledAsync(bool enabled, CancellationToken ct = default);

    [InquirySelectAllByField("OptionalToken")]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> ByOptionalTokenAsync(Guid? token, CancellationToken ct = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("ConvertedToken")]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> ByConvertedTokenAsync(OracleExternalId token, CancellationToken ct = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("ConvertedEnabled")]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> ByConvertedEnabledAsync(OracleToggle enabled, CancellationToken ct = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Token", Compare.In)]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> InTokensAsync(IReadOnlyList<Guid> tokens, CancellationToken ct = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Enabled", Compare.In)]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> InEnabledAsync(IReadOnlyList<bool> enabled, CancellationToken ct = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("ConvertedToken", Compare.In)]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> InConvertedTokensAsync(IReadOnlyList<OracleExternalId> tokens, CancellationToken ct = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("ConvertedEnabled", Compare.In)]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> InConvertedEnabledAsync(IReadOnlyList<OracleToggle> enabled, CancellationToken ct = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Token", Compare.NotIn)]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> NotInTokensAsync(IReadOnlyList<Guid> tokens, CancellationToken ct = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Enabled", Compare.NotIn)]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> NotInEnabledAsync(IReadOnlyList<bool> enabled, CancellationToken ct = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("ConvertedToken", Compare.NotIn)]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> NotInConvertedTokensAsync(IReadOnlyList<OracleExternalId> tokens, CancellationToken ct = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("ConvertedEnabled", Compare.NotIn)]
    public partial Task<IReadOnlyList<ProviderReadAllTypes>> NotInConvertedEnabledAsync(IReadOnlyList<OracleToggle> enabled, CancellationToken ct = default);

    [InquiryInsertAll]
    public partial Task<int> InsertAllAsync(IEnumerable<ProviderReadAllTypes> items, CancellationToken ct = default);

    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IEnumerable<ProviderReadAllTypes> items, CancellationToken ct = default);
}

[InquiryTable("TemporalBatchItem")]
public sealed class TemporalBatchItem
{
    [InquiryKey] public int Id { get; set; }
    [InquiryColumn] public DateOnly EventDate { get; set; }
    [InquiryColumn] public TimeOnly EventTime { get; set; }
    [InquiryColumn] public DateTimeOffset OffsetAt { get; set; }
    [InquiryColumn(Converter = typeof(OracleBusinessDateConverter))] public OracleBusinessDate ConvertedDate { get; set; }
}

public partial class TemporalBatchItemStore : InquiryStore<TemporalBatchItem>
{
    [InquiryInsertAll] public partial Task<int> InsertAllAsync(IEnumerable<TemporalBatchItem> items, CancellationToken ct = default);
    [InquiryUpdateAll] public partial Task<int> UpdateAllAsync(IEnumerable<TemporalBatchItem> items, CancellationToken ct = default);
    [InquirySelectOneByKey] public partial Task<TemporalBatchItem?> SelectByKeyAsync(int id, CancellationToken ct = default);
}

[Collection(OracleCollection.Name)]
public sealed class ProviderAwareMaterializerIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public ProviderAwareMaterializerIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = """
        CREATE TABLE ProviderReadItem (
            Id NUMBER(10) GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            OffsetAt TIMESTAMP WITH TIME ZONE NOT NULL,
            EventDate DATE NOT NULL,
            EventTime INTERVAL DAY TO SECOND NOT NULL,
            ConvertedDate DATE NOT NULL,
            OptionalDate DATE NULL,
            OptionalTime INTERVAL DAY TO SECOND NULL,
            OptionalOffset TIMESTAMP WITH TIME ZONE NULL);
        CREATE TABLE ProviderReadAllTypes (
            Id NUMBER(10) PRIMARY KEY, NumberValue NUMBER(10) NOT NULL, Enabled NUMBER(1) NOT NULL,
            Token RAW(16) NOT NULL, OptionalEnabled NUMBER(1) NULL, OptionalToken RAW(16) NULL,
            ConvertedToken RAW(16) NOT NULL, ConvertedEnabled NUMBER(1) NOT NULL,
            Payload BLOB NOT NULL, Name NVARCHAR2(100) NOT NULL, OccurredAt TIMESTAMP NOT NULL);
        CREATE TABLE TemporalBatchItem (
            Id NUMBER(10) PRIMARY KEY, EventDate DATE NOT NULL,
            EventTime INTERVAL DAY TO SECOND NOT NULL, OffsetAt TIMESTAMP WITH TIME ZONE NOT NULL,
            ConvertedDate DATE NOT NULL)
        """;

    [SkippableFact]
    public async Task OrdinarySelectUsesProviderAwareReaders()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "providerread");
        var store = harness.GetRequiredService<ProviderReadItemStore>();
        var date = new DateOnly(2024, 2, 29);
        var item = new ProviderReadItem
        {
            OffsetAt = new DateTimeOffset(2026, 7, 11, 12, 34, 56, TimeSpan.FromMinutes(-270)),
            EventDate = date,
            // ODP.NET normalizes INTERVAL DAY TO SECOND to microsecond precision (10 ticks). Keep a
            // seven-digit fractional value on that representable boundary and assert its exact ticks.
            EventTime = new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1_234_570)),
            ConvertedDate = new OracleBusinessDate(date.AddDays(1)),
            OptionalDate = null,
            OptionalTime = null,
            OptionalOffset = null,
        };

        var returned = await store.InsertReturningAsync(item);
        Assert.NotNull(returned);
        Assert.True(returned!.Id > 0);
        AssertItem(returned, item);
        item = returned;

        var selected = await store.SelectByKeyAsync(returned.Id);
        Assert.NotNull(selected);
        AssertItem(selected!, item);

        item.EventDate = date.AddDays(1);
        item.EventTime = item.EventTime.Add(TimeSpan.FromTicks(10));
        item.OptionalDate = DateOnly.MinValue;
        item.OptionalTime = new TimeOnly(23, 59, 59).Add(TimeSpan.FromTicks(9_999_990));
        item.OptionalOffset = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.FromMinutes(345));
        var updated = await store.UpdateReturningAsync(item);
        Assert.NotNull(updated);
        AssertItem(updated!, item);

        Assert.Single(await store.ByDateAsync(item.EventDate));
        Assert.Single(await store.ByTimeAsync(item.EventTime));
        Assert.Single(await store.ByOffsetAsync(item.OffsetAt));

        var returnedBatch = new[]
        {
            await store.InsertReturningAsync(NewTemporal(date.AddDays(4), new TimeOnly(7, 8, 9), TimeSpan.FromMinutes(330))),
            await store.InsertReturningAsync(NewTemporal(date.AddDays(5), new TimeOnly(10, 11, 12), TimeSpan.FromMinutes(-270))),
        };
        Assert.All(returnedBatch, Assert.NotNull);
        foreach (var value in returnedBatch) value!.EventDate = value.EventDate.AddDays(1);
        Assert.Equal(2, await store.UpdateAllAsync(returnedBatch.Select(static value => value!).ToArray()));

        var batchStore = harness.GetRequiredService<TemporalBatchItemStore>();
        var batch = new[]
        {
            new TemporalBatchItem { Id = 101, EventDate = DateOnly.MinValue, EventTime = new TimeOnly(1, 2, 3), OffsetAt = new DateTimeOffset(date.AddDays(2).ToDateTime(new TimeOnly(1, 2, 3)), TimeSpan.FromMinutes(345)), ConvertedDate = new OracleBusinessDate(DateOnly.MinValue) },
            new TemporalBatchItem { Id = 102, EventDate = DateOnly.MaxValue, EventTime = new TimeOnly(4, 5, 6), OffsetAt = new DateTimeOffset(date.AddDays(3).ToDateTime(new TimeOnly(4, 5, 6)), TimeSpan.FromMinutes(-210)), ConvertedDate = new OracleBusinessDate(DateOnly.MaxValue) },
        };
        Assert.Equal(2, await batchStore.InsertAllAsync(batch));
        batch[0].EventDate = batch[0].EventDate.AddDays(1);
        batch[1].EventTime = batch[1].EventTime.Add(TimeSpan.FromTicks(10));
        Assert.Equal(2, await batchStore.UpdateAllAsync(batch));
        Assert.Equal(batch[0].EventDate, (await batchStore.SelectByKeyAsync(101))!.EventDate);
        Assert.Equal(batch[1].EventTime.Ticks, (await batchStore.SelectByKeyAsync(102))!.EventTime.Ticks);
    }

    [SkippableFact]
    public async Task OrdinarySelectRetainsNonTemporalReaderCoverage()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "providerall");
        var token = Guid.NewGuid();
        await using (var connection = new OracleConnection(harness.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO ProviderReadAllTypes (Id,NumberValue,Enabled,Token,ConvertedToken,ConvertedEnabled,Payload,Name,OccurredAt) VALUES (1,42,1,HEXTORAW('{Convert.ToHexString(token.ToByteArray())}'),HEXTORAW('{Convert.ToHexString(token.ToByteArray())}'),1,HEXTORAW('01020304'),'reader',TIMESTAMP '2026-07-11 12:34:56')";
            await command.ExecuteNonQueryAsync();
        }
        var value = await harness.GetRequiredService<ProviderReadAllTypesStore>().SelectByKeyAsync(1);
        Assert.NotNull(value);
        Assert.Equal(42, value!.NumberValue);
        Assert.True(value.Enabled);
        Assert.Equal(token, value.Token);
        Assert.Equal(new OracleExternalId(token), value.ConvertedToken);
        Assert.Equal(new OracleToggle(true), value.ConvertedEnabled);
        Assert.Equal([1, 2, 3, 4], value.Payload);
        Assert.Equal("reader", value.Name);
        Assert.Equal(new DateTime(2026, 7, 11, 12, 34, 56), value.OccurredAt);
    }

    [SkippableFact]
    public async Task GeneratedAllTypesBindersRoundTripGuidBooleanAndNullables()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "providerallbind");
        var store = harness.GetRequiredService<ProviderReadAllTypesStore>();
        var token = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        var optionalToken = new Guid("ffeeddcc-bbaa-9988-7766-554433221100");
        var convertedToken = new Guid("10213243-5465-7687-98a9-bacbdcedfe0f");
        var item = NewAllTypes(1, true, token, false, optionalToken, false, convertedToken);

        var inserted = await store.InsertReturningAsync(item);
        Assert.NotNull(inserted);
        AssertAllTypes(item, inserted!);
        Assert.Single(await store.ByTokenAsync(token));
        Assert.Single(await store.ByEnabledAsync(true));
        Assert.Single(await store.ByOptionalTokenAsync(optionalToken));
        Assert.Single(await store.ByConvertedTokenAsync(new OracleExternalId(convertedToken)));
        Assert.Single(await store.ByConvertedEnabledAsync(new OracleToggle(false)));
        Assert.Single(await store.InTokensAsync([token]));
        Assert.Single(await store.InEnabledAsync([true]));
        Assert.Single(await store.InConvertedTokensAsync([new OracleExternalId(convertedToken)]));
        Assert.Single(await store.InConvertedEnabledAsync([new OracleToggle(false)]));
        Assert.Single(await store.NotInTokensAsync([optionalToken]));
        Assert.Empty(await store.NotInTokensAsync([token]));
        Assert.Single(await store.NotInEnabledAsync([false]));
        Assert.Empty(await store.NotInEnabledAsync([true]));
        Assert.Single(await store.NotInConvertedTokensAsync([new OracleExternalId(token)]));
        Assert.Empty(await store.NotInConvertedTokensAsync([new OracleExternalId(convertedToken)]));
        Assert.Single(await store.NotInConvertedEnabledAsync([new OracleToggle(true)]));
        Assert.Empty(await store.NotInConvertedEnabledAsync([new OracleToggle(false)]));

        item.Enabled = false;
        item.Token = optionalToken;
        item.OptionalEnabled = null;
        item.OptionalToken = null;
        item.ConvertedEnabled = new OracleToggle(true);
        item.ConvertedToken = new OracleExternalId(token);
        var updated = await store.UpdateReturningAsync(item);
        Assert.NotNull(updated);
        AssertAllTypes(item, updated!);
        Assert.Single(await store.ByConvertedTokenAsync(item.ConvertedToken));
        Assert.Single(await store.ByConvertedEnabledAsync(item.ConvertedEnabled));
        Assert.Single(await store.InTokensAsync([optionalToken]));
        Assert.Single(await store.InEnabledAsync([false]));
        Assert.Single(await store.InConvertedTokensAsync([new OracleExternalId(token)]));
        Assert.Single(await store.InConvertedEnabledAsync([new OracleToggle(true)]));

        var batch = new[]
        {
            NewAllTypes(2, false, convertedToken, null, null, true, optionalToken),
            NewAllTypes(3, true, new Guid("0ffeeddc-cbba-a998-8776-655443322110"), true, token, false, token),
        };
        batch[0].Name = "éclair";
        batch[1].Name = "東京";
        Assert.Equal(2, await store.InsertAllAsync(batch));
        foreach (var expected in batch)
        {
            var selected = await store.SelectByKeyAsync(expected.Id);
            Assert.NotNull(selected);
            AssertAllTypes(expected, selected!);
        }

        batch[0].Enabled = true;
        batch[0].OptionalEnabled = false;
        batch[0].OptionalToken = optionalToken;
        batch[0].ConvertedEnabled = new OracleToggle(false);
        batch[0].ConvertedToken = new OracleExternalId(token);
        batch[1].Enabled = false;
        batch[1].OptionalEnabled = null;
        batch[1].OptionalToken = null;
        batch[1].ConvertedEnabled = new OracleToggle(true);
        batch[1].ConvertedToken = new OracleExternalId(convertedToken);
        Assert.Equal(2, await store.UpdateAllAsync(batch));
        AssertAllTypes(batch[0], (await store.SelectByKeyAsync(batch[0].Id))!);
        AssertAllTypes(batch[1], (await store.SelectByKeyAsync(batch[1].Id))!);
    }

    private static ProviderReadItem NewTemporal(DateOnly date, TimeOnly time, TimeSpan offset) => new()
    {
        EventDate = date,
        EventTime = time,
        OffsetAt = new DateTimeOffset(date.ToDateTime(time), offset),
        ConvertedDate = new OracleBusinessDate(date),
    };

    private static ProviderReadAllTypes NewAllTypes(
        int id,
        bool enabled,
        Guid token,
        bool? optionalEnabled,
        Guid? optionalToken,
        bool convertedEnabled,
        Guid convertedToken) => new()
    {
        Id = id,
        NumberValue = id * 10,
        Enabled = enabled,
        Token = token,
        OptionalEnabled = optionalEnabled,
        OptionalToken = optionalToken,
        ConvertedEnabled = new OracleToggle(convertedEnabled),
        ConvertedToken = new OracleExternalId(convertedToken),
        Payload = [(byte)id, (byte)(id + 1)],
        Name = $"item-{id}",
        OccurredAt = new DateTime(2026, 7, 11, 12, 34, id),
    };

    private static void AssertAllTypes(ProviderReadAllTypes expected, ProviderReadAllTypes actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.NumberValue, actual.NumberValue);
        Assert.Equal(expected.Enabled, actual.Enabled);
        Assert.Equal(expected.Token, actual.Token);
        Assert.Equal(expected.OptionalEnabled, actual.OptionalEnabled);
        Assert.Equal(expected.OptionalToken, actual.OptionalToken);
        Assert.Equal(expected.ConvertedEnabled, actual.ConvertedEnabled);
        Assert.Equal(expected.ConvertedToken, actual.ConvertedToken);
        Assert.Equal(expected.Payload, actual.Payload);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.OccurredAt, actual.OccurredAt);
    }

    private static void AssertItem(ProviderReadItem actual, ProviderReadItem expected)
    {
        Assert.Equal(expected.OffsetAt.UtcDateTime, actual.OffsetAt.UtcDateTime);
        Assert.Equal(expected.OffsetAt.Offset, actual.OffsetAt.Offset);
        Assert.Equal(expected.EventDate, actual.EventDate);
        Assert.Equal(expected.EventTime.Ticks, actual.EventTime.Ticks);
        Assert.Equal(expected.ConvertedDate, actual.ConvertedDate);
        Assert.Equal(expected.OptionalDate, actual.OptionalDate);
        Assert.Equal(expected.OptionalTime?.Ticks, actual.OptionalTime?.Ticks);
        Assert.Equal(expected.OptionalOffset, actual.OptionalOffset);
    }
}
