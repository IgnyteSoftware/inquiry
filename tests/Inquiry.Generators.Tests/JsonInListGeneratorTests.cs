namespace Inquiry.Generators.Tests;

/// <summary>
/// SQL Server JSON IN lists (#617): an opted-in <c>Compare.In</c> renders <c>OPENJSON ... WITH</c> typed
/// as the column, binds one JSON parameter and emits no table-type artifact.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void SqlServerJsonInCriterionRendersTypedOpenJsonAndEmitsNoArtifact()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In, InListBinding = InListBinding.Json)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);

            [InquirySelectAllByPredicate]
                [InquiryWhere("ProductName", Compare.In, InListBinding = InListBinding.Json)]
                public partial Task<IReadOnlyList<Product>> InNamesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GeneratedProductStoreText(result);

        Assert.Contains("[CategoryId] IN (SELECT [value] FROM OPENJSON(@CategoryId) WITH ([value] INT '$'))", text);
        Assert.Contains("global::Inquiry.SqlServer.Parameters.InquiryJsonInParameter.Bind(_c, \"@CategoryId\", categoryIds, \"int\", 7);", text);
        // Short integer lists keep rc.1's scalar binding; JSON replaces only the long-list transport.
        Assert.Contains("\"@CategoryId_in0\"", text);
        Assert.Contains("[ProductName] IN (SELECT [value] FROM OPENJSON(@ProductName) WITH ([value] NVARCHAR(MAX) '$'))", text);
        Assert.Contains("global::Inquiry.SqlServer.Parameters.InquiryJsonInParameter.Bind(_c, \"@ProductName\", names, \"nvarchar\", 7);", text);
        Assert.DoesNotContain("InquiryTvpParameter", text);
        Assert.DoesNotContain("_inquiryTvpDescriptor_", text);

        var schema = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.DoesNotContain("ProviderArtifactsDdl", schema);
        Assert.DoesNotContain("ProviderArtifactsValidationSql", schema);
    }

    [Fact]
    public void SqlServerAssemblyJsonDefaultAppliesAndCriterionCanKeepTableType()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);

            [InquirySelectAllByPredicate]
                [InquiryWhere("ProductName", Compare.In, InListBinding = InListBinding.TableValuedParameter)]
                public partial Task<IReadOnlyList<Product>> InNamesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default);
            """) + "\n[assembly: global::Inquiry.SqlServer.SqlServerInListBinding(global::Inquiry.Stores.InListBinding.Json)]\n";

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GeneratedProductStoreText(result);

        Assert.Contains("[CategoryId] IN (SELECT [value] FROM OPENJSON(@CategoryId) WITH ([value] INT '$'))", text);
        Assert.Contains("InquiryJsonInParameter.Bind(_c, \"@CategoryId\", categoryIds, \"int\", 7);", text);
        Assert.Contains("[ProductName] IN (SELECT [Value] FROM @ProductName)", text);
        Assert.Contains("InquiryTvpParameter.Bind(_c, \"@ProductName\", names, \"[dbo].[Inquiry_Tvp_", text);

        // Only the criterion that kept the table type contributes an artifact.
        var schema = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("N'nvarchar(max)|nullable=0' AS [ExpectedElementSignature]", schema);
        Assert.DoesNotContain("N'int|nullable=", schema);
    }

    [Fact]
    public void JsonInListBindingDoesNotChangeOtherProvidersOrNotIn()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In, InListBinding = InListBinding.Json)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);

            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.NotIn, InListBinding = InListBinding.Json)]
                public partial Task<IReadOnlyList<Product>> NotInCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var sqlServer = GeneratedProductStoreText(RunGenerator(source, dialect: "SqlServer"));
        Assert.Contains("InquiryInExpansion.ExpandNotIn(_c, \"@CategoryId\"", sqlServer);
        Assert.DoesNotContain("NOT IN (SELECT [value] FROM OPENJSON", sqlServer);

        var postgres = GeneratedProductStoreText(RunGenerator(source, dialect: "PostgreSql"));
        Assert.DoesNotContain("OPENJSON", postgres);
        Assert.DoesNotContain("InquiryJsonInParameter", postgres);
    }
}
