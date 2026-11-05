using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// INQ115 (#537): an operation that does not apply [InquiryWhere] criteria rejects them, directly declared
/// or supplied by an [InquirySpecification] attribute. Before the fix these declarations compiled and
/// ran their unfiltered SQL.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string UnsupportedCriteriaEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Paging;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TGadget")]
        public sealed class Gadget
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryColumn("Name")] public string Name { get; set; } = string.Empty;
            [InquiryColumn("Category")] public string? Category { get; set; }
            [InquiryColumn("IsDeleted"), InquirySoftDelete] public bool IsDeleted { get; set; }
        }

        [InquirySpecification]
        [InquiryWhere("Category", Compare.IsNull)]
        [AttributeUsage(AttributeTargets.Method)]
        public sealed class UncategorizedAttribute : Attribute
        {
        }

        [InquirySpecification]
        [AttributeUsage(AttributeTargets.Method)]
        public sealed class EmptySpecificationAttribute : Attribute
        {
        }

        """;

    private static string UnsupportedCriteriaStore(string methods) =>
        UnsupportedCriteriaEntity + "public partial class GadgetStore : InquiryStore<Gadget>\n{\n" + methods + "\n}\n";

    private static string? UnsupportedCriteriaStoreText(GeneratorTestResult result)
        => result.RunResult.GeneratedTrees
            .SingleOrDefault(static t => t.FilePath.EndsWith("GadgetStore.InquiryStore.g.cs", StringComparison.Ordinal))
            ?.GetText().ToString();

    public static TheoryData<string, string, string> UnsupportedCriteriaOperations => new()
    {
        { "InquirySelectAll", "[InquirySelectAll]", "public partial Task<IReadOnlyList<Gadget>> TargetAsync(CancellationToken cancellationToken = default);" },
        { "InquirySelectAll", "[InquirySelectAll(OrderBy = \"Id\", Paged = true)]", "public partial Task<InquiryPagedResult<Gadget>> TargetAsync(int offset, int limit, CancellationToken cancellationToken = default);" },
        { "InquirySelectAllByField", "[InquirySelectAllByField(\"Name\")]", "public partial Task<IReadOnlyList<Gadget>> TargetAsync(string name, CancellationToken cancellationToken = default);" },
        { "InquirySelectOneByKey", "[InquirySelectOneByKey]", "public partial Task<Gadget?> TargetAsync(long id, CancellationToken cancellationToken = default);" },
        { "InquirySelectTopByOrder", "[InquirySelectTopByOrder(\"Id\")]", "public partial Task<Gadget?> TargetAsync(CancellationToken cancellationToken = default);" },
        { "InquiryGroupCount", "[InquiryGroupCount(\"Name\")]", "public partial Task<IReadOnlyList<GroupCount<string>>> TargetAsync(CancellationToken cancellationToken = default);" },
        { "InquiryKeysetPage", "[InquiryKeysetPage(\"Id\")]", "public partial Task<InquiryPage<Gadget, long>> TargetAsync(long? after, int pageSize, CancellationToken cancellationToken = default);" },
        { "InquiryFullTextSearch", "[InquiryFullTextSearch(\"Name\")]", "public partial Task<IReadOnlyList<Gadget>> TargetAsync(string term, CancellationToken cancellationToken = default);" },
        { "InquirySelectAllEager", "[InquirySelectAllEager]", "public partial Task<IReadOnlyList<Gadget>> TargetAsync(CancellationToken cancellationToken = default);" },
        { "InquirySelectOneByKeyEager", "[InquirySelectOneByKeyEager]", "public partial Task<Gadget?> TargetAsync(long id, CancellationToken cancellationToken = default);" },
        { "InquiryRestoreOneByKey", "[InquiryRestoreOneByKey]", "public partial Task<bool> TargetAsync(long id, CancellationToken cancellationToken = default);" },
        { "InquiryInsert", "[InquiryInsert]", "public partial Task<int> TargetAsync(Gadget gadget, CancellationToken cancellationToken = default);" },
        { "InquiryInsert", "[InquiryInsert]", "public partial Task<int> TargetAsync(IEnumerable<Gadget> gadgets, CancellationToken cancellationToken = default);" },
        { "InquiryUpsert", "[InquiryUpsert]", "public partial Task<int> TargetAsync(Gadget gadget, CancellationToken cancellationToken = default);" },
        { "InquiryBulkInsert", "[InquiryBulkInsert]", "public partial Task<long> TargetAsync(IEnumerable<Gadget> gadgets, CancellationToken cancellationToken = default);" },
        { "InquiryStoredProcedure", "[InquiryStoredProcedure(\"usp_Target\")]", "public partial Task<int> TargetAsync(CancellationToken cancellationToken = default);" },
    };

    [Theory]
    [MemberData(nameof(UnsupportedCriteriaOperations))]
    public void OperationWithoutCriteriaSupportRejectsParameterlessCriterion(string attributeName, string operation, string declaration)
    {
        // SQL Server supports every operation in the list, so INQ115 is the only reason to fail.
        var result = RunGenerator(UnsupportedCriteriaStore(
            operation + "\n[InquiryWhere(\"Category\", Compare.IsNull)]\n" + declaration), dialect: "SqlServer");

        AssertRejectedCriteria(result, attributeName);
    }

    [Theory]
    [MemberData(nameof(UnsupportedCriteriaOperations))]
    public void OperationWithoutCriteriaSupportRejectsSpecificationCriterion(string attributeName, string operation, string declaration)
    {
        var result = RunGenerator(UnsupportedCriteriaStore(
            operation + "\n[Uncategorized]\n" + declaration), dialect: "SqlServer");

        AssertRejectedCriteria(result, attributeName);
    }

    [Theory]
    [MemberData(nameof(UnsupportedCriteriaOperations))]
    public void OperationWithoutCriteriaSupportCompilesWithoutCriteria(string attributeName, string operation, string declaration)
    {
        _ = attributeName;
        // The fixtures are valid declarations once the criterion goes, so INQ115 above is not masking
        // another error. An empty specification adds no criteria and stays accepted.
        var result = RunGenerator(UnsupportedCriteriaStore(
            operation + "\n[EmptySpecification]\n" + declaration), dialect: "SqlServer");

        AssertNoErrors(result);
    }

    [Fact]
    public void SelectOneByKeyRejectsValueCriterionThatFitsTheKeyParameter()
    {
        // The key parameter satisfies the operation's shape, so before the fix the criterion was
        // ignored with no diagnostic at all.
        var result = RunGenerator(UnsupportedCriteriaStore("""
            [InquirySelectOneByKey]
            [InquiryWhere("Id")]
            public partial Task<Gadget?> TargetAsync(long id, CancellationToken cancellationToken = default);
            """));

        AssertRejectedCriteria(result, "InquirySelectOneByKey");
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void SelectAllRejectsParameterlessCriterionOnEveryDialect(string dialect)
    {
        var result = RunGenerator(UnsupportedCriteriaStore("""
            [InquirySelectAll]
            [InquiryWhere("Category", Compare.IsNull)]
            public partial Task<IReadOnlyList<Gadget>> TargetAsync(CancellationToken cancellationToken = default);
            """), dialect: dialect);

        AssertRejectedCriteria(result, "InquirySelectAll");
    }

    [Fact]
    public void RejectedCriteriaKeepTheValidSiblingMethod()
    {
        var result = RunGenerator(UnsupportedCriteriaStore("""
            [InquirySelectAll]
            [InquiryWhere("Category", Compare.IsNull)]
            public partial Task<IReadOnlyList<Gadget>> TargetAsync(CancellationToken cancellationToken = default);

            [InquiryCount]
            public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
            """));

        AssertRejectedCriteria(result, "InquirySelectAll");
        var text = UnsupportedCriteriaStoreText(result);
        Assert.NotNull(text);
        Assert.Contains("Task<long> CountAsync(", text);
    }

    [Fact]
    public void DeleteAllWithCriterionStillReportsMutationTargetInvalid()
    {
        var result = RunGenerator(UnsupportedCriteriaStore("""
            [InquiryDeleteAll]
            [InquiryWhere("Category", Compare.IsNull)]
            public partial Task<int> TargetAsync(CancellationToken cancellationToken = default);
            """));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("INQ096", diagnostic.Id);
    }

    [Theory]
    [InlineData(ReportDiagnostic.Suppress, null)]
    [InlineData(ReportDiagnostic.Warn, null)]
    [InlineData(ReportDiagnostic.Suppress, ReportDiagnostic.Warn)]
    public void LoweredRejectionStillEmitsNoBody(ReportDiagnostic inq115, ReportDiagnostic? inq039)
    {
        // Lowering INQ115, even with the INQ039 stub opt-in, must not bring back a body that runs
        // the unfiltered SQL. The partial method stays unimplemented, so the build still fails.
        var result = RunGenerator(UnsupportedCriteriaStore("""
            [InquiryCount]
            [InquiryWhere("Name")]
            public partial Task<long> ValidAsync(string name, CancellationToken cancellationToken = default);

            [InquirySelectAll]
            [InquiryWhere("Category", Compare.IsNull)]
            public partial Task<IReadOnlyList<Gadget>> TargetAsync(CancellationToken cancellationToken = default);
            """),
            unsupportedOperationSeverity: inq039,
            additionalDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["INQ115"] = inq115 });

        var text = UnsupportedCriteriaStoreText(result);
        Assert.NotNull(text);
        Assert.Contains("Task<long> ValidAsync(", text);
        Assert.DoesNotContain("TargetAsync", text);
        Assert.Contains(result.Compilation.GetDiagnostics(), static d => d.Id == "CS8795" && d.Severity == DiagnosticSeverity.Error);
    }

    private static void AssertRejectedCriteria(GeneratorTestResult result, string attributeName)
    {
        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("INQ115", diagnostic.Id);
        Assert.Equal(
            $"Store method 'TargetAsync' uses [{attributeName}], which does not support [InquiryWhere] criteria, including criteria supplied by an [InquirySpecification] attribute. Remove the criteria, or move them to an operation that applies them.",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));

        // No generated body: the store may still emit (constructor, siblings), but never this method.
        var text = UnsupportedCriteriaStoreText(result);
        if (text is not null)
        {
            Assert.DoesNotContain("TargetAsync", text);
        }
    }
}
