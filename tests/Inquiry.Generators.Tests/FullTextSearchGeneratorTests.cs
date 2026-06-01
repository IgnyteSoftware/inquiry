using System;
using System.Linq;

namespace Inquiry.Generators.Tests;

/// <summary>
/// W9 full-text search emission tests: per-dialect FTS predicates (PostgreSQL to_tsvector/@@,
/// SQL Server FREETEXT, MySQL MATCH…AGAINST) bound to a single @searchTerm, and the INQ035 rejection
/// on dialects that don't support it (SQLite). Live execution is gated/documented (requires a
/// full-text index), so these verify the emitted SQL.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string DocEntity = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TDoc")]
        public sealed class Doc
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Title")]
            public string Title { get; set; } = string.Empty;

            [InquiryColumn("Body")]
            public string Body { get; set; } = string.Empty;
        }
        """;

    private static string FtsStore(string methods) =>
        DocEntity + "\n\npublic partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>\n{\n" + methods + "\n}\n";

    private static string GetDocStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    private const string SearchMethod = """
        [InquiryFullTextSearch("Title", "Body")]
        public partial Task<IReadOnlyList<Doc>> SearchAsync(string term, CancellationToken cancellationToken = default);
        """;

    [Fact]
    public void PostgreSqlFullTextSearchUsesTsVector()
    {
        var result = RunGenerator(FtsStore(SearchMethod), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetDocStore(result);

        Assert.Contains("to_tsvector('simple', coalesce(", text);
        Assert.Contains("@@ plainto_tsquery('simple', @searchTerm)", text);
        Assert.Contains("_p.ParameterName = \"@searchTerm\";", text);
    }

    [Fact]
    public void SqlServerFullTextSearchUsesFreetext()
    {
        var result = RunGenerator(FtsStore(SearchMethod), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetDocStore(result);

        Assert.Contains("FREETEXT(([Title], [Body]), @searchTerm)", text);
    }

    [Fact]
    public void MySqlFullTextSearchUsesMatchAgainst()
    {
        var result = RunGenerator(FtsStore(SearchMethod), dialect: "MySql");
        AssertNoErrors(result);
        var text = GetDocStore(result);

        Assert.Contains("MATCH(`Title`, `Body`) AGAINST (@searchTerm IN NATURAL LANGUAGE MODE)", text);
    }

    [Fact]
    public void SqliteRejectsFullTextSearchWithInq035()
    {
        // SQLite (the default dialect) does not support [InquiryFullTextSearch] in v1.
        var result = RunGenerator(FtsStore(SearchMethod));
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ035");
    }

    [Fact]
    public void UnmappedFullTextColumnReportsInq007()
    {
        var result = RunGenerator(FtsStore("""
            [InquiryFullTextSearch("Nonexistent")]
            public partial Task<IReadOnlyList<Doc>> SearchAsync(string term, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ007");
    }
}
