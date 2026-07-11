namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void SqlServerTvpArtifactsAreQualifiedDeduplicatedAndDeterministic()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("Items")]
            public sealed class Item
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Length = 64)] public string Code { get; set; } = string.Empty;
            }
            public partial class ItemStore : InquiryStore<Item>
            {
                [InquirySelectAllByPredicate, InquiryWhere("Id", Compare.In)]
                public partial Task<IReadOnlyList<Item>> ByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct = default);
                [InquiryDeleteAll] public partial Task<int> DeleteAllAsync(IReadOnlyList<int> ids, CancellationToken ct = default);
                [InquiryDeleteWhere, InquiryWhere("Code", Compare.In)]
                public partial Task<int> DeleteCodesAsync(IReadOnlyList<string> codes, CancellationToken ct = default);
                [InquirySelectAllByPredicate, InquiryWhere("Id", Compare.NotIn)]
                public partial Task<IReadOnlyList<Item>> NotIdsAsync(IReadOnlyList<int> ids, CancellationToken ct = default);
            }
            [InquiryTable("TenantItems", Schema = "tenant")]
            public sealed class TenantItem { [InquiryKey] public int Id { get; set; } }
            public partial class TenantItemStore : InquiryStore<TenantItem>
            {
                [InquirySelectAllByPredicate, InquiryWhere("Id", Compare.In)]
                public partial Task<IReadOnlyList<TenantItem>> ByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var schema = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        var itemStore = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("\\ItemStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        var tenantStore = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("TenantItemStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains("public const string ProviderArtifactsDdl", schema);
        Assert.Contains("public const string ProviderArtifactsValidationSql", schema);
        Assert.Contains("public const string Ddl = ProviderArtifactsDdl +", schema);
        Assert.Equal(3, global::System.Text.RegularExpressions.Regex.Matches(schema, "CREATE TYPE").Count);
        Assert.Contains("TYPE_ID(N'[dbo].[Inquiry_Tvp_5fcff71acdcd2dc2f2d9b8c73ef6cfb000902eeb236c89d2221808eb2617bbee]')", schema);
        Assert.Contains("AS TABLE ([Value] INT NOT NULL)", schema);
        Assert.Contains("AS TABLE ([Value] NVARCHAR(MAX) NOT NULL)", schema);
        Assert.Contains("SCHEMA_ID(N'tenant')", schema);
        Assert.Contains("CREATE SCHEMA [tenant]", schema);
        Assert.Contains("[tenant].[Inquiry_Tvp_5fcff71acdcd2dc2f2d9b8c73ef6cfb000902eeb236c89d2221808eb2617bbee]", schema);
        Assert.Contains("N'int' AS [ExpectedElementSignature]", schema);
        Assert.Contains("N'nvarchar(max)' AS [ExpectedElementSignature]", schema);

        Assert.Contains("InquiryTvpParameter.Bind(_c, \"@Id\", ids, \"[dbo].[Inquiry_Tvp_5fcff71acdcd2dc2f2d9b8c73ef6cfb000902eeb236c89d2221808eb2617bbee]\")", itemStore);
        Assert.Contains("InquiryTvpParameter.Bind(_c, \"@Code\", codes, \"[dbo].[Inquiry_Tvp_474f2ebbdd781f2c0331853ca09837a0aa4613f2bf445089eafda2b033abe95c]\")", itemStore);
        Assert.Contains("InquiryTvpParameter.Bind(_c, \"@Id\", ids, \"[tenant].[Inquiry_Tvp_5fcff71acdcd2dc2f2d9b8c73ef6cfb000902eeb236c89d2221808eb2617bbee]\")", tenantStore);
        Assert.DoesNotContain("BindUnsupported", itemStore);
        Assert.DoesNotContain("BindUnsupported", tenantStore);
        Assert.Contains("InquiryInExpansion.ExpandNotIn", itemStore);
    }

    [Fact]
    public void SqlServerNotInOnlyEmitsNoProviderArtifactConstants()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("CategoryId", Compare.NotIn)]
            public partial Task<IReadOnlyList<Product>> NotInAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default);
            """);
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var schema = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.DoesNotContain("ProviderArtifactsDdl", schema);
        Assert.DoesNotContain("CREATE TYPE", schema);
    }

    [Fact]
    public void SqlServerViewStoreCanEmitArtifactWithoutTableDdl()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            [InquiryView("VItems")]
            public sealed class VItem { [InquiryColumn] public int Id { get; set; } }
            public partial class VItemStore : InquiryStore<VItem>
            {
                [InquirySelectAllByPredicate, InquiryWhere("Id", Compare.In)]
                public partial Task<IReadOnlyList<VItem>> ByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct = default);
            }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var schema = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("ProviderArtifactsDdl", schema);
        Assert.Contains("CREATE TYPE [dbo].[Inquiry_Tvp_5fcff71acdcd2dc2f2d9b8c73ef6cfb000902eeb236c89d2221808eb2617bbee]", schema);
        Assert.DoesNotContain("IF OBJECT_ID", schema);
    }

    [Fact]
    public void SqlServerTvpArtifactEscapesArbitraryValidSchemaIdentifier()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate, InquiryWhere("CategoryId", Compare.In)]
            public partial Task<IReadOnlyList<Product>> InAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default);
            """).Replace("[InquiryTable(\"Products\")]", "[InquiryTable(\"Products\", Schema = \"9 odd].schema'\")]");
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.Contains("[9 odd]].schema'].[Inquiry_Tvp_5fcff71acdcd2dc2f2d9b8c73ef6cfb000902eeb236c89d2221808eb2617bbee]", generated);
        Assert.Contains("N'9 odd].schema'''", generated);
    }

    [Fact]
    public void SqlServerDiscoversExistsUpdateConverterAndEnumArtifactsFromEffectiveProviderTypes()
    {
        const string source = """
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry; using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            public readonly record struct StrongId(long Value);
            public sealed class StrongIdConverter : IInquiryValueConverter<StrongId, long>
            { public long ToProvider(StrongId value) => value.Value; public StrongId FromProvider(long value) => new(value); }
            public enum State { One, Two }
            [InquiryTable("Items")]
            public sealed class Item
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Converter = typeof(StrongIdConverter))] public StrongId ExternalId { get; set; }
                [InquiryColumn, InquiryEnumAsString] public State State { get; set; }
                [InquiryColumn] public decimal Price { get; set; }
            }
            public partial class FirstStore : InquiryStore<Item>
            {
                [InquiryExists, InquiryWhere("ExternalId", Compare.In)]
                public partial Task<bool> ExistsAsync(IReadOnlyList<StrongId> ids, CancellationToken ct = default);
            }
            public partial class SecondStore : InquiryStore<Item>
            {
                [InquiryUpdateWhere("Price"), InquiryWhere("State", Compare.In)]
                public partial Task<int> UpdateAsync(decimal price, IReadOnlyList<State> states, CancellationToken ct = default);
            }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.Contains("AS TABLE ([Value] BIGINT NOT NULL)", generated);
        Assert.Contains("AS TABLE ([Value] NVARCHAR(MAX) NOT NULL)", generated);
        Assert.Contains("InquiryTvpParameter.Bind(_c, \"@ExternalId\"", generated);
        Assert.Contains("InquiryTvpParameter.Bind(_c, \"@State\"", generated);
        Assert.DoesNotContain("BindUnsupported", generated);
    }

    [Fact]
    public void SqlServerUnsupportedCollectionUsesExplicitUnsupportedPathAndNoArtifact()
    {
        const string source = """
            using System; using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry; using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("Items")] public sealed class Item { [InquiryKey] public int Id { get; set; } [InquiryColumn] public DateOnly Day { get; set; } }
            public partial class ItemStore : InquiryStore<Item>
            { [InquiryExists, InquiryWhere("Day", Compare.In)] public partial Task<bool> ExistsAsync(IReadOnlyList<DateOnly> days, CancellationToken ct = default); }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains("InquiryTvpParameter.BindUnsupported(_c, \"@Day\", days)", generated);
        Assert.DoesNotContain("ProviderArtifactsDdl", generated);
    }

    [Theory]
    [InlineData("Sqlite", "InquiryJsonArrayParameter.Bind(_c, \"@CategoryId\", ids)")]
    [InlineData("PostgreSql", "InquiryArrayParameter.Bind(_c, \"@CategoryId\", ids)")]
    public void NonSqlServerCollectionOutputRemainsArtifactFree(string dialect, string binder)
    {
        var source = PredicateSource("""
            [InquiryExists, InquiryWhere("CategoryId", Compare.In)]
            public partial Task<bool> ExistsAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default);
            """);
        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains(binder, generated);
        Assert.DoesNotContain("ProviderArtifactsDdl", generated);
        Assert.DoesNotContain("BindUnsupported", generated);
    }

    [Fact]
    public void SqlServerArtifactOutputIsStoreOrderInvariantAndDeduplicatedAcrossStores()
    {
        const string prelude = """
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry; using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryView("VItems")] public sealed class Item { [InquiryColumn] public int Id { get; set; } [InquiryColumn] public string Code { get; set; } = ""; }
            """;
        const string first = """
            public partial class AStore : InquiryStore<Item>
            { [InquiryExists, InquiryWhere("Id", Compare.In)] public partial Task<bool> A(IReadOnlyList<int> ids, CancellationToken ct = default); }
            """;
        const string second = """
            public partial class BStore : InquiryStore<Item>
            {
                [InquiryExists, InquiryWhere("Id", Compare.In)] public partial Task<bool> B(IReadOnlyList<int> ids, CancellationToken ct = default);
                [InquiryExists, InquiryWhere("Code", Compare.In)] public partial Task<bool> C(IReadOnlyList<string> codes, CancellationToken ct = default);
            }
            """;
        var left = RunGenerator(prelude + first + second, dialect: "SqlServer");
        var right = RunGenerator(prelude + second + first, dialect: "SqlServer");
        AssertNoErrors(left);
        AssertNoErrors(right);
        var leftSchema = Assert.Single(left.RunResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        var rightSchema = Assert.Single(right.RunResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Equal(leftSchema, rightSchema);
        Assert.Equal(2, global::System.Text.RegularExpressions.Regex.Matches(leftSchema, "CREATE TYPE").Count);
    }

    [Fact]
    public void InvalidStubbedOperationDoesNotContributeArtifact()
    {
        var source = PredicateSource("""
            [InquiryExists, InquiryWhere("CategoryId", Compare.In)]
            public partial Task<int> InvalidAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default);
            """);
        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(result.RunResult.Diagnostics, static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.DoesNotContain("ProviderArtifactsDdl", generated);
    }

    [Fact]
    public void SqlServerCustomSchemaSetupIsDeduplicatedBeforeItsTypes()
    {
        const string source = """
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry; using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryView("V", Schema = "z].schema")]
            public sealed class Item { [InquiryColumn] public int Id { get; set; } [InquiryColumn] public string Code { get; set; } = ""; }
            public partial class ItemStore : InquiryStore<Item>
            {
                [InquiryExists, InquiryWhere("Id", Compare.In)] public partial Task<bool> A(IReadOnlyList<int> ids, CancellationToken ct = default);
                [InquiryExists, InquiryWhere("Code", Compare.In)] public partial Task<bool> B(IReadOnlyList<string> codes, CancellationToken ct = default);
            }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var schema = Assert.Single(result.RunResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Single(global::System.Text.RegularExpressions.Regex.Matches(schema, "SCHEMA_ID\\(N'z].schema'\\)").Cast<global::System.Text.RegularExpressions.Match>());
        Assert.True(schema.IndexOf("CREATE SCHEMA [z]].schema]", StringComparison.Ordinal) < schema.IndexOf("CREATE TYPE [z]].schema]", StringComparison.Ordinal));
    }
}
