using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Array IN parameter tests across dialects: PostgreSQL uses <c>= ANY(@name)</c> with native arrays,
/// SQLite uses <c>json_each(@name)</c>, SQL Server uses TVPs, Oracle uses <c>JSON_TABLE</c> — all
/// bind the collection as a single parameter (constant SQL, no per-element cap). MySQL keeps the
/// <c>IN (@name)</c> sentinel + runtime expansion.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void PostgreSqlInPredicateRendersAnyAndBindsArrayParameter()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: "PostgreSql");
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        // Constant SQL: = ANY(@CategoryId), no IN sentinel.
        Assert.Contains("\\\"CategoryId\\\" = ANY(@CategoryId)", generatedText);
        Assert.DoesNotContain("IN (@CategoryId)", generatedText);

        // The whole collection binds as one array parameter; no runtime text rewrite.
        Assert.Contains("global::Inquiry.Parameters.InquiryArrayParameter.Bind(_c, \"@CategoryId\", categoryIds);", generatedText);
        Assert.DoesNotContain("InquiryInExpansion", generatedText);
    }

    [Fact]
    public void SqliteInPredicateUsesJsonEachAndJsonArrayBinding()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        Assert.Contains("\\\"CategoryId\\\" IN (SELECT value FROM json_each(@CategoryId))", generatedText);
        Assert.Contains("global::Inquiry.Parameters.InquiryJsonArrayParameter.Bind(_c, \"@CategoryId\", categoryIds);", generatedText);
        Assert.DoesNotContain("InquiryInExpansion", generatedText);
    }

    [Fact]
    public void SqliteDateTimeInPredicateUsesJsonArrayBinding()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Event")]
            public sealed class Event
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn]
                public DateTime OccurredAt { get; set; }
            }

            public partial class EventStore : Inquiry.Stores.InquiryStore<Demo.Event>
            {
                [InquirySelectAllByPredicate]
                [InquiryWhere("OccurredAt", Compare.In)]
                public partial Task<IReadOnlyList<Event>> OnDatesAsync(IReadOnlyList<DateTime> dates, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("EventStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("global::Inquiry.Parameters.InquiryJsonArrayParameter.Bind(_c, \"@OccurredAt\", dates);", text);
    }

    [Fact]
    public void PostgreSqlDeleteAllByKeysRendersAnyAndBindsArrayParameter()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            public partial class ItemStore : Inquiry.Stores.InquiryStore<Demo.Item>
            {
                [InquiryDeleteAll]
                public partial Task<int> DeleteAllAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "PostgreSql");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("\\\"Id\\\" = ANY(@keys)", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryArrayParameter.Bind(_c, \"@keys\", ids);", text);
        Assert.DoesNotContain("InquiryInExpansion", text);
    }

    [Fact]
    public void PostgreSqlPredicateMutationInRendersAnyAndBindsArrayParameter()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn]
                public string Category { get; set; } = string.Empty;
            }

            public partial class ItemStore : Inquiry.Stores.InquiryStore<Demo.Item>
            {
                [InquiryDeleteWhere]
                [InquiryWhere("Category", Compare.In)]
                public partial Task<int> DeleteCategoriesAsync(IReadOnlyList<string> categories, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "PostgreSql");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("\\\"Category\\\" = ANY(@Category)", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryArrayParameter.Bind(_c, \"@Category\", categories);", text);
    }
}
