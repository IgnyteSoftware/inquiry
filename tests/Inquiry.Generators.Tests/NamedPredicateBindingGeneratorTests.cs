using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Named <c>[InquiryWhere(Parameter = …, UpperParameter = …)]</c> binding (#476): the attribute order
/// sets the SQL, the named parameter supplies each value, and INQ109/INQ112 guard the binding.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void NamedCriteriaKeepTheirColumnsWhenSameTypeParametersAreSwapped()
    {
        const string criteria = """
            [InquirySelectAllByPredicate]
            [InquiryWhere("Score", Compare.GreaterThanOrEqual, Parameter = nameof(minimum))]
            [InquiryWhere("Score", Compare.LessThanOrEqual, Parameter = nameof(maximum))]
            """;
        var declared = RunGenerator(CompositionStore(criteria + """
            public partial Task<IReadOnlyList<Item>> RangeAsync(int minimum, int maximum, CancellationToken cancellationToken = default);
            """));
        var swapped = RunGenerator(CompositionStore(criteria + """
            public partial Task<IReadOnlyList<Item>> RangeAsync(int maximum, int minimum, CancellationToken cancellationToken = default);
            """));
        var positional = RunGenerator(CompositionStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Score", Compare.GreaterThanOrEqual)]
            [InquiryWhere("Score", Compare.LessThanOrEqual)]
            public partial Task<IReadOnlyList<Item>> RangeAsync(int minimum, int maximum, CancellationToken cancellationToken = default);
            """));

        var declaredText = GeneratedStoreText(declared, "ItemStore.InquiryStore.g.cs");
        var swappedText = GeneratedStoreText(swapped, "ItemStore.InquiryStore.g.cs");
        var positionalText = PositionalStoreText(positional, "ItemStore.InquiryStore.g.cs");

        const string sql = "WHERE \\\"Score\\\" >= @Score AND \\\"Score\\\" <= @Score2";
        Assert.Contains(sql, declaredText);
        Assert.Contains(sql, swappedText);
        Assert.Contains(sql, positionalText);
        foreach (var text in new[] { declaredText, swappedText, positionalText })
        {
            Assert.Contains("_p0.ParameterName = \"@Score\";", text);
            Assert.Contains("_p0.Value = (object?)minimum ?? global::System.DBNull.Value;", text);
            Assert.Contains("_p1.ParameterName = \"@Score2\";", text);
            Assert.Contains("_p1.Value = (object?)maximum ?? global::System.DBNull.Value;", text);
        }
    }

    [Fact]
    public void NamedBindingCoversBetweenInIsNullSpecificationAndOffsetPaging()
    {
        var result = RunGenerator(CompositionEntity + """
            [InquirySpecification]
            [InquiryWhere("Category", Parameter = "category")]
            [AttributeUsage(AttributeTargets.Method)]
            public sealed class NamedCategoryAttribute : Attribute
            {
            }

            public partial class ItemStore : InquiryStore<Item>
            {
                [InquirySelectAllByPredicate(OrderBy = "Id ASC", Paged = true)]
                [NamedCategory]
                [InquiryWhere("Score", Compare.Between, Parameter = nameof(low), UpperParameter = nameof(high))]
                [InquiryWhere("Id", Compare.In, Parameter = nameof(ids))]
                [InquiryWhere("State", Compare.IsNull)]
                public partial Task<InquiryPagedResult<Item>> PageAsync(
                    IReadOnlyList<long> ids, int high, string category, int low,
                    int offset, int limit, CancellationToken cancellationToken = default);
            }
            """);
        var text = GeneratedStoreText(result, "ItemStore.InquiryStore.g.cs");

        Assert.Contains("WHERE \\\"Category\\\" = @Category AND \\\"Score\\\" BETWEEN @Score_lo AND @Score_hi AND \\\"Id\\\" IN (SELECT value FROM json_each(@Id)) AND \\\"State\\\" IS NULL", text);
        Assert.Contains("_p0.Value = (object?)category ?? global::System.DBNull.Value;", text);
        Assert.Contains("_p1.ParameterName = \"@Score_lo\";", text);
        Assert.Contains("_p1.Value = (object?)low ?? global::System.DBNull.Value;", text);
        Assert.Contains("_p2.ParameterName = \"@Score_hi\";", text);
        Assert.Contains("_p2.Value = (object?)high ?? global::System.DBNull.Value;", text);
        Assert.Contains("InquiryJsonArrayParameter.Bind(_c, \"@Id\", ids);", text);
        Assert.Contains("LIMIT @__limit OFFSET @__offset", text);
    }

    [Fact]
    public void NamedBindingLetsSetBasedUpdateValuesAppearInAnyPosition()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdate]
            [InquiryWhere("Name", Parameter = nameof(name))]
            public partial Task<int> RepriceAsync(string name, decimal price, CancellationToken cancellationToken = default);

            [InquiryUpdate]
            [InquirySet("Price", "{Price} + @delta")]
            [InquiryWhere("Name", Parameter = nameof(name))]
            public partial Task<int> AdjustAsync(string name, decimal delta, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ThingStore.InquiryStore.g.cs");

        Assert.Contains("UPDATE \\\"TThing\\\" SET \\\"Price\\\" = @Price WHERE \\\"Name\\\" = @Name", text);
        Assert.Contains("UPDATE \\\"TThing\\\" SET \\\"Price\\\" = \\\"Price\\\" + @delta WHERE \\\"Name\\\" = @Name", text);
        // Both methods bind the SET value from Arg1 and the criterion from Arg0.
        Assert.Equal(2, CountOccurrences(text, "_p0.Value = (object?)_args.Arg1 ?? global::System.DBNull.Value;"));
        Assert.Equal(2, CountOccurrences(text, "_p1.Value = (object?)_args.Arg0 ?? global::System.DBNull.Value;"));
    }

    [Theory]
    [InlineData(
        """
        [InquiryWhere("Category", Parameter = nameof(category))]
        [InquiryWhere("Score")]
        public partial Task<IReadOnlyList<Item>> FindAsync(string category, int score, CancellationToken cancellationToken = default);
        """,
        "the criterion on 'Score' names no parameter")]
    [InlineData(
        """
        [InquiryWhere("Category", Parameter = "categry")]
        public partial Task<IReadOnlyList<Item>> FindAsync(string category, CancellationToken cancellationToken = default);
        """,
        "names parameter 'categry', which the method does not declare")]
    [InlineData(
        """
        [InquiryWhere("Category", Parameter = nameof(category))]
        public partial Task<IReadOnlyList<Item>> FindAsync(string category, int score, CancellationToken cancellationToken = default);
        """,
        "parameter 'score' is not bound by any criterion")]
    [InlineData(
        """
        [InquiryWhere("Category", Parameter = nameof(category))]
        [InquiryWhere("Name", Parameter = nameof(category))]
        public partial Task<IReadOnlyList<Item>> FindAsync(string category, CancellationToken cancellationToken = default);
        """,
        "parameter 'category' is bound by more than one criterion")]
    [InlineData(
        """
        [InquiryWhere("Category", Parameter = nameof(category))]
        [InquiryWhere("State", Compare.IsNull, Parameter = nameof(category))]
        public partial Task<IReadOnlyList<Item>> FindAsync(string category, CancellationToken cancellationToken = default);
        """,
        "uses IsNull, which takes no parameter")]
    [InlineData(
        """
        [InquiryWhere("Category", Parameter = nameof(category))]
        [InquiryWhere("State", Compare.IsNotNull, Parameter = nameof(category))]
        public partial Task<IReadOnlyList<Item>> FindAsync(string category, CancellationToken cancellationToken = default);
        """,
        "uses IsNotNull, which takes no parameter")]
    public void InvalidNamedBindingReportsINQ109(string method, string reason)
    {
        var result = RunGenerator(CompositionStore("[InquirySelectAllByPredicate]\n" + method));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static d => d.Id == "INQ109");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(reason, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownNamedParameterOnSetBasedUpdateReportsINQ109BeforeUpdateValueChecks()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdate]
            [InquiryWhere("Id", Parameter = "idd")]
            public partial Task<int> RepriceAsync(long id, decimal price, CancellationToken cancellationToken = default);
            """));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("INQ109", diagnostic.Id);
        Assert.Contains("names parameter 'idd', which the method does not declare",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        """
        [InquiryWhere("Category", Parameter = nameof(category))]
        public partial Task<IReadOnlyList<Item>> FindAsync(int category, CancellationToken cancellationToken = default);
        """,
        "INQ019")]
    [InlineData(
        """
        [InquiryWhere("Score", Optional = true, Parameter = nameof(score))]
        public partial Task<IReadOnlyList<Item>> FindAsync(int score, CancellationToken cancellationToken = default);
        """,
        "INQ098")]
    public void NamedBindingKeepsThePositionalTypeDiagnostics(string method, string id)
    {
        var result = RunGenerator(CompositionStore("[InquirySelectAllByPredicate]\n" + method));

        Assert.Equal(id, Assert.Single(result.GeneratorDiagnostics).Id);
    }

    [Fact]
    public void PositionalCriteriaWithSameTypeParametersReportINQ112()
    {
        var result = RunGenerator(CompositionStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Category")]
            [InquiryWhere("Score", Compare.GreaterThan)]
            [InquiryWhere("Name")]
            public partial Task<IReadOnlyList<Item>> SameTypeAsync(string category, int score, string name, CancellationToken cancellationToken = default);

            [InquirySelectAllByPredicate]
            [InquiryWhere("Category")]
            [InquiryWhere("Score", Compare.Between)]
            public partial Task<IReadOnlyList<Item>> DistinctTypesAsync(string category, int low, int high, CancellationToken cancellationToken = default);
            """));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("INQ112", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'SameTypeAsync' binds criteria on 'Category' and 'Name' by position to parameters of type 'string'",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns a generated store whose positional criteria share a parameter type. Such a method
    /// reports only INQ112, and the generated code must still compile.
    /// </summary>
    private static string PositionalStoreText(GeneratorTestResult result, string fileName)
    {
        Assert.All(result.GeneratorDiagnostics, static d => Assert.Equal("INQ112", d.Id));
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        var tree = Assert.Single(result.RunResult.GeneratedTrees, t => t.FilePath.EndsWith(fileName, StringComparison.Ordinal));
        return tree.GetText().ToString();
    }
}
