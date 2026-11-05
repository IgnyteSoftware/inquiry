namespace Inquiry.Generators.Tests;

/// <summary>
/// Ownership of the offset-paging bind names (#543). A mapped property or a JSON-path leaf can be
/// named <c>__offset</c> or <c>__limit</c>. Property names come first, the paging names avoid them,
/// and criterion names avoid the paging names. Declarations with no such collision keep their SQL;
/// the existing <c>@__offset</c>/<c>@__limit</c> assertions guard that.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string OffsetProperty = "    [InquiryColumn(\"PageOffset\")]\n    public int __offset { get; set; }\n";

    private static string PagingNameStore(string extraMembers, string methods, string extraTypes = "") => $$"""
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Paging;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TPage")]
        public sealed class Item
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Data")]
            public string Data { get; set; } = string.Empty;

        {{extraMembers}}
        }

        {{extraTypes}}

        public partial class ItemStore : InquiryStore<Item>
        {
        {{methods}}
        }
        """;

    private static string PagingNameStoreText(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    private static void AssertBindsInOrder(string body, params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            Assert.Contains($"_p{i}.ParameterName = \"{names[i]}\";", body, StringComparison.Ordinal);
        }

        Assert.DoesNotContain($"_p{names.Length}.ParameterName", body, StringComparison.Ordinal);
    }

    private const string OffsetFieldAndPageMethods = """
            [InquirySelectAllByField("__offset", OrderBy = "Id ASC", Paged = true)]
            public partial Task<IReadOnlyList<Item>> ByOffsetAsync(int value, int offset, int limit, CancellationToken cancellationToken = default);

            [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
            public partial Task<IReadOnlyList<Item>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
        """;

    [Fact]
    public void OffsetPagingNameAvoidsAMappedPropertyWithTheSameName_Sqlite()
    {
        var result = RunGenerator(PagingNameStore(OffsetProperty, OffsetFieldAndPageMethods));
        AssertNoErrors(result);
        var text = PagingNameStoreText(result);

        Assert.Equal(
            "SELECT \\\"Id\\\", \\\"Data\\\", \\\"PageOffset\\\" FROM \\\"TPage\\\" WHERE \\\"PageOffset\\\" = @__offset ORDER BY \\\"Id\\\" ASC LIMIT @__limit OFFSET @___offset",
            GeneratedConst(text, "_sql_ByOffsetAsync"));
        AssertBindsInOrder(GeneratedMethodBody(text, " ByOffsetAsync("), "@__offset", "@___offset", "@__limit");

        // The name belongs to the entity, so a select-all that binds no property moves it too.
        Assert.Equal(
            "SELECT \\\"Id\\\", \\\"Data\\\", \\\"PageOffset\\\" FROM \\\"TPage\\\" ORDER BY \\\"Id\\\" ASC LIMIT @__limit OFFSET @___offset",
            GeneratedConst(text, "_sql_PageAsync"));
        AssertBindsInOrder(GeneratedMethodBody(text, " PageAsync("), "@___offset", "@__limit");
    }

    [Theory]
    [InlineData("PostgreSql", "WHERE \"PageOffset\" = @__offset ORDER BY \"Id\" ASC LIMIT @__limit OFFSET @___offset")]
    [InlineData("MySql", "WHERE `PageOffset` = @__offset ORDER BY `Id` ASC LIMIT @__limit OFFSET @___offset")]
    [InlineData("MariaDb", "WHERE `PageOffset` = @__offset ORDER BY `Id` ASC LIMIT @__limit OFFSET @___offset")]
    [InlineData("SqlServer", "WHERE [PageOffset] = @__offset ORDER BY [Id] ASC OFFSET @___offset ROWS FETCH NEXT @__limit ROWS ONLY")]
    public void OffsetPagingNameAvoidsAMappedPropertyWithTheSameName(string dialect, string tail)
    {
        var result = RunGenerator(PagingNameStore(OffsetProperty, OffsetFieldAndPageMethods), dialect: dialect);
        AssertNoErrors(result);
        var text = PagingNameStoreText(result);

        Assert.EndsWith(tail.Replace("\"", "\\\"", StringComparison.Ordinal), GeneratedConst(text, "_sql_ByOffsetAsync"), StringComparison.Ordinal);
        AssertBindsInOrder(GeneratedMethodBody(text, " ByOffsetAsync("), "@__offset", "@___offset", "@__limit");
    }

    [Fact]
    public void OffsetPagingNameAvoidsAMappedPropertyWithTheSameName_Oracle()
    {
        var result = RunGenerator(PagingNameStore(OffsetProperty, OffsetFieldAndPageMethods), dialect: "Oracle");
        AssertNoErrors(result);
        var text = PagingNameStoreText(result);

        // iq1$offset$f951564368e298 encodes "__offset"; iq1$offset$b7408137253127 encodes "___offset".
        Assert.Equal(
            "SELECT Id, Data, PageOffset FROM TPage WHERE PageOffset = :iq1$offset$f951564368e298 ORDER BY Id ASC OFFSET :iq1$offset$b7408137253127 ROWS FETCH NEXT :iq1$limitx$d661ce51163918 ROWS ONLY",
            GeneratedConst(text, "_sql_ByOffsetAsync"));
        AssertBindsInOrder(
            GeneratedMethodBody(text, " ByOffsetAsync("),
            "iq1$offset$f951564368e298", "iq1$offset$b7408137253127", "iq1$limitx$d661ce51163918");
    }

    [Fact]
    public void LimitPagingNameAvoidsAMappedPropertyThatDiffersOnlyByCase_Sqlite()
    {
        var result = RunGenerator(PagingNameStore(
            "    [InquiryColumn(\"PageLimit\")]\n    public int __Limit { get; set; }\n",
            """
                [InquirySelectAllByField("__Limit", OrderBy = "Id ASC", Paged = true)]
                public partial Task<IReadOnlyList<Item>> ByLimitAsync(int value, int offset, int limit, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = PagingNameStoreText(result);

        Assert.EndsWith(
            "WHERE \\\"PageLimit\\\" = @__Limit ORDER BY \\\"Id\\\" ASC LIMIT @___limit OFFSET @__offset",
            GeneratedConst(text, "_sql_ByLimitAsync"),
            StringComparison.Ordinal);
        AssertBindsInOrder(GeneratedMethodBody(text, " ByLimitAsync("), "@__Limit", "@__offset", "@___limit");
    }

    [Fact]
    public void OffsetPagingNameKeepsMovingUntilItIsFree_Sqlite()
    {
        var result = RunGenerator(PagingNameStore(
            OffsetProperty + "\n    [InquiryColumn(\"PageOffset2\")]\n    public int ___OFFSET { get; set; }\n",
            """
                [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
                public partial Task<IReadOnlyList<Item>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = PagingNameStoreText(result);

        Assert.EndsWith("LIMIT @__limit OFFSET @____offset", GeneratedConst(text, "_sql_PageAsync"), StringComparison.Ordinal);
        AssertBindsInOrder(GeneratedMethodBody(text, " PageAsync("), "@____offset", "@__limit");
    }

    [Fact]
    public void CriterionOnAPagingNamedPropertyKeepsItsName_Sqlite()
    {
        var result = RunGenerator(PagingNameStore(OffsetProperty, """
                [InquirySelectAllByPredicate(OrderBy = "Id ASC", Paged = true)]
                [InquiryWhere("__offset")]
                public partial Task<IReadOnlyList<Item>> WhereOffsetAsync(int value, int offset, int limit, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = PagingNameStoreText(result);

        Assert.EndsWith(
            "WHERE \\\"PageOffset\\\" = @__offset ORDER BY \\\"Id\\\" ASC LIMIT @__limit OFFSET @___offset",
            GeneratedConst(text, "_sql_WhereOffsetAsync"),
            StringComparison.Ordinal);
        AssertBindsInOrder(GeneratedMethodBody(text, " WhereOffsetAsync("), "@__offset", "@___offset", "@__limit");
    }

    [Fact]
    public void JsonLeafNamedLikeAPagingNameMovesTheCriterionName_Sqlite()
    {
        var result = RunGenerator(PagingNameStore(string.Empty, """
                [InquirySelectAllByPredicate(OrderBy = "Id ASC", Paged = true)]
                [InquiryWhere("Data", Compare.Equal, JsonPath = "$.__limit")]
                public partial Task<InquiryPagedResult<Item>> WhereLimitLeafAsync(string value, int offset, int limit, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = PagingNameStoreText(result);

        Assert.EndsWith(
            "WHERE json_extract(\\\"Data\\\", '$.__limit') = @__limit2 ORDER BY \\\"Id\\\" ASC LIMIT @__limit OFFSET @__offset",
            GeneratedConst(text, "_sql_WhereLimitLeafAsync"),
            StringComparison.Ordinal);
        Assert.EndsWith(
            "WHERE json_extract(\\\"Data\\\", '$.__limit') = @__limit2",
            GeneratedConst(text, "_sqlCount_WhereLimitLeafAsync"),
            StringComparison.Ordinal);
        AssertBindsInOrder(GeneratedMethodBody(text, " WhereLimitLeafAsync("), "@__limit2", "@__offset", "@__limit");
    }

    [Fact]
    public void JsonLeafAvoidsAPagingNameIgnoringCaseEvenWhenAParameterUsesIt_Sqlite()
    {
        // The C# offset parameter is named like the paging bind name; the bind name is still reserved.
        var result = RunGenerator(PagingNameStore(string.Empty, """
                [InquirySelectAllByPredicate(OrderBy = "Id ASC", Paged = true)]
                [InquiryWhere("Data", Compare.Equal, JsonPath = "$.__OFFSET")]
                public partial Task<IReadOnlyList<Item>> WhereOffsetLeafAsync(string value, int __offset, int limit, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = PagingNameStoreText(result);

        Assert.EndsWith(
            "WHERE json_extract(\\\"Data\\\", '$.__OFFSET') = @__OFFSET2 ORDER BY \\\"Id\\\" ASC LIMIT @__limit OFFSET @__offset",
            GeneratedConst(text, "_sql_WhereOffsetLeafAsync"),
            StringComparison.Ordinal);
        AssertBindsInOrder(GeneratedMethodBody(text, " WhereOffsetLeafAsync("), "@__OFFSET2", "@__offset", "@__limit");
    }

    [Fact]
    public void ProjectionThatOmitsTheCollidingPropertyStillMovesThePagingName_Sqlite()
    {
        var result = RunGenerator(PagingNameStore(
            OffsetProperty,
            """
                [InquirySelectAllByField("__offset", OrderBy = "Id ASC", Paged = true)]
                public partial Task<IReadOnlyList<ItemData>> DataByOffsetAsync(int value, int offset, int limit, CancellationToken cancellationToken = default);
            """,
            """
            [InquiryProjection(typeof(Item))]
            public sealed record ItemData
            {
                [InquiryColumn("Data")]
                public string Data { get; init; } = string.Empty;
            }
            """));
        AssertNoErrors(result);
        var text = PagingNameStoreText(result);

        Assert.Equal(
            "SELECT \\\"Data\\\" FROM \\\"TPage\\\" WHERE \\\"PageOffset\\\" = @__offset ORDER BY \\\"Id\\\" ASC LIMIT @__limit OFFSET @___offset",
            GeneratedConst(text, "_sql_DataByOffsetAsync"));
        AssertBindsInOrder(GeneratedMethodBody(text, " DataByOffsetAsync("), "@__offset", "@___offset", "@__limit");
    }

    [Fact]
    public void KeysetPagingNamesStayFixedBesidePropertiesNamedLikeThem_Sqlite()
    {
        // Keyset methods bind no property-named value, so nothing can collide and nothing moves.
        var result = RunGenerator(PagingNameStore(
            "    [InquiryColumn(\"PageSize\")]\n    public int __pageSize { get; set; }\n\n    [InquiryColumn(\"Cursor0\")]\n    public int __cursor0 { get; set; }\n",
            """
                [InquiryKeysetPage("Id")]
                public partial Task<InquiryPage<Item, long>> KeysetAsync(long? afterId, int pageSize, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = PagingNameStoreText(result);

        Assert.Equal(
            "SELECT \\\"Id\\\", \\\"Data\\\", \\\"PageSize\\\", \\\"Cursor0\\\" FROM \\\"TPage\\\" WHERE \\\"Id\\\" > @__cursor0 ORDER BY \\\"Id\\\" ASC LIMIT @__pageSize OFFSET 0",
            GeneratedConst(text, "_sql_KeysetAsync"));
        Assert.Equal(
            "SELECT \\\"Id\\\", \\\"Data\\\", \\\"PageSize\\\", \\\"Cursor0\\\" FROM \\\"TPage\\\" ORDER BY \\\"Id\\\" ASC LIMIT @__pageSize OFFSET 0",
            GeneratedConst(text, "_sql_KeysetAsync_first"));
    }
}
