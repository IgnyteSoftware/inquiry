namespace Inquiry.Generators.Tests;

/// <summary>
/// Ownership of the runtime-parameterized filter bind name (#288). A legal C# name can equal the
/// filter's default <c>__gf_&lt;property&gt;</c> name. Names fixed by the entity come first, the
/// filter name avoids them, and names chosen per method avoid the filter names. Declarations with no
/// such collision keep their SQL; the existing <c>@__gf_TenantId</c> assertions guard that.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ShadowProperty = "    [InquiryColumn(\"Shadow\")]\n    public long __gf_TenantId { get; set; }\n";

    /// <summary>The TenantDoc entity with extra members after TenantId, plus an optional projection.</summary>
    private static string TenantDocWith(string extraMembers, string methods, string extraTypes = "")
    {
        var entity = TenantDocEntityLf.Replace(
            "    public long TenantId { get; set; }\n",
            "    public long TenantId { get; set; }\n\n" + extraMembers,
            StringComparison.Ordinal);
        Assert.NotEqual(TenantDocEntityLf, entity);
        return entity + "\n\n" + extraTypes + "\npublic partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>\n{\n" + methods + "\n}\n";
    }

    private const string ShadowReadAndWriteMethods = """
        [InquirySelectAll]
        public partial Task<IReadOnlyList<Doc>> AllAsync(CancellationToken cancellationToken = default);

        [InquirySelectAllByField("__gf_TenantId")]
        public partial Task<IReadOnlyList<Doc>> ByShadowAsync(long shadow, CancellationToken cancellationToken = default);

        [InquiryUpdate]
        public partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);
        """;

    [Fact]
    public void FilterBindNameAvoidsAMappedPropertyWithTheSameName_Sqlite()
    {
        var result = RunGenerator(TenantDocWith(ShadowProperty, ShadowReadAndWriteMethods));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Equal(
            "SELECT \\\"Id\\\", \\\"Name\\\", \\\"TenantId\\\", \\\"Shadow\\\" FROM \\\"TDoc\\\" WHERE \\\"TenantId\\\" = @___gf_TenantId",
            GeneratedConst(text, "_sqlSelectAll"));
        Assert.Equal(
            "SELECT \\\"Id\\\", \\\"Name\\\", \\\"TenantId\\\", \\\"Shadow\\\" FROM \\\"TDoc\\\" WHERE \\\"Shadow\\\" = @__gf_TenantId AND \\\"TenantId\\\" = @___gf_TenantId",
            GeneratedConst(text, "_sqlSelectBy___gf_TenantId"));
        Assert.Equal(
            "UPDATE \\\"TDoc\\\" SET \\\"Name\\\" = @Name, \\\"TenantId\\\" = @TenantId, \\\"Shadow\\\" = @__gf_TenantId WHERE \\\"Id\\\" = @Id AND \\\"TenantId\\\" = @___gf_TenantId",
            GeneratedConst(text, "_sqlUpdate"));

        // The read helper and both write-helper overloads bind the moved name; none binds the property's.
        Assert.Equal(3, CountOccurrences(text, "_fp0.ParameterName = \"@___gf_TenantId\";"));
        Assert.DoesNotContain("_fp0.ParameterName = \"@__gf_TenantId\";", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterBindNameAvoidsAMappedPropertyThatDiffersOnlyByCase_Sqlite()
    {
        var result = RunGenerator(TenantDocWith(
            "    [InquiryColumn(\"Shadow\")]\n    public long __GF_TenantId { get; set; }\n",
            """
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Doc>> AllAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.EndsWith("WHERE \\\"TenantId\\\" = @___gf_TenantId", GeneratedConst(text, "_sqlSelectAll"), StringComparison.Ordinal);
    }

    [Fact]
    public void FilterBindNameKeepsMovingUntilItIsFree_Sqlite()
    {
        var result = RunGenerator(TenantDocWith(
            ShadowProperty + "\n    [InquiryColumn(\"Shadow2\")]\n    public long ___gf_TenantId { get; set; }\n",
            """
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Doc>> AllAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.EndsWith("WHERE \\\"TenantId\\\" = @____gf_TenantId", GeneratedConst(text, "_sqlSelectAll"), StringComparison.Ordinal);
        Assert.Contains("_fp0.ParameterName = \"@____gf_TenantId\";", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoFiltersWhosePropertiesDifferOnlyByCaseGetDistinctBindNames_Sqlite()
    {
        var result = RunGenerator(TenantDocWith(
            "    [InquiryColumn(\"TenantCode\"), InquiryGlobalFilter(ContextKey = \"TenantCode\")]\n    public long tenantId { get; set; }\n",
            """
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Doc>> AllAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.EndsWith(
            "WHERE \\\"TenantId\\\" = @__gf_TenantId AND \\\"TenantCode\\\" = @___gf_tenantId",
            GeneratedConst(text, "_sqlSelectAll"),
            StringComparison.Ordinal);
        Assert.Contains("_fp0.ParameterName = \"@__gf_TenantId\";", text, StringComparison.Ordinal);
        Assert.Contains("_fp1.ParameterName = \"@___gf_tenantId\";", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionReadUsesTheEntitysFilterBindName_Sqlite()
    {
        // The projection omits Shadow, so only the entity can know the filter name has moved.
        var result = RunGenerator(TenantDocWith(
            ShadowProperty,
            """
            [InquirySelectAll]
            public partial Task<IReadOnlyList<DocName>> NamesAsync(CancellationToken cancellationToken = default);
            """,
            """
            [InquiryProjection(typeof(Doc))]
            public sealed record DocName
            {
                [InquiryColumn("Name")]
                public string Name { get; init; } = string.Empty;
            }
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Contains("SELECT \\\"Name\\\" FROM \\\"TDoc\\\" WHERE \\\"TenantId\\\" = @___gf_TenantId\";", text, StringComparison.Ordinal);
        Assert.Contains("_fp0.ParameterName = \"@___gf_TenantId\";", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BetweenEndpointNamesAvoidAFilterBindName_Sqlite()
    {
        // "__gf" + "_lo" equals the bind name of a filter on a property named "lo".
        var source = TenantDocEntityLf
            .Replace(
                "[InquiryColumn(\"TenantId\"), InquiryGlobalFilter(ContextKey = \"TenantId\", EnforceOnWrites = true)]\n    public long TenantId { get; set; }\n",
                "[InquiryColumn(\"Lo\"), InquiryGlobalFilter(ContextKey = \"Lo\")]\n    public long lo { get; set; }\n\n    [InquiryColumn(\"Range\")]\n    public long __gf { get; set; }\n",
                StringComparison.Ordinal);
        Assert.NotEqual(TenantDocEntityLf, source);
        var result = RunGenerator(source + "\n\npublic partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>\n{\n" + """
            [InquirySelectAllByPredicate]
            [InquiryWhere("__gf", Compare.Between)]
            public partial Task<IReadOnlyList<Doc>> InRangeAsync(long low, long high, CancellationToken cancellationToken = default);
            """ + "\n}\n");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Contains("WHERE \\\"Range\\\" BETWEEN @__gf_lo2 AND @__gf_hi AND \\\"Lo\\\" = @__gf_lo\";", text, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonLeafParameterNamesAvoidAFilterBindNameIgnoringCase_Sqlite()
    {
        var result = RunGenerator(TenantDocWith(
            "    [InquiryColumn(\"Data\")]\n    public string Data { get; set; } = string.Empty;\n",
            """
            [InquirySelectAllByPredicate]
            [InquiryWhere("Data", Compare.Equal, JsonPath = "$.__gf_TenantId")]
            public partial Task<IReadOnlyList<Doc>> ExactAsync(string value, CancellationToken cancellationToken = default);

            [InquirySelectAllByPredicate]
            [InquiryWhere("Data", Compare.Equal, JsonPath = "$.__GF_TenantId")]
            public partial Task<IReadOnlyList<Doc>> CaseOnlyAsync(string value, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Contains("WHERE json_extract(\\\"Data\\\", '$.__gf_TenantId') = @__gf_TenantId2 AND \\\"TenantId\\\" = @__gf_TenantId\";", text, StringComparison.Ordinal);
        Assert.Contains("WHERE json_extract(\\\"Data\\\", '$.__GF_TenantId') = @__GF_TenantId2 AND \\\"TenantId\\\" = @__gf_TenantId\";", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SetExpressionParameterNamedLikeTheFilterBindsUnderAnotherName_Sqlite()
    {
        // The method parameter keeps its C# name; only its SQL bind name moves.
        var result = RunGenerator(TenantDocStore("""
            [InquiryUpdate]
            [InquirySet("Name", "@__gf_TenantId")]
            [InquiryWhere("Id")]
            public partial Task<int> RenameAsync(string __gf_TenantId, long id, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Contains("UPDATE \\\"TDoc\\\" SET \\\"Name\\\" = @__gf_TenantId2 WHERE \\\"Id\\\" = @Id AND \\\"TenantId\\\" = @__gf_TenantId\";", text, StringComparison.Ordinal);
        Assert.Contains("_p0.ParameterName = \"@__gf_TenantId2\";", text, StringComparison.Ordinal);
        Assert.Contains("_fp0.ParameterName = \"@__gf_TenantId\";", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NamedCriterionParameterNamedLikeTheFilterKeepsTheColumnBindName_Sqlite()
    {
        // Named binding selects the argument by name; the SQL name still comes from the column.
        var result = RunGenerator(TenantDocStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Name", Parameter = nameof(__gf_TenantId))]
            public partial Task<IReadOnlyList<Doc>> ByNameAsync(string __gf_TenantId, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Contains("WHERE \\\"Name\\\" = @Name AND \\\"TenantId\\\" = @__gf_TenantId\";", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterBindNameAvoidsAMappedPropertyWithTheSameName_Oracle()
    {
        var result = RunGenerator(TenantDocWith(ShadowProperty, ShadowReadAndWriteMethods), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        // Oracle encodes logical names (OracleBindName); the two names share a stem but not a hash.
        Assert.Equal(
            "SELECT Id, Name, TenantId, Shadow FROM TDoc WHERE Shadow = :iq1$gfTena$5ca5c0b9bc7072 AND TenantId = :iq1$gfTena$fa546044feefd5",
            GeneratedConst(text, "_sqlSelectBy___gf_TenantId"));
        Assert.EndsWith(
            " AND TenantId = :iq1$gfTena$fa546044feefd5",
            GeneratedConst(text, "_sqlUpdate"),
            StringComparison.Ordinal);
        Assert.Contains("_fp0.ParameterName = \"iq1$gfTena$fa546044feefd5\";", text, StringComparison.Ordinal);
    }
}
