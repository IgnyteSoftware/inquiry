using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// JSON-path predicate emission ([InquiryWhere(JsonPath = …)]): a criterion filters inside a JSON text
/// column by comparing the dialect's path extraction against the bound parameter. Verifies the
/// per-dialect extraction syntax (SQLite json_extract, SqlServer/Oracle JSON_VALUE, MySQL
/// JSON_UNQUOTE(JSON_EXTRACT), PostgreSQL #>> with translated path), composition with an ordinary
/// criterion, the operator hooks (LIKE/IN), and the INQ060 validation diagnostics.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string CatalogEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TCatalog")]
        public sealed class Catalog
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            // A plain string column holding JSON text.
            [InquiryColumn("Data")]
            public string Data { get; set; } = string.Empty;
        }

        """;

    private static string CatalogStore(string methods) => CatalogEntity + """
        public partial class CatalogStore : Inquiry.Stores.InquiryStore<Demo.Catalog>
        {
        """ + "\n" + methods + "\n}\n";

    private static string GetCatalogStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("CatalogStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    private const string ByStatus = """
        [InquirySelectAllByPredicate]
        [InquiryWhere("Data", Compare.Equal, JsonPath = "$.status")]
        public partial Task<IReadOnlyList<Catalog>> ByStatusAsync(string status, CancellationToken cancellationToken = default);
        """;

    [Fact]
    public void JsonPathEqualExtractsAndCompares_Sqlite()
    {
        var result = RunGenerator(CatalogStore(ByStatus));
        AssertNoErrors(result);
        var text = GetCatalogStore(result);

        Assert.Contains("WHERE json_extract(\\\"Data\\\", '$.status') = @status", text);
    }

    [Fact]
    public void JsonPathComposesWithOrdinaryCriterion_Sqlite()
    {
        var result = RunGenerator(CatalogStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Name", Compare.Like)]
            [InquiryWhere("Data", Compare.Equal, JsonPath = "$.address.city")]
            public partial Task<IReadOnlyList<Catalog>> SearchAsync(string name, string city, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetCatalogStore(result);

        // Nested path, AND-composed after the ordinary LIKE criterion.
        Assert.Contains("\\\"Name\\\" LIKE @Name AND json_extract(\\\"Data\\\", '$.address.city') = @city", text);
    }

    [Fact]
    public void JsonPathLikeUsesExtraction_Sqlite()
    {
        var result = RunGenerator(CatalogStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Data", Compare.Like, JsonPath = "$.tag")]
            public partial Task<IReadOnlyList<Catalog>> ByTagAsync(string tag, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetCatalogStore(result);

        Assert.Contains("WHERE json_extract(\\\"Data\\\", '$.tag') LIKE @tag", text);
    }

    [Fact]
    public void SqlServerUsesJsonValue()
    {
        var result = RunGenerator(CatalogStore(ByStatus), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetCatalogStore(result);

        Assert.Contains("WHERE JSON_VALUE([Data], '$.status') = @status", text);
    }

    [Fact]
    public void OracleUsesJsonValueUnquoted()
    {
        var result = RunGenerator(CatalogStore(ByStatus), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetCatalogStore(result);

        Assert.Contains("WHERE JSON_VALUE(Data, '$.status') = :status", text);
    }

    [Fact]
    public void MySqlUsesJsonUnquoteExtract()
    {
        var result = RunGenerator(CatalogStore(ByStatus), dialect: "MySql");
        AssertNoErrors(result);
        var text = GetCatalogStore(result);

        Assert.Contains("WHERE JSON_UNQUOTE(JSON_EXTRACT(`Data`, '$.status')) = @status", text);
    }

    [Fact]
    public void PostgreSqlUsesPathOperatorWithTranslatedPath()
    {
        var result = RunGenerator(CatalogStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Data", Compare.Equal, JsonPath = "$.address.city")]
            public partial Task<IReadOnlyList<Catalog>> ByCityAsync(string city, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetCatalogStore(result);

        // $.address.city → #>> '{address,city}', column cast to jsonb.
        Assert.Contains("WHERE (\\\"Data\\\")::jsonb #>> '{address,city}' = @city", text);
    }

    [Fact]
    public void JsonPathOnNonStringColumnReportsINQ060()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TBad")]
            public sealed class Bad
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Count")]
                public int Count { get; set; }
            }

            public partial class BadStore : Inquiry.Stores.InquiryStore<Demo.Bad>
            {
                [InquirySelectAllByPredicate]
                [InquiryWhere("Count", Compare.Equal, JsonPath = "$.x")]
                public partial Task<IReadOnlyList<Bad>> ByXAsync(string x, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ060");
    }

    [Theory]
    [InlineData("status")]        // no leading $
    [InlineData("$")]             // bare root, no segment
    [InlineData("$.")]            // empty segment
    [InlineData("$.a.")]          // trailing dot
    [InlineData("$..a")]          // empty inner segment
    [InlineData("$.o'brien")]     // apostrophe — would break the single-quoted SQL literal
    [InlineData("$.items[0]")]    // array index — not uniformly translatable (PostgreSQL #>>)
    [InlineData("$.a b")]         // whitespace
    [InlineData("$.first-name")]  // hyphen — needs quoting on SqlServer/MySQL/Oracle (out of v1 scope)
    [InlineData("$.0name")]       // digit-leading — same quoting requirement
    public void MalformedJsonPathReportsINQ060(string path)
    {
        var result = RunGenerator(CatalogStore($$"""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Data", Compare.Equal, JsonPath = "{{path}}")]
            public partial Task<IReadOnlyList<Catalog>> ByStatusAsync(string status, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ060");
    }

    [Fact]
    public void UnderscoreJsonPathSegmentIsAccepted_Sqlite()
    {
        // Identifier segments may contain underscores and start with one.
        var result = RunGenerator(CatalogStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Data", Compare.Equal, JsonPath = "$.line_1")]
            public partial Task<IReadOnlyList<Catalog>> ByLineAsync(string line, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetCatalogStore(result);

        Assert.Contains("WHERE json_extract(\\\"Data\\\", '$.line_1') = @line_1", text);
    }
}
