using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

public enum TvpFacetState : uint { Maximum = uint.MaxValue }
public readonly record struct TvpFacetToken(uint Value);
public sealed class TvpFacetTokenConverter : IInquiryValueConverter<TvpFacetToken, uint>
{
    public uint ToProvider(TvpFacetToken value) => value.Value;
    public TvpFacetToken FromProvider(uint value) => new(value);
}

[InquiryTable("TvpFacetItem")]
public sealed class TvpFacetItem
{
    [InquiryKey] public int Id { get; set; }
    [InquiryColumn(SqlType = "VARCHAR(37)")] public string AnsiCode { get; set; } = string.Empty;
    [InquiryColumn(SqlType = "CHAR(5)")] public string FixedCode { get; set; } = string.Empty;
    [InquiryColumn(Precision = 29, Scale = 7)] public decimal Amount { get; set; }
    [InquiryColumn(Length = 17)] public byte[] Payload { get; set; } = [];
    [InquiryColumn(SqlType = "BINARY(4)")] public byte[] FixedPayload { get; set; } = [];
    [InquiryColumn(Scale = 3)] public DateTimeOffset CapturedAt { get; set; }
    [InquiryColumn] public int? OptionalNumber { get; set; }
    [InquiryColumn(Length = 19)] public string UnicodeCode { get; set; } = string.Empty;
    [InquiryColumn] public string UnicodeMax { get; set; } = string.Empty;
    [InquiryColumn(SqlType = "VARCHAR(MAX)")] public string AnsiMax { get; set; } = string.Empty;
    [InquiryColumn] public bool Flag { get; set; }
    [InquiryColumn] public byte Tiny { get; set; }
    [InquiryColumn] public short Small { get; set; }
    [InquiryColumn] public long Big { get; set; }
    [InquiryColumn] public float RealValue { get; set; }
    [InquiryColumn] public double FloatValue { get; set; }
    [InquiryColumn] public Guid ExternalId { get; set; }
    [InquiryColumn(Scale = 0)] public DateTime OccurredAt { get; set; }
    [InquiryColumn] public DateOnly OccurredOn { get; set; }
    [InquiryColumn(Scale = 7)] public TimeOnly LocalTime { get; set; }
    [InquiryColumn] public TvpFacetState State { get; set; }
    [InquiryColumn(Converter = typeof(TvpFacetTokenConverter))] public TvpFacetToken Token { get; set; }
    [InquiryColumn(Converter = typeof(TvpFacetTokenConverter))] public TvpFacetToken? OptionalToken { get; set; }
}

public partial class TvpFacetStore : InquiryStore<TvpFacetItem>
{
    [InquiryExists, InquiryWhere("AnsiCode", Compare.In)]
    public partial Task<bool> HasAnsiCodeAsync(IReadOnlyList<string> values, CancellationToken cancellationToken = default);

    [InquiryExists, InquiryWhere("FixedCode", Compare.In)]
    public partial Task<bool> HasFixedCodeAsync(IReadOnlyList<string> values, CancellationToken cancellationToken = default);

    [InquiryExists, InquiryWhere("Amount", Compare.In)]
    public partial Task<bool> HasAmountAsync(IReadOnlyList<decimal> values, CancellationToken cancellationToken = default);

    [InquiryExists, InquiryWhere("Payload", Compare.In)]
    public partial Task<bool> HasPayloadAsync(IReadOnlyList<byte[]> values, CancellationToken cancellationToken = default);

    [InquiryExists, InquiryWhere("FixedPayload", Compare.In)]
    public partial Task<bool> HasFixedPayloadAsync(IReadOnlyList<byte[]> values, CancellationToken cancellationToken = default);

    [InquiryExists, InquiryWhere("CapturedAt", Compare.In)]
    public partial Task<bool> HasCapturedAtAsync(IReadOnlyList<DateTimeOffset> values, CancellationToken cancellationToken = default);

    [InquiryExists, InquiryWhere("OptionalNumber", Compare.In)]
    public partial Task<bool> HasOptionalNumberAsync(IReadOnlyList<int?> values, CancellationToken cancellationToken = default);

    [InquiryExists, InquiryWhere("UnicodeCode", Compare.In)] public partial Task<bool> HasUnicodeCodeAsync(IReadOnlyList<string> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("UnicodeMax", Compare.In)] public partial Task<bool> HasUnicodeMaxAsync(IReadOnlyList<string> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("AnsiMax", Compare.In)] public partial Task<bool> HasAnsiMaxAsync(IReadOnlyList<string> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("Flag", Compare.In)] public partial Task<bool> HasFlagAsync(IReadOnlyList<bool> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("Tiny", Compare.In)] public partial Task<bool> HasTinyAsync(IReadOnlyList<byte> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("Small", Compare.In)] public partial Task<bool> HasSmallAsync(IReadOnlyList<short> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("Big", Compare.In)] public partial Task<bool> HasBigAsync(IReadOnlyList<long> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("RealValue", Compare.In)] public partial Task<bool> HasRealAsync(IReadOnlyList<float> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("FloatValue", Compare.In)] public partial Task<bool> HasFloatAsync(IReadOnlyList<double> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("ExternalId", Compare.In)] public partial Task<bool> HasGuidAsync(IReadOnlyList<Guid> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("OccurredAt", Compare.In)] public partial Task<bool> HasDateTimeAsync(IReadOnlyList<DateTime> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("OccurredOn", Compare.In)] public partial Task<bool> HasDateOnlyAsync(IReadOnlyList<DateOnly> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("LocalTime", Compare.In)] public partial Task<bool> HasTimeOnlyAsync(IReadOnlyList<TimeOnly> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("State", Compare.In)] public partial Task<bool> HasStateAsync(IReadOnlyList<TvpFacetState> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("Token", Compare.In)] public partial Task<bool> HasTokenAsync(IReadOnlyList<TvpFacetToken> values, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("OptionalToken", Compare.In)] public partial Task<bool> HasOptionalTokenAsync(IReadOnlyList<TvpFacetToken?> values, CancellationToken cancellationToken = default);
}

[Collection(SqlServerCollection.Name)]
public sealed class TvpFacetIntegrationTests
{
    private const string Ddl = """
        CREATE TABLE [TvpFacetItem]
        (
            [Id] INT NOT NULL PRIMARY KEY,
            [AnsiCode] VARCHAR(37) NOT NULL,
            [FixedCode] CHAR(5) NOT NULL,
            [Amount] DECIMAL(29,7) NOT NULL,
            [Payload] VARBINARY(17) NOT NULL,
            [FixedPayload] BINARY(4) NOT NULL,
            [CapturedAt] DATETIMEOFFSET(3) NOT NULL,
            [OptionalNumber] INT NULL,
            [UnicodeCode] NVARCHAR(19) NOT NULL,
            [UnicodeMax] NVARCHAR(MAX) NOT NULL,
            [AnsiMax] VARCHAR(MAX) NOT NULL,
            [Flag] BIT NOT NULL,
            [Tiny] TINYINT NOT NULL,
            [Small] SMALLINT NOT NULL,
            [Big] BIGINT NOT NULL,
            [RealValue] REAL NOT NULL,
            [FloatValue] FLOAT NOT NULL,
            [ExternalId] UNIQUEIDENTIFIER NOT NULL,
            [OccurredAt] DATETIME2(0) NOT NULL,
            [OccurredOn] DATE NOT NULL,
            [LocalTime] TIME(7) NOT NULL,
            [State] INT NOT NULL,
            [Token] INT NOT NULL,
            [OptionalToken] INT NULL
        );
        INSERT [TvpFacetItem]
            ([Id], [AnsiCode], [FixedCode], [Amount], [Payload], [FixedPayload], [CapturedAt], [OptionalNumber],
             [UnicodeCode], [UnicodeMax], [AnsiMax], [Flag], [Tiny], [Small], [Big], [RealValue], [FloatValue],
             [ExternalId], [OccurredAt], [OccurredOn], [LocalTime], [State], [Token], [OptionalToken])
        VALUES
            (1, 'alpha', 'fixed', 123.4567000, 0x010203, 0x01020304, '2026-07-13T12:34:56.123+00:00', 42,
             N'κόσμε', N'unbounded-unicode', 'unbounded-ansi', 1, 255, -123, 9223372036854775806,
             CAST(1.5 AS REAL), CAST(2.5 AS FLOAT), '11111111-2222-3333-4444-555555555555',
             '2026-07-13T12:34:56', '2026-07-13', '12:34:56.1234567', -1, -1, -1);
        """;

    private readonly SqlServerContainerFixture _fixture;
    public TvpFacetIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task EmptyCollectionsBindZeroRowsAcrossEveryFacetFamily()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "tvpfacetempty");
        var store = harness.GetRequiredService<TvpFacetStore>();

        Assert.False(await store.HasAnsiCodeAsync([]));
        Assert.False(await store.HasFixedCodeAsync([]));
        Assert.False(await store.HasAmountAsync([]));
        Assert.False(await store.HasPayloadAsync([]));
        Assert.False(await store.HasFixedPayloadAsync([]));
        Assert.False(await store.HasCapturedAtAsync([]));
        Assert.False(await store.HasOptionalNumberAsync([]));
        Assert.False(await store.HasUnicodeCodeAsync([]));
        Assert.False(await store.HasUnicodeMaxAsync([]));
        Assert.False(await store.HasAnsiMaxAsync([]));
        Assert.False(await store.HasFlagAsync([]));
        Assert.False(await store.HasTinyAsync([]));
        Assert.False(await store.HasSmallAsync([]));
        Assert.False(await store.HasBigAsync([]));
        Assert.False(await store.HasRealAsync([]));
        Assert.False(await store.HasFloatAsync([]));
        Assert.False(await store.HasGuidAsync([]));
        Assert.False(await store.HasDateTimeAsync([]));
        Assert.False(await store.HasDateOnlyAsync([]));
        Assert.False(await store.HasTimeOnlyAsync([]));
        Assert.False(await store.HasStateAsync([]));
        Assert.False(await store.HasTokenAsync([]));
        Assert.False(await store.HasOptionalTokenAsync([]));
    }

    [SkippableFact]
    public async Task ExactDescriptorsRoundTripNonEmptyFacetValuesAndNullableRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "tvpfacetvalues");
        var store = harness.GetRequiredService<TvpFacetStore>();

        Assert.True(await store.HasAnsiCodeAsync(["alpha"]));
        Assert.True(await store.HasFixedCodeAsync(["fixed"]));
        Assert.True(await store.HasAmountAsync([123.4567000m]));
        Assert.True(await store.HasPayloadAsync([new byte[] { 1, 2, 3 }]));
        Assert.True(await store.HasFixedPayloadAsync([new byte[] { 1, 2, 3, 4 }]));
        Assert.True(await store.HasCapturedAtAsync([new DateTimeOffset(2026, 7, 13, 12, 34, 56, 123, TimeSpan.Zero)]));
        Assert.True(await store.HasOptionalNumberAsync([null, 42]));
        Assert.True(await store.HasUnicodeCodeAsync(["κόσμε"]));
        Assert.True(await store.HasUnicodeMaxAsync(["unbounded-unicode"]));
        Assert.True(await store.HasAnsiMaxAsync(["unbounded-ansi"]));
        Assert.True(await store.HasFlagAsync([true]));
        Assert.True(await store.HasTinyAsync([byte.MaxValue]));
        Assert.True(await store.HasSmallAsync([-123]));
        Assert.True(await store.HasBigAsync([9223372036854775806]));
        Assert.True(await store.HasRealAsync([1.5f]));
        Assert.True(await store.HasFloatAsync([2.5]));
        Assert.True(await store.HasGuidAsync([Guid.Parse("11111111-2222-3333-4444-555555555555")]));
        Assert.True(await store.HasDateTimeAsync([new DateTime(2026, 7, 13, 12, 34, 56, DateTimeKind.Unspecified)]));
        Assert.True(await store.HasDateOnlyAsync([new DateOnly(2026, 7, 13)]));
        Assert.True(await store.HasTimeOnlyAsync([new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1234567))]));
        Assert.True(await store.HasStateAsync([TvpFacetState.Maximum]));
        Assert.True(await store.HasTokenAsync([new TvpFacetToken(uint.MaxValue)]));
        Assert.True(await store.HasOptionalTokenAsync([null, new TvpFacetToken(uint.MaxValue)]));
    }
}
