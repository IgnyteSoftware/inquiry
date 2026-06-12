using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Set-based predicate mutation emission: <c>[InquiryUpdateWhere]</c> emits an
/// <c>UPDATE … SET … WHERE &lt;predicates&gt;</c> const (SET parameters first, then the predicate
/// bindings) and <c>[InquiryDeleteWhere]</c> emits the matching <c>DELETE … WHERE</c> const — or the
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
            [InquiryDeleteWhere]
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
            [InquiryUpdateWhere("Price")]
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
        Assert.Contains("_p0.Value = (object?)price ?? global::System.DBNull.Value;", text);
        Assert.Contains("_p1.Value = (object?)name ?? global::System.DBNull.Value;", text);
        Assert.Contains("return Inquiry.ExecuteAsync(_cmd,", text);
    }

    [Fact]
    public void UpdateWhereWithInPredicateUsesSentinelAndExpansion()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdateWhere("Price")]
            [InquiryWhere("Id", Compare.In)]
            public partial Task<int> RepriceManyAsync(decimal price, IEnumerable<long> ids, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ThingStore.InquiryStore.g.cs");

        Assert.Contains("private const string _sqlUpdateWhere_RepriceManyAsync = \"UPDATE \\\"TThing\\\" SET \\\"Price\\\" = @Price WHERE \\\"Id\\\" IN (@Id)\";", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryInExpansion.Expand(_c, \"@Id\", ids, Inquiry.MaxParametersPerCommand);", text);
    }

    [Fact]
    public void DeleteWhereWithInPredicateUsesSentinelAndExpansion()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryDeleteWhere]
            [InquiryWhere("Name", Compare.In)]
            public partial Task<int> DeleteNamedAsync(IEnumerable<string> names, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ThingStore.InquiryStore.g.cs");

        Assert.Contains("private const string _sqlDeleteWhere_DeleteNamedAsync = \"DELETE FROM \\\"TThing\\\" WHERE \\\"Name\\\" IN (@Name)\";", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryInExpansion.Expand(_c, \"@Name\", names, Inquiry.MaxParametersPerCommand);", text);
    }

    [Fact]
    public void DeleteWhereOnSoftDeleteEntityEmitsSoftUpdateWithActiveFilter()
    {
        var result = RunGenerator(SoftDocStore("""
            [InquiryDeleteWhere]
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
            [InquiryDeleteWhere(HardDelete = true)]
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
            [InquiryUpdateWhere("Title")]
            [InquiryWhere("Id")]
            public partial Task<int> RetitleAsync(string title, long id, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "DocStore.InquiryStore.g.cs");

        Assert.Contains("private const string _sqlUpdateWhere_RetitleAsync = \"UPDATE \\\"TDoc\\\" SET \\\"Title\\\" = @Title WHERE \\\"Id\\\" = @Id AND \\\"IsDeleted\\\" = 0\";", text);
    }

    [Fact]
    public void UpdateWhereSetAndFilterOnSameColumnDoNotCollide()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdateWhere("Name")]
            [InquiryWhere("Name")]
            public partial Task<int> RenameAsync(string newName, string oldName, CancellationToken cancellationToken = default);
            """));
        var text = GeneratedStoreText(result, "ThingStore.InquiryStore.g.cs");

        // The SET claims "@Name"; the predicate on the same column is uniquified to "@Name2".
        Assert.Contains("private const string _sqlUpdateWhere_RenameAsync = \"UPDATE \\\"TThing\\\" SET \\\"Name\\\" = @Name WHERE \\\"Name\\\" = @Name2\";", text);
        Assert.Contains("_p0.ParameterName = \"@Name\";", text);
        Assert.Contains("_p0.Value = (object?)newName ?? global::System.DBNull.Value;", text);
        Assert.Contains("_p1.ParameterName = \"@Name2\";", text);
        Assert.Contains("_p1.Value = (object?)oldName ?? global::System.DBNull.Value;", text);
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
                [InquiryDeleteWhere]
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
            [InquiryUpdateWhere("Nope")]
            [InquiryWhere("Name")]
            public partial Task<int> RepriceAsync(decimal price, string name, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ007");
    }

    [Fact]
    public void UpdateWhereSetFieldOnKeyColumnIsRejected()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdateWhere("Id")]
            [InquiryWhere("Name")]
            public partial Task<int> RekeyAsync(long id, string name, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ044");
    }

    [Fact]
    public void UpdateWhereSetFieldOnSoftDeleteIndicatorIsRejected()
    {
        var result = RunGenerator(SoftDocStore("""
            [InquiryUpdateWhere("IsDeleted")]
            [InquiryWhere("Id")]
            public partial Task<int> MarkAsync(bool isDeleted, long id, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ044");
    }

    [Fact]
    public void DeleteWhereWithoutWhereCriteriaIsRejected()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryDeleteWhere]
            public partial Task<int> DeleteEverythingAsync(CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ023");
    }

    [Fact]
    public void UpdateWhereWithoutWhereCriteriaIsRejected()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryUpdateWhere("Price")]
            public partial Task<int> RepriceAllAsync(decimal price, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ023");
    }

    [Fact]
    public void PredicateMutationWithWrongReturnTypeIsRejected()
    {
        var result = RunGenerator(ThingStore("""
            [InquiryDeleteWhere]
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
            [InquiryUpdateWhere("Price")]
            [InquiryWhere("Name")]
            public partial Task<int> RepriceAsync(decimal price, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ019");
    }
}
