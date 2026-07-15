using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>Task&lt;InquiryPagedResult&lt;T&gt;&gt;</c> return shape on <c>[InquirySelectAll]</c> and
/// <c>[InquirySelectAllByField]</c>: generates a paired SELECT + COUNT sharing the same WHERE
/// clause so page count and result set cannot diverge.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void PagedResultSelectAllEmitsPairedCountConst()
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
            public partial Task<Inquiry.Paging.InquiryPagedResult<User>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result);

        Assert.Contains("_sqlCount_PageAsync", text);
        Assert.Contains("COUNT(*)", text);
        Assert.Contains("await Inquiry.QueryListAsync<", text);
        Assert.Contains("await Inquiry.ExecuteScalarAsync<long, byte>(", text);
        Assert.Contains("new global::Inquiry.Paging.InquiryPagedResult<global::Demo.User>(_items, _total)", text);
    }

    [Fact]
    public void PagedResultByFieldCountSharesWhereClause()
    {
        var source = PagingStore("""
            [InquirySelectAllByField("Name", OrderBy = "Id ASC", Paged = true)]
            public partial Task<Inquiry.Paging.InquiryPagedResult<User>> PageByNameAsync(string name, int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result);

        // Both select and count consts contain the field WHERE clause.
        Assert.Contains("_sql_PageByNameAsync", text);
        Assert.Contains("_sqlCount_PageByNameAsync", text);

        // The count SQL uses the same WHERE as the select.
        var countConstMatch = text.Split("_sqlCount_PageByNameAsync = \"")[1].Split("\";")[0];
        Assert.Contains("WHERE", countConstMatch);
        Assert.Contains("@Name", countConstMatch);
        Assert.Contains("COUNT", countConstMatch);
        Assert.DoesNotContain("ORDER BY", countConstMatch);
        Assert.DoesNotContain("OFFSET", countConstMatch);
        Assert.DoesNotContain("LIMIT", countConstMatch);
    }

    [Theory]
    [InlineData("Sqlite", "COUNT(*)")]
    [InlineData("SqlServer", "COUNT_BIG(*)")]
    public void PagedResultCountUsesDialectCountExpression(string dialect, string countExpr)
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
            public partial Task<Inquiry.Paging.InquiryPagedResult<User>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var text = GetStore(result);

        var countConstMatch = text.Split("_sqlCount_PageAsync = \"")[1].Split("\";")[0];
        Assert.Contains(countExpr, countConstMatch);
    }

    [Fact]
    public void PagedResultReturnShapeImpliesPagination()
    {
        // Return shape alone implies offset paging — Paged=true is redundant but harmless.
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Id ASC")]
            public partial Task<Inquiry.Paging.InquiryPagedResult<User>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result);

        Assert.Contains("_sqlCount_PageAsync", text);
        Assert.Contains("new global::Inquiry.Paging.InquiryPagedResult<global::Demo.User>(_items, _total)", text);
    }

    [Fact]
    public void PagedResultWithoutOrderByReportsInq020()
    {
        var source = PagingStore("""
            [InquirySelectAll]
            public partial Task<Inquiry.Paging.InquiryPagedResult<User>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ020");
    }

    [Fact]
    public void PagedResultWithDistinctReportsInq083()
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Id ASC", Distinct = true)]
            public partial Task<Inquiry.Paging.InquiryPagedResult<User>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ083");
    }

    [Fact]
    public void PagedResultSoftDeleteCountIncludesActiveFilter()
    {
        const string softDeleteSource = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Paging;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TItem")]
            public sealed class Item
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn, InquirySoftDelete]
                public bool IsDeleted { get; set; }
            }

            public partial class ItemStore : InquiryStore<Item>
            {
                [InquirySelectAll(OrderBy = "Id ASC")]
                public partial Task<InquiryPagedResult<Item>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(softDeleteSource);
        AssertNoErrors(result);

        var store = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = store.GetText().ToString();

        // Both select and count consts must include the soft-delete filter.
        var selectConst = text.Split("_sql_PageAsync = \"")[1].Split("\";")[0];
        var countConst = text.Split("_sqlCount_PageAsync = \"")[1].Split("\";")[0];

        Assert.Contains("IsDeleted", selectConst);
        Assert.Contains("IsDeleted", countConst);
    }
}
