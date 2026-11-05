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
        var text = GetStore(result).Replace("\r", "");

        Assert.Contains("_sqlCount_PageAsync", text);
        Assert.Contains("COUNT(*)", text);

        // #403: the page SELECT and the COUNT travel as one multi-result command, bound once and read
        // through the grid reader, so the method makes one round trip on one connection.
        Assert.Contains("""
                    var _sql = _sql_PageAsync + ";" + _sqlCount_PageAsync;
                    var _cmd = new global::Inquiry.Commands.InquiryGeneratedCommand<(int Arg0, int Arg1)>(
                        _sql,
                        (Arg0: offset, Arg1: limit),
            """, text);
        Assert.Contains("""
                            _c.Parameters.Add(_p1);
                        });
                    await using var _grid = await Inquiry.QueryMultipleAsync<(int Arg0, int Arg1)>(_cmd, cancellationToken).ConfigureAwait(false);
                    var _items = await _grid.ReadListAsync<global::Demo.User, global::Demo.UserInquiryEntityStructMaterializer>(default, cancellationToken).ConfigureAwait(false);
                    var _total = await _grid.ReadScalarAsync<long>(cancellationToken).ConfigureAwait(false);
                    return new global::Inquiry.Paging.InquiryPagedResult<global::Demo.User>(_items, _total);
                }
            """, text);
        Assert.Equal(1, CountOccurrences(text, "new global::Inquiry.Commands.InquiryGeneratedCommand<"));
        Assert.DoesNotContain("ExecuteScalarAsync", text);
        Assert.DoesNotContain("Inquiry.QueryListAsync<", text);
        Assert.DoesNotContain("_countCmd", text);
    }

    [Theory]
    [InlineData("Sqlite", "        var _sql = _sql_PageAsync + \";\" + _sqlCount_PageAsync;")]
    [InlineData("SqlServer", "        var _sql = _sql_PageAsync + \";\" + _sqlCount_PageAsync;")]
    [InlineData("PostgreSql", "        var _sql = _sql_PageAsync + \";\" + _sqlCount_PageAsync;")]
    [InlineData("MySql", "        var _sql = _sql_PageAsync + \";\" + _sqlCount_PageAsync;")]
    [InlineData("MariaDb", "        var _sql = _sql_PageAsync + \";\" + _sqlCount_PageAsync;")]
    [InlineData("Oracle", "        var _sql = \"DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR \" + _sql_PageAsync + \"; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR \" + _sqlCount_PageAsync + \"; DBMS_SQL.RETURN_RESULT(c); END;\";")]
    public void PagedResultComposesOneMultiResultCommandPerDialect(string dialect, string expectedSqlLine)
    {
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
            public partial Task<Inquiry.Paging.InquiryPagedResult<User>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var text = GetStore(result).Replace("\r", "");

        Assert.Contains(expectedSqlLine + "\n", text);
        Assert.Contains("await using var _grid = await Inquiry.QueryMultipleAsync<(int Arg0, int Arg1)>(_cmd, cancellationToken).ConfigureAwait(false);", text);
        Assert.Contains("var _total = await _grid.ReadScalarAsync<long>(cancellationToken).ConfigureAwait(false);", text);
        Assert.DoesNotContain("ExecuteScalarAsync", text);
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

        // One command binds @Name once; the COUNT statement reuses it.
        text = text.Replace("\r", "");
        Assert.Contains("""
                    var _sql = _sql_PageByNameAsync + ";" + _sqlCount_PageByNameAsync;
                    var _cmd = new global::Inquiry.Commands.InquiryGeneratedCommand<(string Arg0, int Arg1, int Arg2)>(
                        _sql,
            """, text);
        Assert.Equal(1, CountOccurrences(text, "ParameterName = \"@Name\";"));
        Assert.Contains("""
                            _c.Parameters.Add(_p2);
                        });
                    await using var _grid = await Inquiry.QueryMultipleAsync<(string Arg0, int Arg1, int Arg2)>(_cmd, cancellationToken).ConfigureAwait(false);
                    var _items = await _grid.ReadListAsync<global::Demo.User, global::Demo.UserInquiryEntityStructMaterializer>(default, cancellationToken).ConfigureAwait(false);
                    var _total = await _grid.ReadScalarAsync<long>(cancellationToken).ConfigureAwait(false);
                    return new global::Inquiry.Paging.InquiryPagedResult<global::Demo.User>(_items, _total);
            """, text);
        Assert.DoesNotContain("_countCmd", text);
    }

    [Fact]
    public void PagedResultByPredicateComposesOneMultiResultCommand()
    {
        var source = PagingStore("""
            [InquirySelectAllByPredicate(OrderBy = "Id ASC", Paged = true)]
            [InquiryWhere("Name")]
            public partial Task<Inquiry.Paging.InquiryPagedResult<User>> PageByNameAsync(string name, int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetStore(result).Replace("\r", "");

        Assert.Contains("""
                    var _sql = _sql_PageByNameAsync + ";" + _sqlCount_PageByNameAsync;
                    var _cmd = new global::Inquiry.Commands.InquiryGeneratedCommand<(string Arg0, int Arg1, int Arg2)>(
                        _sql,
            """, text);
        Assert.Equal(1, CountOccurrences(text, "ParameterName = \"@Name\";"));
        Assert.Contains("""
                    });
                    await using var _grid = await Inquiry.QueryMultipleAsync<(string Arg0, int Arg1, int Arg2)>(_cmd, cancellationToken).ConfigureAwait(false);
                    var _items = await _grid.ReadListAsync<global::Demo.User, global::Demo.UserInquiryEntityStructMaterializer>(default, cancellationToken).ConfigureAwait(false);
                    var _total = await _grid.ReadScalarAsync<long>(cancellationToken).ConfigureAwait(false);
                    return new global::Inquiry.Paging.InquiryPagedResult<global::Demo.User>(_items, _total);
            """, text);
        Assert.Equal(1, CountOccurrences(text, "new global::Inquiry.Commands.InquiryGeneratedCommand<"));
        Assert.DoesNotContain("ExecuteScalarAsync", text);
        Assert.DoesNotContain("_countCmd", text);
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
