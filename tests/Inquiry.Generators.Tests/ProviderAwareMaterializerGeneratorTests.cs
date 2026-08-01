namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void OracleReaderHookReachesEveryMaterializerFunnelAndReturning()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            public readonly struct BusinessDate { }
            public sealed class BusinessDateConverter : IInquiryValueConverter<BusinessDate, DateOnly>
            {
                public DateOnly ToProvider(BusinessDate value) => default;
                public BusinessDate FromProvider(DateOnly value) => default;
            }
            [InquiryTable("ProviderEvent")]
            public sealed class ProviderEvent
            {
                [InquiryKey(IsGenerated = true)] public int Id { get; set; }
                [InquiryColumn] public DateOnly EventDate { get; set; }
                [InquiryColumn] public TimeOnly EventTime { get; set; }
                [InquiryColumn(Converter = typeof(BusinessDateConverter))] public BusinessDate ConvertedDate { get; set; }
            }
            [InquiryProjection(typeof(ProviderEvent))]
            public sealed class EventProjection
            {
                [InquiryColumn("EventDate")] public DateOnly EventDate { get; set; }
            }
            [InquiryAdHoc]
            public sealed class EventReport { public TimeOnly EventTime { get; set; } }
            public partial class ProviderEventStore : InquiryStore<ProviderEvent>
            {
                [InquiryInsert(ReturnEntity = true)]
                public partial Task<ProviderEvent?> InsertReturningAsync(ProviderEvent value, CancellationToken ct = default);
                [InquirySelectAll]
                public partial Task<IReadOnlyList<EventProjection>> ProjectAsync(CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var generated = result.RunResult.GeneratedTrees.ToDictionary(
            static tree => tree.FilePath,
            static tree => tree.GetText().ToString());
        var entity = Assert.Single(generated, static pair => pair.Key.EndsWith("ProviderEvent.InquiryEntity.g.cs", StringComparison.Ordinal)).Value;
        var projection = Assert.Single(generated, static pair => pair.Key.EndsWith("EventProjection.InquiryProjection.g.cs", StringComparison.Ordinal)).Value;
        var adHoc = Assert.Single(generated, static pair => pair.Key.EndsWith("EventReport.InquiryAdHoc.g.cs", StringComparison.Ordinal)).Value;
        var store = Assert.Single(generated, static pair => pair.Key.EndsWith("ProviderEventStore.InquiryStore.g.cs", StringComparison.Ordinal)).Value;

        Assert.Contains("DateOnly.FromDateTime(reader.GetDateTime(1))", entity);
        Assert.Contains("TimeOnly.FromTimeSpan(reader.GetFieldValue<global::System.TimeSpan>(2))", entity);
        Assert.Contains("BusinessDateConverter>.Instance.FromProvider(global::System.DateOnly.FromDateTime(reader.GetDateTime(3)))", entity);
        Assert.Contains("DateOnly.FromDateTime(reader.GetDateTime(0))", projection);
        Assert.Contains("TimeOnly.FromTimeSpan(reader.GetFieldValue<global::System.TimeSpan>(0))", adHoc);
        Assert.Contains("ProviderEventInquiryEntityStructMaterializer", store);

        foreach (var text in new[] { entity, projection, adHoc })
        {
            Assert.DoesNotContain("reader.GetValue(", text);
            Assert.DoesNotContain("Convert.ChangeType", text);
            Assert.DoesNotContain("System.Reflection", text);
        }
    }
}
