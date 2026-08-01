using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Each entity emits a <c>static partial class {Entity}InquirySql</c> with <c>ColumnList</c> and
/// (for tables) <c>InsertColumnList</c> as <c>const string</c> fields, so raw SQL queries can
/// reference dialect-correct, compile-time column lists.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ColumnListSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Product")]
        public sealed class Product
        {
            [InquiryKey(IsGenerated = true)]
            public int Id { get; set; }

            [InquiryColumn]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn]
            public decimal Price { get; set; }
        }

        public partial class ProductStore : InquiryStore<Product>
        {
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Product>> AllAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void ColumnListContainsAllColumns()
    {
        var result = RunGenerator(ColumnListSource);
        AssertNoErrors(result);

        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Product.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = entity.GetText().ToString();

        Assert.Contains("public static partial class ProductInquirySql", text);
        Assert.Contains("public const string ColumnList = \"\\\"Id\\\", \\\"Name\\\", \\\"Price\\\"\";", text);
    }

    [Fact]
    public void InsertColumnListExcludesGeneratedKey()
    {
        var result = RunGenerator(ColumnListSource);
        AssertNoErrors(result);

        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Product.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = entity.GetText().ToString();

        Assert.Contains("public const string InsertColumnList = \"\\\"Name\\\", \\\"Price\\\"\";", text);
    }

    [Fact]
    public void InsertColumnListExcludesComputedAndDatabaseDefaultColumns()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Audit")]
            public sealed class Audit
            {
                [InquiryKey]
                public Guid Id { get; set; }

                [InquiryColumn]
                public string Action { get; set; } = string.Empty;

                [InquiryColumn(UseDatabaseDefault = true)]
                public DateTime CreatedAt { get; set; }

                [InquiryColumn(Computed = "Action + ' done'")]
                public string Summary { get; set; } = string.Empty;

                [InquiryConcurrencyToken(DatabaseGenerated = true)]
                public byte[] RowVersion { get; set; } = null!;
            }

            public partial class AuditStore : InquiryStore<Audit>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<Audit>> AllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);

        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Audit.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = entity.GetText().ToString();

        // ColumnList includes everything
        Assert.Contains("[Id], [Action], [CreatedAt], [Summary], [RowVersion]", text);
        // InsertColumnList excludes UseDatabaseDefault (CreatedAt), Computed (Summary),
        // and IsDatabaseGeneratedToken (RowVersion) — only Id and Action remain
        Assert.Contains("public const string InsertColumnList = \"[Id], [Action]\";", text);
    }

    [Fact]
    public void ViewEntityGetsColumnListButNoInsertColumnList()
    {
        var result = RunGenerator(ViewSource);
        AssertNoErrors(result);

        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerOrderSummary.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = entity.GetText().ToString();

        Assert.Contains("public static partial class CustomerOrderSummaryInquirySql", text);
        Assert.Contains("public const string ColumnList =", text);
        Assert.DoesNotContain("InsertColumnList", text);
    }

    [Fact]
    public void SqlServerDialectUsesSquareBracketQuoting()
    {
        var result = RunGenerator(ColumnListSource, dialect: "SqlServer");
        AssertNoErrors(result);

        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Product.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = entity.GetText().ToString();

        Assert.Contains("public const string ColumnList = \"[Id], [Name], [Price]\";", text);
    }

    [Fact]
    public void MySqlDialectUsesBacktickQuoting()
    {
        var result = RunGenerator(ColumnListSource, dialect: "MySql");
        AssertNoErrors(result);

        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Product.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = entity.GetText().ToString();

        Assert.Contains("public const string ColumnList = \"`Id`, `Name`, `Price`\";", text);
    }

    private const string DuplicateColumnSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Product")]
        public sealed class Product
        {
            [InquiryKey(IsGenerated = true)]
            public int Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn("Name")]
            public string DisplayName { get; set; } = string.Empty;
        }
        """;

    [Fact]
    public void DuplicateColumnIsReportedAndEmittedOnlyOnce()
    {
        var result = RunGenerator(DuplicateColumnSource);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ002" && d.Severity == DiagnosticSeverity.Error);
        AssertColumnListHasNoRepeatedIdentifier(result);
    }

    [Fact]
    public void DuplicateColumnIsDroppedEvenWhenInq002IsSuppressed()
    {
        // A consumer can downgrade INQ002 in .editorconfig. The emitted SQL must still be valid, so
        // the later duplicate is dropped independently of the diagnostic's severity.
        var result = RunGenerator(
            DuplicateColumnSource,
            additionalDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["INQ002"] = ReportDiagnostic.Suppress });

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ002");
        AssertColumnListHasNoRepeatedIdentifier(result);
    }

    private static void AssertColumnListHasNoRepeatedIdentifier(GeneratorTestResult result)
    {
        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Product.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = entity.GetText().ToString();

        // The first property wins; the later one mapping to the same column is dropped so neither the
        // SELECT list nor the INSERT list repeats the identifier.
        Assert.Contains("public const string ColumnList = \"\\\"Id\\\", \\\"Name\\\"\";", text);
        Assert.Contains("public const string InsertColumnList = \"\\\"Name\\\"\";", text);
    }
}
