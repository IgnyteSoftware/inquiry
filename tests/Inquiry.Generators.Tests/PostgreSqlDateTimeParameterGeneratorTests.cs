namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string PostgreSqlDateTimeParameterSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;
        namespace Demo;

        public readonly record struct WallClock(DateTime Value);
        public sealed class WallClockConverter : IInquiryValueConverter<WallClock, DateTime>
        {
            public DateTime ToProvider(WallClock value) => value.Value;
            public WallClock FromProvider(DateTime value) => new(value);
        }

        [InquiryTable("TemporalItem")]
        public sealed class TemporalItem
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public DateTime OccurredAt { get; set; }
            [InquiryColumn] public DateTime? OptionalAt { get; set; }
            [InquiryColumn(Converter = typeof(WallClockConverter))] public WallClock ConvertedAt { get; set; }
            [InquiryColumn] public DateTimeOffset OffsetAt { get; set; }
        }

        public partial class TemporalStore : InquiryStore<TemporalItem>
        {
            [InquiryInsert(ReturnEntity = true)] public partial Task<TemporalItem?> InsertAsync(TemporalItem item, CancellationToken ct = default);
            [InquiryBulkInsert] public partial Task<long> BulkAsync(IEnumerable<TemporalItem> items, CancellationToken ct = default);
            [InquirySelectAllByPredicate][InquiryWhere("OccurredAt")] public partial Task<IReadOnlyList<TemporalItem>> ByOccurredAsync(DateTime value, CancellationToken ct = default);
            [InquirySelectAllByPredicate][InquiryWhere("OptionalAt")] public partial Task<IReadOnlyList<TemporalItem>> ByOptionalAsync(DateTime? value, CancellationToken ct = default);
            [InquirySelectAllByPredicate][InquiryWhere("OccurredAt", Compare.In)] public partial Task<IReadOnlyList<TemporalItem>> InOccurredAsync(IReadOnlyList<DateTime> values, CancellationToken ct = default);
            [InquirySelectAllByPredicate][InquiryWhere("ConvertedAt", Compare.In)] public partial Task<IReadOnlyList<TemporalItem>> InConvertedAsync(IReadOnlyList<WallClock> values, CancellationToken ct = default);
            [InquirySelectAllByPredicate][InquiryWhere("OffsetAt")] public partial Task<IReadOnlyList<TemporalItem>> ByOffsetAsync(DateTimeOffset value, CancellationToken ct = default);
        }
        """;

    [Fact]
    public void PostgreSqlDateTimeValuesBridgeAcrossScalarConverterBulkAndCollectionPaths()
    {
        var result = RunGenerator(PostgreSqlDateTimeParameterSource, dialect: "PostgreSql");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("TemporalStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();
        const string bridge = "global::System.DateTime.SpecifyKind(";
        const string unspecified = "global::System.DateTimeKind.Unspecified";

        var insert = Method(text, "InsertAsync");
        Assert.Contains(bridge, insert);
        Assert.Contains("OccurredAt", insert);
        Assert.Contains("OptionalAt.Value", insert);
        Assert.Contains("WallClockConverter>.Instance.ToProvider(", insert);
        Assert.Contains(unspecified, insert);
        Assert.Contains("OffsetAt", insert);
        Assert.DoesNotContain("SpecifyKind(_e.OffsetAt", insert);

        Assert.Contains(bridge + "_e.OccurredAt, " + unspecified + ")", text);
        Assert.Contains("Inquiry.BulkInsertAsync", Method(text, "BulkAsync"));
        Assert.Contains(bridge + "value, " + unspecified + ")", Method(text, "ByOccurredAsync"));
        Assert.Contains(bridge + "value.Value, " + unspecified + ")", Method(text, "ByOptionalAsync"));

        var directCollection = Method(text, "InOccurredAsync");
        Assert.Contains("global::System.Linq.Enumerable.Select(values, static _e => " + bridge + "_e, " + unspecified + "))", directCollection);
        var convertedCollection = Method(text, "InConvertedAsync");
        Assert.Contains("WallClockConverter>.Instance.ToProvider(_e)", convertedCollection);
        Assert.Contains(bridge, convertedCollection);

        var offset = Method(text, "ByOffsetAsync");
        Assert.Contains("value", offset);
        Assert.DoesNotContain("SpecifyKind", offset);
        Assert.Contains("DbType.DateTimeOffset", offset);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void NonPostgreSqlDateTimeValuesRemainUnbridged(string dialect)
    {
        var result = RunGenerator(PostgreSqlDateTimeParameterSource, dialect: dialect);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("TemporalStore.InquiryStore.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain("DateTime.SpecifyKind", tree.GetText().ToString());
    }
}
