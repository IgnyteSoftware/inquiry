using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// W2 ORDER BY + pagination emission tests: exact const SQL for ORDER BY, offset paging
/// (SQLite/PostgreSql/MySql LIMIT…OFFSET vs SqlServer OFFSET…FETCH), and keyset paging
/// (row-value default vs SqlServer lexicographic OR-form), plus the W2 diagnostics.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string PagingEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Paging;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TUser")]
        public sealed class User
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn]
            public DateTime CreatedAt { get; set; }
        }
        """;

    private static string PagingStore(string methods) =>
        PagingEntity + "\n\npublic partial class UserStore : Inquiry.Stores.InquiryStore<Demo.User>\n{\n" + methods + "\n}\n";

    private static string GetStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("UserStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    private static void AssertNoErrors(GeneratorTestResult result)
    {
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);
    }

    [Fact]
    public void OrderByEmitsQuotedMultiColumnClause()
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Name ASC, Id DESC")]
            public partial Task<IReadOnlyList<User>> SelectOrderedAsync(CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result);

        Assert.Contains(
            "private const string _sql_SelectOrderedAsync = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"CreatedAt\\\" FROM \\\"TUser\\\" ORDER BY \\\"Name\\\" ASC, \\\"Id\\\" DESC\";",
            text);
    }

    [Theory]
    [InlineData("Sqlite", "LIMIT @__limit OFFSET @__offset")]
    [InlineData("PostgreSql", "LIMIT @__limit OFFSET @__offset")]
    [InlineData("MySql", "LIMIT @__limit OFFSET @__offset")]
    [InlineData("SqlServer", "OFFSET @__offset ROWS FETCH NEXT @__limit ROWS ONLY")]
    public void OffsetPagingEmitsDialectClause(string dialect, string tail)
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
            public partial Task<IReadOnlyList<User>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var text = GetStore(result);

        // Identifier quoting differs per dialect; assert the ORDER BY + the dialect pagination tail.
        Assert.Contains("ORDER BY", text);
        Assert.Contains(tail.Replace("\"", "\\\""), text);
        Assert.Contains("_p0.ParameterName = \"@__offset\";", text);
        Assert.Contains("_p1.ParameterName = \"@__limit\";", text);
    }

    [Fact]
    public void KeysetSingleColumnEmitsCursorGuardAndPageSizePlusOne()
    {
        var source = PagingStore("""
            [InquiryKeysetPage("Id")]
            public partial Task<InquiryPage<User, long>> KeysetAsync(long? afterId, int pageSize, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result);

        Assert.Contains(
            "private const string _sql_KeysetAsync = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"CreatedAt\\\" FROM \\\"TUser\\\" WHERE (@__cursor0 IS NULL OR \\\"Id\\\" > @__cursor0) ORDER BY \\\"Id\\\" ASC LIMIT @__pageSize OFFSET 0\";",
            text);
        Assert.Contains("_p1.Value = pageSize + 1;", text);
        Assert.Contains("var _hasMore = _rows.Count > pageSize;", text);
        Assert.Contains("new global::Inquiry.Paging.InquiryPage<global::Demo.User, long>(_items, _next, _hasMore);", text);
    }

    [Fact]
    public void KeysetMultiColumnUsesRowValueByDefault()
    {
        var source = PagingStore("""
            [InquiryKeysetPage("CreatedAt", "Id")]
            public partial Task<InquiryPage<User, (System.DateTime, long)>> KeysetAsync((System.DateTime, long)? after, int pageSize, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result);

        Assert.Contains(
            "WHERE (@__cursor0 IS NULL OR (\\\"CreatedAt\\\", \\\"Id\\\") > (@__cursor0, @__cursor1))",
            text);
    }

    [Fact]
    public void KeysetMultiColumnUsesOrFormOnSqlServer()
    {
        var source = PagingStore("""
            [InquiryKeysetPage("CreatedAt", "Id")]
            public partial Task<InquiryPage<User, (System.DateTime, long)>> KeysetAsync((System.DateTime, long)? after, int pageSize, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetStore(result);

        // Lexicographic OR-form: (a > @c0) OR (a = @c0 AND b > @c1), bracketed and quoted with [].
        Assert.Contains(
            "WHERE (@__cursor0 IS NULL OR (([CreatedAt] > @__cursor0) OR ([CreatedAt] = @__cursor0 AND [Id] > @__cursor1)))",
            text);
    }

    [Fact]
    public void KeysetBackwardUsesLessThan()
    {
        var source = PagingStore("""
            [InquiryKeysetPage("Id", Direction = KeysetDirection.Backward)]
            public partial Task<InquiryPage<User, long>> KeysetAsync(long? afterId, int pageSize, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result);

        Assert.Contains("WHERE (@__cursor0 IS NULL OR \\\"Id\\\" < @__cursor0) ORDER BY \\\"Id\\\" DESC", text);
    }

    [Fact]
    public void OffsetPagingWithoutOrderByReportsInq020()
    {
        var source = PagingStore("""
            [InquirySelectAll(Paged = true)]
            public partial Task<IReadOnlyList<User>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ020");
    }

    [Fact]
    public void UnknownOrderFieldReportsInq021()
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Nope ASC")]
            public partial Task<IReadOnlyList<User>> SelectOrderedAsync(CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ021");
    }
}
