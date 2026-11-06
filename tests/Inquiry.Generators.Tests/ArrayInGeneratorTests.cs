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
        Assert.DoesNotContain("InquiryInExpansion.Expand(", generatedText);
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
                [InquiryDelete, InquiryWhere("Id", Compare.In)]
                public partial Task<int> DeleteAllAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "PostgreSql");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("\\\"Id\\\" = ANY(@Id)", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryArrayParameter.Bind(_c, \"@Id\", _args.Arg0);", text);
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
                [InquiryDelete]
                [InquiryWhere("Category", Compare.In)]
                public partial Task<int> DeleteCategoriesAsync(IReadOnlyList<string> categories, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "PostgreSql");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("\\\"Category\\\" = ANY(@Category)", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryArrayParameter.Bind(_c, \"@Category\", _args.Arg0);", text);
    }

    [Fact]
    public void SqliteSmallInListTriesScalarBucketsBeforeJsonArray()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        // The const statement keeps the JSON transport; small lists swap only the IN fragment at run time.
        Assert.Contains(
            "private const string _sqlPredicate_InCategoriesAsync = \"SELECT \\\"Id\\\", \\\"ProductName\\\", \\\"UnitPrice\\\", \\\"UnitsInStock\\\", \\\"CategoryId\\\", \\\"Discontinued\\\" FROM \\\"Products\\\" WHERE \\\"CategoryId\\\" IN (SELECT value FROM json_each(@CategoryId))\";",
            generatedText);
        Assert.Contains(
            "private static readonly string[] _inNames_InCategoriesAsync_0 = { \"@CategoryId_in0\", \"@CategoryId_in1\", \"@CategoryId_in2\", \"@CategoryId_in3\" };",
            generatedText);
        Assert.Contains(
            "private static readonly string[] _inBuckets_InCategoriesAsync_0 = { \"\\\"CategoryId\\\" IN (@CategoryId_in0)\", \"\\\"CategoryId\\\" IN (@CategoryId_in0, @CategoryId_in1)\", \"\\\"CategoryId\\\" IN (@CategoryId_in0, @CategoryId_in1, @CategoryId_in2, @CategoryId_in3)\" };",
            generatedText);
        Assert.Contains(
            "if (!global::Inquiry.Parameters.InquiryInExpansion.TryBindScalarList(_c, categoryIds, _args.MaxParameters, \"\\\"CategoryId\\\" IN (SELECT value FROM json_each(@CategoryId))\", _inBuckets_InCategoriesAsync_0, _inNames_InCategoriesAsync_0, static _e => (object?)_e ?? global::System.DBNull.Value, static _p => { _p.DbType = global::System.Data.DbType.Int32; }))",
            generatedText);
        Assert.Contains("    global::Inquiry.Parameters.InquiryJsonArrayParameter.Bind(_c, \"@CategoryId\", categoryIds);", generatedText);
    }

    [Fact]
    public void PostgreSqlInListKeepsArrayParameterForEveryLength()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: "PostgreSql");
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        Assert.DoesNotContain("TryBindScalarList", generatedText);
        Assert.DoesNotContain("_inNames_", generatedText);
    }

    [Fact]
    public void SmallInListIsNotTriedWhenTheMethodAlsoHasNotIn()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                [InquiryWhere("UnitsInStock", Compare.NotIn)]
                public partial Task<IReadOnlyList<Product>> MixedAsync(IReadOnlyList<int> categoryIds, IReadOnlyList<short> stock, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        // NOT IN expansion picks its element names at run time, so fixed scalar names stay out of this method.
        Assert.DoesNotContain("TryBindScalarList", generatedText);
        Assert.Contains("global::Inquiry.Parameters.InquiryJsonArrayParameter.Bind(_c, \"@CategoryId\", categoryIds);", generatedText);
        Assert.Contains("global::Inquiry.Parameters.InquiryInExpansion.ExpandNotIn(_c, \"@UnitsInStock\", stock, _args.MaxParameters", generatedText);
    }

    [Fact]
    public void SmallInListElementNamesMoveAwayFromOtherBindNames()
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
                public int Code { get; set; }

                [InquiryColumn]
                public int Code_in0 { get; set; }
            }

            public partial class ItemStore : Inquiry.Stores.InquiryStore<Demo.Item>
            {
                [InquirySelectAllByPredicate]
                [InquiryWhere("Code", Compare.In)]
                [InquiryWhere("Code_in0", Compare.Equal)]
                public partial Task<IReadOnlyList<Item>> FindAsync(IReadOnlyList<int> codes, int other, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // The criterion keeps its own name; the element name that would collide with it moves.
        Assert.Contains("_p1.ParameterName = \"@Code_in0\";", text);
        Assert.Contains("private static readonly string[] _inNames_FindAsync_0 = { \"@Code_in02\", \"@Code_in1\", ", text);
        // The scalar criterion binds after the collection, so its parameter is reserved from the budget.
        Assert.Contains("TryBindScalarList(_c, codes, _args.MaxParameters - 1, ", text);
    }

    [Fact]
    public void SqlServerSmallInListBindsScalarsOnlyForTypesThatCompareTheSameAsTheTvp()
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

            [InquiryTable("Reading")]
            public sealed class Reading
            {
                [InquiryKey(IsGenerated = true)] public long Id { get; set; }
                [InquiryColumn] public int Code { get; set; }
                [InquiryColumn(Length = 20)] public string Name { get; set; } = string.Empty;
                [InquiryColumn(SqlType = "varchar(20)")] public string Tag { get; set; } = string.Empty;
                [InquiryColumn] public decimal Amount { get; set; }
                [InquiryColumn(Scale = 3)] public DateTimeOffset CapturedAt { get; set; }
                [InquiryColumn] public Guid Token { get; set; }
                [InquiryColumn] public char Grade { get; set; }
            }

            public partial class ReadingStore : Inquiry.Stores.InquiryStore<Demo.Reading>
            {
                [InquirySelectAllByPredicate, InquiryWhere("Code", Compare.In)]
                public partial Task<IReadOnlyList<Reading>> ByCodesAsync(IReadOnlyList<int> codes, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("Name", Compare.In)]
                public partial Task<IReadOnlyList<Reading>> ByNamesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("Tag", Compare.In)]
                public partial Task<IReadOnlyList<Reading>> ByTagsAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("Amount", Compare.In)]
                public partial Task<IReadOnlyList<Reading>> ByAmountsAsync(IReadOnlyList<decimal> amounts, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("CapturedAt", Compare.In)]
                public partial Task<IReadOnlyList<Reading>> ByTimesAsync(IReadOnlyList<DateTimeOffset> times, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("Token", Compare.In)]
                public partial Task<IReadOnlyList<Reading>> ByTokensAsync(IReadOnlyList<Guid> tokens, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("Grade", Compare.In)]
                public partial Task<IReadOnlyList<Reading>> ByGradesAsync(IReadOnlyList<char> grades, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ReadingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Only integers compare the same as a scalar and as a TVP row.
        Assert.Contains("TryBindScalarList(_c, codes, ", text);
        // The TVP sets each string's width, rounds decimals to DECIMAL(18,2) and times to DATETIMEOFFSET(3), and
        // an explicit SqlType sets its own column type, so those lists keep the TVP for every length.
        Assert.DoesNotContain("TryBindScalarList(_c, names, ", text);
        Assert.DoesNotContain("TryBindScalarList(_c, tags, ", text);
        Assert.DoesNotContain("TryBindScalarList(_c, amounts, ", text);
        Assert.DoesNotContain("TryBindScalarList(_c, times, ", text);
        Assert.DoesNotContain("TryBindScalarList(_c, tokens, ", text);
        Assert.DoesNotContain("TryBindScalarList(_c, grades, ", text);
        Assert.Contains("InquiryTvpParameter.Bind(_c, \"@Amount\", amounts, ", text);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void ShortStringInListKeepsTheCollectionTransportOnEveryProvider(string dialect)
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("ProductName", Compare.In)]
                public partial Task<IReadOnlyList<Product>> ByNamesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: dialect);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        // Each transport converts a string to its own SQL type, width and collation, so only integers bind as scalars (#509).
        Assert.DoesNotContain("TryBindScalarList", generatedText);
        Assert.DoesNotContain("_inNames_", generatedText);
    }
}
