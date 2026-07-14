namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string OracleTemporalEagerSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;
        namespace Demo;

        public readonly record struct BusinessDate(DateOnly Value);
        public sealed class BusinessDateConverter : IInquiryValueConverter<BusinessDate, DateOnly>
        { public DateOnly ToProvider(BusinessDate value) => value.Value; public BusinessDate FromProvider(DateOnly value) => new(value); }
        public readonly record struct BusinessTime(TimeOnly Value);
        public sealed class BusinessTimeConverter : IInquiryValueConverter<BusinessTime, TimeOnly>
        { public TimeOnly ToProvider(BusinessTime value) => value.Value; public BusinessTime FromProvider(TimeOnly value) => new(value); }

        [InquiryTable("DateParent")]
        public sealed class DateParent
        {
            [InquiryKey] public DateOnly Id { get; set; }
            [InquiryRelation(nameof(DateChild.ParentId))]
            public IReadOnlyList<DateChild> Children { get; set; } = new List<DateChild>();
        }
        [InquiryTable("DateChild")]
        public sealed class DateChild
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public DateOnly ParentId { get; set; }
        }
        public partial class DateParentStore : InquiryStore<DateParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<DateParent?> GetAsync(DateOnly id, CancellationToken cancellationToken = default);
        }

        [InquiryTable("ConvertedDateParent")]
        public sealed class ConvertedDateParent
        {
            [InquiryKey(Converter = typeof(BusinessDateConverter))] public BusinessDate Id { get; set; }
            [InquiryRelation(nameof(ConvertedDateChild.ParentId))]
            public IReadOnlyList<ConvertedDateChild> Children { get; set; } = new List<ConvertedDateChild>();
        }
        [InquiryTable("ConvertedDateChild")]
        public sealed class ConvertedDateChild
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn(Converter = typeof(BusinessDateConverter))] public BusinessDate ParentId { get; set; }
        }
        public partial class ConvertedDateParentStore : InquiryStore<ConvertedDateParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<ConvertedDateParent?> GetAsync(BusinessDate id, CancellationToken cancellationToken = default);
        }

        [InquiryTable("TimeParent")]
        public sealed class TimeParent
        {
            [InquiryKey] public TimeOnly Id { get; set; }
            [InquiryColumn(Converter = typeof(BusinessDateConverter))] public BusinessDate OwnerId { get; set; }
            [InquiryRelation(nameof(OwnerId))] public DateOwner? Owner { get; set; }
        }
        [InquiryTable("DateOwner")]
        public sealed class DateOwner
        {
            [InquiryKey(Converter = typeof(BusinessDateConverter))] public BusinessDate Id { get; set; }
        }
        public partial class TimeParentStore : InquiryStore<TimeParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<TimeParent?> GetAsync(TimeOnly id, CancellationToken cancellationToken = default);
        }

        [InquiryTable("ConvertedTimeParent")]
        public sealed class ConvertedTimeParent
        {
            [InquiryKey(Converter = typeof(BusinessTimeConverter))] public BusinessTime Id { get; set; }
            [InquiryColumn] public TimeOnly OwnerId { get; set; }
            [InquiryRelation(nameof(OwnerId))] public TimeOwner? Owner { get; set; }
        }
        [InquiryTable("TimeOwner")]
        public sealed class TimeOwner
        {
            [InquiryKey] public TimeOnly Id { get; set; }
        }
        public partial class ConvertedTimeParentStore : InquiryStore<ConvertedTimeParent>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<ConvertedTimeParent?> GetAsync(BusinessTime id, CancellationToken cancellationToken = default);
        }
        """;

    private const string OracleTemporalParameterSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Paging;
        using Inquiry.Stores;
        namespace Demo;
        public readonly record struct BusinessDate(DateOnly Value);
        public sealed class BusinessDateConverter : IInquiryValueConverter<BusinessDate, DateOnly>
        { public DateOnly ToProvider(BusinessDate value) => value.Value; public BusinessDate FromProvider(DateOnly value) => new(value); }
        public readonly record struct BusinessTime(TimeOnly Value);
        public sealed class BusinessTimeConverter : IInquiryValueConverter<BusinessTime, TimeOnly>
        { public TimeOnly ToProvider(BusinessTime value) => value.Value; public BusinessTime FromProvider(TimeOnly value) => new(value); }
        public readonly record struct BusinessOffset(DateTimeOffset Value);
        public sealed class BusinessOffsetConverter : IInquiryValueConverter<BusinessOffset, DateTimeOffset>
        { public DateTimeOffset ToProvider(BusinessOffset value) => value.Value; public BusinessOffset FromProvider(DateTimeOffset value) => new(value); }
        [InquiryTable("TemporalItem")]
        public sealed class TemporalItem
        {
            [InquiryKey(IsGenerated = true)] public int Id { get; set; }
            [InquiryColumn] public DateOnly EventDate { get; set; }
            [InquiryColumn] public TimeOnly EventTime { get; set; }
            [InquiryColumn] public DateTimeOffset OffsetAt { get; set; }
            [InquiryColumn] public DateOnly? OptionalDate { get; set; }
            [InquiryColumn] public TimeOnly? OptionalTime { get; set; }
            [InquiryColumn(Converter = typeof(BusinessDateConverter))] public BusinessDate ConvertedDate { get; set; }
            [InquiryColumn(Converter = typeof(BusinessTimeConverter))] public BusinessTime ConvertedTime { get; set; }
            [InquiryColumn(Converter = typeof(BusinessDateConverter))] public BusinessDate? OptionalConvertedDate { get; set; }
            [InquiryColumn(Converter = typeof(BusinessOffsetConverter))] public BusinessOffset ConvertedOffset { get; set; }
        }
        public partial class TemporalStore : InquiryStore<TemporalItem>
        {
            [InquiryInsert(ReturnEntity = true)] public partial Task<TemporalItem?> InsertAsync(TemporalItem item, CancellationToken ct = default);
            [InquiryUpdate] public partial Task<bool> UpdateAsync(TemporalItem item, CancellationToken ct = default);
            [InquiryInsertAll] public partial Task<int> InsertAllAsync(IEnumerable<TemporalItem> items, CancellationToken ct = default);
            [InquiryUpdateAll] public partial Task<int> UpdateAllAsync(IEnumerable<TemporalItem> items, CancellationToken ct = default);
            [InquirySelectAllByField("EventDate", OrderBy = "Id", Paged = true)] public partial Task<IReadOnlyList<TemporalItem>> PageAsync(DateOnly date, int offset, int limit, CancellationToken ct = default);
            [InquirySelectAllByPredicate][InquiryWhere("EventTime")] public partial Task<IReadOnlyList<TemporalItem>> ByTimeAsync(TimeOnly time, CancellationToken ct = default);
            [InquiryKeysetPage("EventDate")] public partial Task<InquiryPage<TemporalItem, DateOnly>> SeekAsync(DateOnly? after, int pageSize, CancellationToken ct = default);
            [InquiryKeysetPage("ConvertedDate")] public partial Task<InquiryPage<TemporalItem, BusinessDate>> SeekConvertedAsync(BusinessDate? after, int pageSize, CancellationToken ct = default);
            [InquirySelectAllByPredicate][InquiryWhere("EventDate", Compare.In)] public partial Task<IReadOnlyList<TemporalItem>> InDatesAsync(IReadOnlyList<DateOnly> dates, CancellationToken ct = default);
        }
        """;

    [Fact]
    public void OracleTemporalValuesBridgeAfterNullAndConverterTransforms()
    {
        var result = RunGenerator(OracleTemporalParameterSource, dialect: "Oracle");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("TemporalStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("EventDate.ToDateTime(global::System.TimeOnly.MinValue)", text);
        Assert.Contains("EventTime.ToTimeSpan()", text);
        Assert.Contains("OptionalDate.Value.ToDateTime(global::System.TimeOnly.MinValue)", text);
        Assert.Contains("OptionalTime.Value.ToTimeSpan()", text);
        Assert.Contains("BusinessDateConverter>.Instance.ToProvider(", text);
        Assert.Contains(").ToDateTime(global::System.TimeOnly.MinValue)", text);
        Assert.Contains("BusinessTimeConverter>.Instance.ToProvider(", text);
        Assert.Contains(").ToTimeSpan()", text);
        Assert.Contains("OptionalConvertedDate is null ? global::System.DBNull.Value", text);
        Assert.Contains("OptionalConvertedDate.Value", text);
        Assert.Contains("BusinessOffsetConverter>.Instance.ToProvider(", text);
        Assert.Contains("DbType.DateTimeOffset;", text);
        Assert.Contains("DbType.Date;", text);
        Assert.Contains("DbType.DateTimeOffset;", text);
        Assert.DoesNotContain("DbType.Time;", text);
        Assert.Contains("after.Value.ToDateTime(global::System.TimeOnly.MinValue)", text);

        foreach (var methodName in new[] { "InsertAsync", "UpdateAsync", "InsertAllAsync", "UpdateAllAsync" })
        {
            var method = Method(text, methodName);
            Assert.Contains(".ToDateTime(global::System.TimeOnly.MinValue)", method);
            Assert.Contains(".ToTimeSpan()", method);
            Assert.Contains("DbType.DateTimeOffset", method);
            Assert.DoesNotContain("DbType.Time;", method);
        }
        var datePredicate = Method(text, "PageAsync");
        Assert.Contains("date.ToDateTime(global::System.TimeOnly.MinValue)", datePredicate);
        var timePredicate = Method(text, "ByTimeAsync");
        Assert.Contains("time.ToTimeSpan()", timePredicate);
        var directKeyset = Method(text, "SeekAsync");
        Assert.Contains("after.Value.ToDateTime(global::System.TimeOnly.MinValue)", directKeyset);
        var convertedKeyset = Method(text, "SeekConvertedAsync");
        Assert.Contains("BusinessDateConverter>.Instance.ToProvider(after.Value)", convertedKeyset);
        Assert.Contains(".ToDateTime(global::System.TimeOnly.MinValue)", convertedKeyset);
        Assert.Contains("after.HasValue ?", convertedKeyset);

        var collection = Method(text, "InDatesAsync");
        Assert.DoesNotContain(".ToDateTime(global::System.TimeOnly.MinValue)", collection);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void NonOracleTemporalValuesRemainUnbridged(string dialect)
    {
        var result = RunGenerator(OracleTemporalParameterSource, dialect: dialect);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("TemporalStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();
        Assert.DoesNotContain(".ToDateTime(global::System.TimeOnly.MinValue)", text);
        Assert.DoesNotContain(".ToTimeSpan()", text);
        Assert.Contains("DbType.Time;", text);
        Assert.Contains("_p0.Value = (object?)after ?? global::System.DBNull.Value;", Method(text, "SeekAsync"));
    }

    [Fact]
    public void OracleEagerTemporalKeysBridgeInlineValuesAfterConverters()
    {
        var result = RunGenerator(OracleTemporalEagerSource, dialect: "Oracle");
        AssertNoErrors(result);

        var directGrid = OracleGeneratedStoreText(result, "DateParentStore");
        var convertedGrid = OracleGeneratedStoreText(result, "ConvertedDateParentStore");
        var directSeparate = OracleGeneratedStoreText(result, "TimeParentStore");
        var convertedSeparate = OracleGeneratedStoreText(result, "ConvertedTimeParentStore");

        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"iq1$Idxxxx$30d4cf864d6e68\", (object)id.ToDateTime(global::System.TimeOnly.MinValue), global::System.Data.DbType.Date)", directGrid);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"iq1$Parent$b4df331386b214\", (object)id.ToDateTime(global::System.TimeOnly.MinValue), global::System.Data.DbType.Date)", directGrid);

        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"iq1$Idxxxx$30d4cf864d6e68\", (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.BusinessDateConverter>.Instance.ToProvider(id).ToDateTime(global::System.TimeOnly.MinValue), global::System.Data.DbType.Date)", convertedGrid);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"iq1$Parent$b4df331386b214\", (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.BusinessDateConverter>.Instance.ToProvider(id).ToDateTime(global::System.TimeOnly.MinValue), global::System.Data.DbType.Date)", convertedGrid);

        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"iq1$Idxxxx$30d4cf864d6e68\", (object)id.ToTimeSpan())", directSeparate);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"iq1$Idxxxx$30d4cf864d6e68\", (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.BusinessDateConverter>.Instance.ToProvider(_entity.OwnerId).ToDateTime(global::System.TimeOnly.MinValue), global::System.Data.DbType.Date)", directSeparate);

        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"iq1$Idxxxx$30d4cf864d6e68\", (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.BusinessTimeConverter>.Instance.ToProvider(id).ToTimeSpan())", convertedSeparate);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"iq1$Idxxxx$30d4cf864d6e68\", (object)_entity.OwnerId.ToTimeSpan())", convertedSeparate);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void NonOracleEagerTemporalKeysKeepRawAndConverterProviderExpressions(string dialect)
    {
        var result = RunGenerator(OracleTemporalEagerSource, dialect: dialect);
        AssertNoErrors(result);

        var directGrid = OracleGeneratedStoreText(result, "DateParentStore");
        var convertedGrid = OracleGeneratedStoreText(result, "ConvertedDateParentStore");
        var directSeparate = OracleGeneratedStoreText(result, "TimeParentStore");
        var convertedSeparate = OracleGeneratedStoreText(result, "ConvertedTimeParentStore");

        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@Id\", id, global::System.Data.DbType.Date)", directGrid);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@ParentId\", id, global::System.Data.DbType.Date)", directGrid);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@Id\", (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.BusinessDateConverter>.Instance.ToProvider(id), global::System.Data.DbType.Date)", convertedGrid);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@ParentId\", (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.BusinessDateConverter>.Instance.ToProvider(id), global::System.Data.DbType.Date)", convertedGrid);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@Id\", id, global::System.Data.DbType.Time)", directSeparate);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@Id\", (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.BusinessDateConverter>.Instance.ToProvider(_entity.OwnerId), global::System.Data.DbType.Date)", directSeparate);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@Id\", (object)global::Inquiry.Entities.InquiryConverterCache<global::Demo.BusinessTimeConverter>.Instance.ToProvider(id), global::System.Data.DbType.Time)", convertedSeparate);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@Id\", _entity.OwnerId, global::System.Data.DbType.Time)", convertedSeparate);

        foreach (var text in new[] { directGrid, convertedGrid, directSeparate, convertedSeparate })
        {
            Assert.DoesNotContain(".ToDateTime(global::System.TimeOnly.MinValue)", text);
            Assert.DoesNotContain(".ToTimeSpan()", text);
        }
    }

    private static string OracleGeneratedStoreText(GeneratorTestResult result, string storeName)
    {
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            t => string.Equals(
                global::System.IO.Path.GetFileName(t.FilePath),
                $"{storeName}.InquiryStore.g.cs",
                StringComparison.Ordinal));
        return tree.GetText().ToString();
    }
}
