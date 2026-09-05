using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Set-based predicate mutation emission: <c>[InquiryUpdate]</c> emits an
/// <c>UPDATE … SET … WHERE &lt;predicates&gt;</c> const (SET parameters first, then the predicate
/// bindings) and <c>[InquiryDelete]</c> emits the matching <c>DELETE … WHERE</c> const — or the
/// soft-delete UPDATE form on a soft-delete entity, with the active-row filter AND-composed exactly
/// like predicate selects. Both return rows affected via <c>ExecuteAsync</c>.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ThingEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TThing")]
        public sealed class Thing
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn("Price")]
            public decimal Price { get; set; }
        }

        """;

    private static string ThingStore(string methods) => ThingEntity + """
        public partial class ThingStore : Inquiry.Stores.InquiryStore<Demo.Thing>
        {
        """ + "\n" + methods + "\n}\n";

    private const string SoftDocEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TDoc")]
        public sealed class Doc
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Title")]
            public string Title { get; set; } = string.Empty;

            [InquiryColumn("IsDeleted"), InquirySoftDelete]
            public bool IsDeleted { get; set; }
        }

        """;

    private static string SoftDocStore(string methods) => SoftDocEntity + """
        public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
        {
        """ + "\n" + methods + "\n}\n";

    private static string GeneratedStoreText(GeneratorTestResult result, string fileName)
    {
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, t => t.FilePath.EndsWith(fileName, StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void DeleteWhereEmitsDeleteSqlAndBinder()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryDelete]
            [InquiryWhere("Name")]
            public partial Task<int> DeleteByNameAsync(string name, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ThingStore.InquiryStore.g.cs");

        Assert.Contains("private const string _sqlDeleteWhere_DeleteByNameAsync = \"DELETE FROM \\\"TThing\\\" WHERE \\\"Name\\\" = @Name\";", text);
        Assert.Contains("_p0.ParameterName = \"@Name\";", text);
        Assert.Contains("return Inquiry.ExecuteAsync(_cmd,", text);
    }

    [Fact]
    public void UpdateWhereEmitsSetAndWhereWithPositionalBinding()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdate]
            [InquiryWhere("Name")]
            public partial Task<int> RepriceAsync(decimal price, string name, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ThingStore.InquiryStore.g.cs");

        Assert.Contains("private const string _sqlUpdateWhere_RepriceAsync = \"UPDATE \\\"TThing\\\" SET \\\"Price\\\" = @Price WHERE \\\"Name\\\" = @Name\";", text);

        // SET parameter bound first (from the first method parameter), predicate parameter second.
        Assert.Contains("_p0.ParameterName = \"@Price\";", text);
        Assert.Contains("_p1.ParameterName = \"@Name\";", text);
        var setIndex = text.IndexOf("_p0.ParameterName = \"@Price\";", StringComparison.Ordinal);
        var whereIndex = text.IndexOf("_p1.ParameterName = \"@Name\";", StringComparison.Ordinal);
        Assert.True(setIndex >= 0 && whereIndex > setIndex);
        Assert.Contains("_p0.Value = (object?)_args.Arg0 ?? global::System.DBNull.Value;", text);
        Assert.Contains("_p1.Value = (object?)_args.Arg1 ?? global::System.DBNull.Value;", text);
        Assert.Contains("return Inquiry.ExecuteAsync(_cmd,", text);
    }

    [Fact]
    public void UpdateWhereWithInPredicateUsesJsonEachAndJsonArrayBinding()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdate]
            [InquiryWhere("Id", Compare.In)]
            public partial Task<int> RepriceManyAsync(decimal price, IEnumerable<long> ids, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ThingStore.InquiryStore.g.cs");

        Assert.Contains("private const string _sqlUpdateWhere_RepriceManyAsync = \"UPDATE \\\"TThing\\\" SET \\\"Price\\\" = @Price WHERE \\\"Id\\\" IN (SELECT value FROM json_each(@Id))\";", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryJsonArrayParameter.Bind(_c, \"@Id\", _args.Arg1);", text);
    }

    [Fact]
    public void DeleteWhereWithInPredicateUsesJsonEachAndJsonArrayBinding()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryDelete]
            [InquiryWhere("Name", Compare.In)]
            public partial Task<int> DeleteNamedAsync(IEnumerable<string> names, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ThingStore.InquiryStore.g.cs");

        Assert.Contains("private const string _sqlDeleteWhere_DeleteNamedAsync = \"DELETE FROM \\\"TThing\\\" WHERE \\\"Name\\\" IN (SELECT value FROM json_each(@Name))\";", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryJsonArrayParameter.Bind(_c, \"@Name\", _args.Arg0);", text);
    }

    [Fact]
    public void DeleteWhereOnSoftDeleteEntityEmitsSoftUpdateWithActiveFilter()
    {
        var result = RunGenerator(SoftDocStore("""
            [InquiryDelete]
            [InquiryWhere("Title")]
            public partial Task<int> DeleteByTitleAsync(string title, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "DocStore.InquiryStore.g.cs");

        // Soft form: UPDATE the indicator, with the active-row filter AND-composed (already-deleted
        // rows are a no-op and excluded from the rows-affected count).
        Assert.Contains("private const string _sqlDeleteWhere_DeleteByTitleAsync = \"UPDATE \\\"TDoc\\\" SET \\\"IsDeleted\\\" = 1 WHERE \\\"Title\\\" = @Title AND \\\"IsDeleted\\\" = 0\";", text);
    }

    [Fact]
    public void HardDeleteWhereOnSoftDeleteEntityEmitsLiteralDelete()
    {
        var result = RunGenerator(SoftDocStore("""
            [InquiryDelete(HardDelete = true)]
            [InquiryWhere("Title")]
            public partial Task<int> PurgeByTitleAsync(string title, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "DocStore.InquiryStore.g.cs");

        // Hard form: literal DELETE, no active-row filter (it may remove soft-deleted rows too).
        Assert.Contains("private const string _sqlDeleteWhere_PurgeByTitleAsync = \"DELETE FROM \\\"TDoc\\\" WHERE \\\"Title\\\" = @Title\";", text);
    }

    [Fact]
    public void UpdateWhereOnSoftDeleteEntityComposesActiveFilter()
    {
        var result = RunGenerator(SoftDocStore("""
            [InquiryUpdate]
            [InquiryWhere("Id")]
            public partial Task<int> RetitleAsync(string title, long id, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "DocStore.InquiryStore.g.cs");

        Assert.Contains("private const string _sqlUpdateWhere_RetitleAsync = \"UPDATE \\\"TDoc\\\" SET \\\"Title\\\" = @Title WHERE \\\"Id\\\" = @Id AND \\\"IsDeleted\\\" = 0\";", text);
    }

    [Fact]
    public void UpdateWhereInfersMultipleSetColumnsFromLeadingParameters()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdate]
            [InquiryWhere("Id")]
            public partial Task<int> RenameAsync(string name, decimal price, long id, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ThingStore.InquiryStore.g.cs");

        Assert.Contains("private const string _sqlUpdateWhere_RenameAsync = \"UPDATE \\\"TThing\\\" SET \\\"Name\\\" = @Name, \\\"Price\\\" = @Price WHERE \\\"Id\\\" = @Id\";", text);
        Assert.Contains("_p0.ParameterName = \"@Name\";", text);
        Assert.Contains("_p1.ParameterName = \"@Price\";", text);
        Assert.Contains("_p2.ParameterName = \"@Id\";", text);
    }

    [Fact]
    public void PredicateMutationOnConcurrencyTokenEntityIsRejected()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TWidget")]
            public sealed class Widget
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn("Version"), InquiryConcurrencyToken]
                public int Version { get; set; }
            }

            public partial class WidgetStore : Inquiry.Stores.InquiryStore<Demo.Widget>
            {
                [InquiryDelete]
                [InquiryWhere("Name")]
                public partial Task<int> DeleteByNameAsync(string name, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ022");
    }

    [Fact]
    public void UpdateWhereUnknownSetFieldIsRejected()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdate]
            [InquiryWhere("Name")]
            public partial Task<int> RepriceAsync(decimal cost, string name, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ007");
    }

    [Fact]
    public void UpdateWhereSetFieldOnKeyColumnIsRejected()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdate]
            [InquiryWhere("Name")]
            public partial Task<int> RekeyAsync(long id, string name, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ044");
    }

    [Fact]
    public void UpdateWhereSetFieldOnSoftDeleteIndicatorIsRejected()
    {
        var result = RunGenerator(SoftDocStore("""
            [InquiryUpdate]
            [InquiryWhere("Id")]
            public partial Task<int> MarkAsync(bool isDeleted, long id, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ044");
    }

    [Fact]
    public void DeleteWhereWithoutWhereCriteriaIsRejected()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryDelete]
            public partial Task<int> DeleteEverythingAsync(CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ096");

        var resultWithoutToken = RunGenerator(ThingStore("""
            [InquiryDelete]
            public partial Task<int> DeleteEverythingAsync();
            """));
        Assert.Contains(resultWithoutToken.RunResult.Diagnostics, static d => d.Id == "INQ096");
    }

    [Fact]
    public void EntityUpdateWithoutEntityParameterIsRejected()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdate]
            public partial Task<bool> RepriceAllAsync(decimal price, CancellationToken cancellationToken = default);
            """));
        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ096");
        Assert.Contains("reads as an update by entity key", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Add [InquiryWhere] to select predicate-update mode", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void PredicateMutationWithWrongReturnTypeIsRejected()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryDelete]
            [InquiryWhere("Name")]
            public partial Task<bool> DeleteByNameAsync(string name, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ005");
    }

    [Fact]
    public void UpdateWhereParameterCountMismatchIsRejected()
    {
        // One SET field + one scalar criterion needs two non-token parameters; only one is supplied.
        var result = RunGenerator(ThingStore("""
            [InquiryUpdate]
            [InquiryWhere("Name")]
            public partial Task<int> RepriceAsync(decimal price, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ096");
    }
}
