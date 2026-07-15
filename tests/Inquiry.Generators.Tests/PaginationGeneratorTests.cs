using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// ORDER BY + pagination emission tests: exact const SQL for ORDER BY, offset paging
/// (SQLite/PostgreSql/MySql LIMIT…OFFSET vs SqlServer OFFSET…FETCH), and keyset paging
/// (row-value default vs SqlServer lexicographic OR-form), plus the pagination diagnostics.
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
    [InlineData("MariaDb", "LIMIT @__limit OFFSET @__offset")]
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
        // #61: the result list is pre-sized to the exact page limit.
        Assert.Contains("(_cmd, default, cancellationToken, capacityHint: limit)", text);
    }

    [Fact]
    public void KeysetSingleColumnEmitsSeekAndFirstPageQueries()
    {
        var source = PagingStore("""
            [InquiryKeysetPage("Id")]
            public partial Task<InquiryPage<User, long>> KeysetAsync(long? afterId, int pageSize, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result);

        // Two queries, not one (@cursor IS NULL OR ...) guard. The guard is non-sargable: it forces a full
        // table SCAN instead of an index SEARCH, so keyset paging degraded ~10x at scale (O(table) not
        // O(pageSize)). The seek query uses a plain, sargable `key > @cursor`; the first-page query (null
        // cursor) drops the cursor predicate entirely. The method picks between them at runtime.
        Assert.Contains(
            "private const string _sql_KeysetAsync = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"CreatedAt\\\" FROM \\\"TUser\\\" WHERE \\\"Id\\\" > @__cursor0 ORDER BY \\\"Id\\\" ASC LIMIT @__pageSize OFFSET 0\";",
            text);
        Assert.Contains(
            "private const string _sql_KeysetAsync_first = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"CreatedAt\\\" FROM \\\"TUser\\\" ORDER BY \\\"Id\\\" ASC LIMIT @__pageSize OFFSET 0\";",
            text);
        // Null cursor -> first-page const; the cursor parameter binds only on the seek path.
        Assert.Contains("var _first = afterId is null;", text);
        Assert.Contains("_first ? _sql_KeysetAsync_first : _sql_KeysetAsync", text);
        Assert.Contains("if (!_first)", text);
        Assert.Contains("_p1.Value = pageSize + 1;", text);
        Assert.Contains("var _hasMore = _rows.Count > pageSize;", text);
        Assert.Contains("new global::Inquiry.Paging.InquiryPage<global::Demo.User, long>(_items, _next, _hasMore);", text);
        // #61: the over-fetch list is pre-sized to pageSize + 1, and the sentinel row is trimmed in place
        // (single RemoveAt, no second list / per-item copy).
        Assert.Contains("QueryListAsync<global::Demo.User, (long? Arg0, int Arg1), global::Demo.UserInquiryEntityStructMaterializer>(_cmd, default, cancellationToken, capacityHint: pageSize + 1)", text);
        Assert.Contains("new global::Inquiry.Commands.InquiryGeneratedCommand<(long? Arg0, int Arg1)>(", text);
        Assert.Contains("if (_hasMore) ((global::System.Collections.Generic.List<global::Demo.User>)_rows).RemoveAt(_rows.Count - 1);", text);
        Assert.DoesNotContain("new global::System.Collections.Generic.List<global::Demo.User>(_rows.Count - 1)", text);
    }

    [Fact]
    public void KeysetCursorParameterCarriesDbType()
    {
        // A null first-page cursor must still bind a typed parameter, else PostgreSQL cannot infer the
        // null parameter's type (42P08). The cursor column 'Id' is long -> DbType.Int64.
        var source = PagingStore("""
            [InquiryKeysetPage("Id")]
            public partial Task<InquiryPage<User, long>> KeysetAsync(long? afterId, int pageSize, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result);

        Assert.Contains("_p0.ParameterName = \"@__cursor0\";", text);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int64;", text);
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

        // Bare row-value seek predicate (no IS NULL guard); the null-cursor case is the separate _first const.
        Assert.Contains(
            "WHERE (\\\"CreatedAt\\\", \\\"Id\\\") > (@__cursor0, @__cursor1) ORDER BY",
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
        // No IS NULL guard — the bare seek predicate keeps the index usable; first page is the _first const.
        Assert.Contains(
            "WHERE (([CreatedAt] > @__cursor0) OR ([CreatedAt] = @__cursor0 AND [Id] > @__cursor1)) ORDER BY",
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

        Assert.Contains("WHERE \\\"Id\\\" < @__cursor0 ORDER BY \\\"Id\\\" DESC", text);
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

    // ---- OrderBy direction-token validation (audit P2 #13) ------------------------------
    //
    // The parser previously did a permissive `parts[1] == "DESC"` check — anything else (typo,
    // extra tokens, garbage) silently fell back to ASC, changing query semantics without warning.
    // INQ042 now flags any token after the field that isn't exactly ASC or DESC (case-insensitive),
    // and any term with more than two whitespace-separated tokens.

    [Fact]
    public void OrderByWithTypoDirectionTokenReportsInq042()
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Name DESCS")]
            public partial Task<IReadOnlyList<User>> SelectOrderedAsync(CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ042");
    }

    [Fact]
    public void OrderByWithUnknownDirectionTokenReportsInq042()
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Name DEC")]
            public partial Task<IReadOnlyList<User>> SelectOrderedAsync(CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ042");
    }

    [Fact]
    public void OrderByWithExtraTokensReportsInq042()
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Name ASC NULLS FIRST")]
            public partial Task<IReadOnlyList<User>> SelectOrderedAsync(CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ042");
    }

    [Fact]
    public void OrderByWithExplicitAscAcceptsTheTerm()
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Name ASC, Id DESC")]
            public partial Task<IReadOnlyList<User>> SelectOrderedAsync(CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
    }

    // ---- Pagination argument guards (audit P2 #12) -------------------------------------
    //
    // The emitted method bodies must validate offset/limit/pageSize up front so a misuse fails
    // with a clear ArgumentOutOfRangeException at the call site, not with a provider-specific
    // error (or, worst case, an integer overflow on the keyset `pageSize + 1` over-fetch).

    [Fact]
    public void OffsetPagingEmitsOffsetAndLimitGuards()
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
            public partial Task<IReadOnlyList<User>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result);

        Assert.Contains(
            "if (offset < 0) throw new global::System.ArgumentOutOfRangeException(nameof(offset), offset, \"Pagination offset must be >= 0.\");",
            text);
        Assert.Contains(
            "if (limit <= 0) throw new global::System.ArgumentOutOfRangeException(nameof(limit), limit, \"Pagination limit must be > 0.\");",
            text);
    }

    [Fact]
    public void KeysetPagingEmitsPageSizeGuards()
    {
        var source = PagingStore("""
            [InquiryKeysetPage("Id")]
            public partial Task<InquiryPage<User, long>> KeysetAsync(long? afterId, int pageSize, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result);

        Assert.Contains(
            "if (pageSize <= 0) throw new global::System.ArgumentOutOfRangeException(nameof(pageSize), pageSize, \"Page size must be > 0.\");",
            text);
        Assert.Contains(
            "if (pageSize == int.MaxValue) throw new global::System.ArgumentOutOfRangeException(nameof(pageSize), pageSize, \"Page size must be less than int.MaxValue.\");",
            text);
    }
}
