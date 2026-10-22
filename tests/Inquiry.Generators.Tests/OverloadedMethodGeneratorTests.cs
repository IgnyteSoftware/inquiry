namespace Inquiry.Generators.Tests;

/// <summary>
/// Overloaded store methods (#287): generated SQL consts and the per-method lookup maps are keyed by
/// full method identity, not by name alone. A plain key lookup must never inherit a sibling
/// overload's per-method SELECT-all const, in either declaration order and with a named-filter bypass.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string OverloadedKeyLookup = """
        [InquirySelectOneByKey]
        public partial Task<Widget?> ReadAsync(long id, CancellationToken cancellationToken = default);
        """;

    private const string OverloadedUnfilteredSelectAll = """
        [InquirySelectAll(IncludeDeleted = true)]
        public partial Task<IReadOnlyList<Widget>> ReadAsync(CancellationToken cancellationToken = default);
        """;

    /// <summary>The generated body of the first method whose header contains <paramref name="headerFragment"/>.</summary>
    private static string GeneratedMethodBody(string text, string headerFragment)
    {
        var start = text.IndexOf(headerFragment, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Generated store does not declare '{headerFragment}'.");
        var end = text.IndexOf("\n    public ", start + headerFragment.Length, StringComparison.Ordinal);
        return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverloadedKeyLookupKeepsItsOwnSql_Sqlite(bool keyLookupFirst)
    {
        var methods = keyLookupFirst
            ? OverloadedKeyLookup + "\n\n" + OverloadedUnfilteredSelectAll
            : OverloadedUnfilteredSelectAll + "\n\n" + OverloadedKeyLookup;
        var result = RunGenerator(WidgetStore(methods));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        var byKey = GeneratedMethodBody(text, "ReadAsync(long id");
        Assert.Contains("_sqlSelectByKey", byKey);
        Assert.DoesNotContain("_sqlSelectAll_", byKey);

        var unfiltered = GeneratedMethodBody(text, "ReadAsync(global::System.Threading.CancellationToken");
        Assert.Contains("_sqlSelectAll_ReadAsync", unfiltered);
        Assert.DoesNotContain("_sqlSelectByKey", unfiltered);
    }

    [Fact]
    public void OverloadedKeyLookupKeepsGlobalFilterBesideBypassOverload_Sqlite()
    {
        var source = DocStoreSource("""
            [InquirySelectAll]
            [InquiryIgnoreFilter("Tenant")]
            public partial Task<IReadOnlyList<Doc>> FindAsync(CancellationToken cancellationToken = default);

            [InquirySelectOneByKey]
            public partial Task<Doc?> FindAsync(long id, CancellationToken cancellationToken = default);
            """)
            .Replace("InquiryGlobalFilter(ContextKey = \"TenantId\")", "InquiryGlobalFilter(ContextKey = \"TenantId\", Name = \"Tenant\")");
        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        var byKey = GeneratedMethodBody(text, "FindAsync(long id");
        Assert.Contains("_sqlSelectByKey", byKey);
        Assert.DoesNotContain("_sqlSelectAll_", byKey);

        var bypass = GeneratedMethodBody(text, "FindAsync(global::System.Threading.CancellationToken");
        Assert.Contains("_sqlSelectAll_FindAsync", bypass);
        Assert.DoesNotContain("_sqlSelectByKey", bypass);
    }
}
