using System;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string CompositionEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Paging;
        using Inquiry.Stores;

        namespace Demo;

        public enum ItemState
        {
            Open,
            Closed,
        }

        [InquiryTable("TItem")]
        public sealed class Item
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn]
            public string Category { get; set; } = string.Empty;

            [InquiryColumn]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn]
            public int Score { get; set; }

            [InquiryColumn]
            [InquiryEnumAsString]
            public ItemState? State { get; set; }
        }

        [InquirySpecification]
        [InquiryWhere("Category")]
        [AttributeUsage(AttributeTargets.Method)]
        public sealed class ByCategoryAttribute : Attribute
        {
        }

        """;

    private static string CompositionStore(string methods) => CompositionEntity + """
        public partial class ItemStore : InquiryStore<Item>
        {
        """ + "\n" + methods + "\n}\n";

    [Fact]
    public void PredicateGroupsNegationAndOptionalValueRenderOneStaticShape()
    {
        var result = RunGenerator(CompositionStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Category", OpenGroups = 1, Not = true)]
            [InquiryWhere("Score", Compare.GreaterThan, Or = true, CloseGroups = 1)]
            [InquiryWhere("Name", Optional = true)]
            public partial Task<IReadOnlyList<Item>> SearchAsync(
                string category, int score, string? name, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        Assert.Contains("WHERE NOT (\\\"Category\\\" = @Category OR \\\"Score\\\" > @Score) AND (@Name IS NULL OR \\\"Name\\\" = @Name)", text);
    }

    [Fact]
    public void PredicateGroupsCanNest()
    {
        var result = RunGenerator(CompositionStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Category", OpenGroups = 1)]
            [InquiryWhere("Score", Compare.GreaterThan, OpenGroups = 1)]
            [InquiryWhere("Name", Or = true, CloseGroups = 1)]
            [InquiryWhere("Id", Or = true, CloseGroups = 1)]
            public partial Task<IReadOnlyList<Item>> SearchAsync(
                string category, int score, string name, long id, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        Assert.Contains("WHERE (\\\"Category\\\" = @Category AND (\\\"Score\\\" > @Score OR \\\"Name\\\" = @Name) OR \\\"Id\\\" = @Id)", text);
    }

    [Fact]
    public void ReusableSpecificationExpandsWhereCriteria()
    {
        var result = RunGenerator(CompositionStore("""
            [InquirySelectAllByPredicate]
            [ByCategory]
            public partial Task<IReadOnlyList<Item>> ByCategoryAsync(
                string category, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        Assert.Contains("WHERE \\\"Category\\\" = @Category", text);
    }

    [Fact]
    public void PredicatePagingUsesTheSamePredicateForRowsAndCount()
    {
        var result = RunGenerator(CompositionStore("""
            [InquirySelectAllByPredicate(OrderBy = "Score DESC", Paged = true)]
            [InquiryWhere("Category")]
            public partial Task<InquiryPagedResult<Item>> PageAsync(
                string category, int offset, int limit, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        Assert.Contains("private const string _sql_PageAsync", text);
        Assert.Contains("WHERE \\\"Category\\\" = @Category ORDER BY \\\"Score\\\" DESC", text);
        Assert.Contains("private const string _sqlCount_PageAsync = \"SELECT COUNT(*) FROM \\\"TItem\\\" WHERE \\\"Category\\\" = @Category\";", text);
        Assert.Contains("new global::Inquiry.Paging.InquiryPagedResult<global::Demo.Item>(_items, _total)", text);
    }

    [Fact]
    public void AggregateCanUseComposedPredicates()
    {
        var result = RunGenerator(CompositionStore("""
            [InquiryAggregate(InquiryAggregateFunction.Sum, "Score")]
            [InquiryWhere("Category")]
            public partial Task<int> SumAsync(string category, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        Assert.Contains("SELECT SUM(\\\"Score\\\") FROM \\\"TItem\\\" WHERE \\\"Category\\\" = @Category", text);
        Assert.Contains("_p0.ParameterName = \"@Category\";", text);
    }

    [Theory]
    [InlineData("Sqlite", "\\\"Score\\\" + @delta")]
    [InlineData("SqlServer", "[Score] + @delta")]
    [InlineData("PostgreSql", "\\\"Score\\\" + @delta")]
    [InlineData("MySql", "`Score` + @delta")]
    [InlineData("MariaDb", "`Score` + @delta")]
    [InlineData("Oracle", ":iq1$")]
    public void ExpressionSetRendersForEveryProvider(string dialect, string expectedExpression)
    {
        var result = RunGenerator(CompositionStore("""
            [InquiryUpdate]
            [InquirySet("Score", "{Score} + @delta")]
            [InquiryWhere("Category")]
            public partial Task<int> IncreaseAsync(
                int delta, string category, CancellationToken cancellationToken = default);
            """), dialect: dialect);
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        Assert.Contains(expectedExpression, text);
        Assert.Contains("_p0.Value = (object?)_args.Arg0 ?? global::System.DBNull.Value;", text);
    }

    [Fact]
    public void ExpressionSetWithoutParametersDoesNotEmitAnImplicitSetBinding()
    {
        var result = RunGenerator(CompositionStore("""
            [InquiryUpdate]
            [InquirySet("Score", "{Score} + 1")]
            [InquiryWhere("Category")]
            public partial Task<int> IncreaseAsync(
                string category, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        Assert.Contains("SET \\\"Score\\\" = \\\"Score\\\" + 1 WHERE \\\"Category\\\" = @Category", text);
        Assert.Contains("_p0.ParameterName = \"@Category\";", text);
        Assert.DoesNotContain("_p1", text);
    }

    [Fact]
    public void UnbalancedGroupsAreRejected()
    {
        var result = RunGenerator(CompositionStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Category", OpenGroups = 1)]
            public partial Task<IReadOnlyList<Item>> SearchAsync(
                string category, CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "INQ097");
    }

    [Fact]
    public void OptionalPredicateRequiresNullableScalarParameter()
    {
        var result = RunGenerator(CompositionStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Score", Optional = true)]
            public partial Task<IReadOnlyList<Item>> SearchAsync(
                int score, CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "INQ098");
    }

    [Fact]
    public void OptionalEnumPredicateBindsNullAsDatabaseNull()
    {
        var result = RunGenerator(CompositionStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("State", Optional = true)]
            public partial Task<IReadOnlyList<Item>> SearchAsync(
                ItemState? state, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        Assert.Contains("state.HasValue ? (object)state.Value.ToString() : global::System.DBNull.Value", text);
    }

    [Fact]
    public void ExpressionSetUsesTheParameterNullabilityForNullableColumns()
    {
        var result = RunGenerator(CompositionStore("""
            [InquiryUpdate]
            [InquirySet("State", "@state")]
            [InquiryWhere("Category")]
            public partial Task<int> SetStateAsync(
                ItemState state, string category, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        Assert.Contains("_p0.Value = (object)_args.Arg0.ToString();", text);
    }

    [Fact]
    public void SetTemplateMarkersInsideStringLiteralsRemainLiteral()
    {
        var result = RunGenerator(CompositionStore("""
            [InquiryUpdate]
            [InquirySet("Name", "'@literal {Name}'")]
            [InquiryWhere("Category")]
            public partial Task<int> RenameAsync(
                string category, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        Assert.Contains("SET \\\"Name\\\" = '@literal {Name}' WHERE \\\"Category\\\" = @Category", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{Missing} + @delta")]
    [InlineData("Missing + @delta")]
    [InlineData("{Score} + @delta; DELETE FROM TItem")]
    public void InvalidSetExpressionsAreRejected(string expression)
    {
        var result = RunGenerator(CompositionStore($$"""
            [InquiryUpdate]
            [InquirySet("Score", "{{expression}}")]
            [InquiryWhere("Category")]
            public partial Task<int> IncreaseAsync(
                int delta, string category, CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "INQ099");
    }
}
