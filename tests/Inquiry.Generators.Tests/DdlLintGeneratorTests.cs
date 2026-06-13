using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// DDL safety lints — advisory diagnostics that are off by default (opt in via .editorconfig). INQ061:
/// a foreign-key column with no index — most engines don't auto-index FKs, so joins/cascades over them
/// scan; dialect-aware (MySQL auto-indexes FK constraints and is exempt) and suppressed when the column
/// is already indexed/unique. INQ062: a decimal column with no explicit precision, which takes the
/// dialect default and can silently round; dialect-agnostic, suppressed when Precision or SqlType is set.
/// Tests opt the diagnostics on via the harness.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private static readonly string[] EnableDdlLints = { "INQ061", "INQ062" };

    private const string UnindexedFkSource = """
        using Inquiry.Entities;

        namespace Demo;

        [InquiryTable("Book")]
        public sealed class Book
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryForeignKey("AuthorId", "Author", "Id")]
            public long AuthorId { get; set; }
        }

        [InquiryTable("Author")]
        public sealed class Author
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }
        }
        """;

    private const string IndexedFkSource = """
        using Inquiry.Entities;

        namespace Demo;

        [InquiryTable("Book")]
        public sealed class Book
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryForeignKey("AuthorId", "Author", "Id", IsIndexed = true)]
            public long AuthorId { get; set; }
        }

        [InquiryTable("Author")]
        public sealed class Author
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }
        }
        """;

    [Fact]
    public void UnindexedForeignKeyReportsINQ061AsInfo_Sqlite()
    {
        var result = RunGenerator(UnindexedFkSource, enableDiagnostics: EnableDdlLints);

        var lint = Assert.Single(result.RunResult.Diagnostics, d => d.Id == "INQ061");
        Assert.Equal(DiagnosticSeverity.Info, lint.Severity);
        Assert.Contains("AuthorId", lint.GetMessage());
    }

    [Fact]
    public void LintIsOffByDefaultWithoutOptIn_Sqlite()
    {
        // The unindexed FK exists, but the advisory is suppressed unless a consumer opts in.
        var result = RunGenerator(UnindexedFkSource);
        AssertNoErrors(result);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ061");
    }

    [Fact]
    public void IndexedForeignKeyDoesNotReportINQ061_Sqlite()
    {
        var result = RunGenerator(IndexedFkSource, enableDiagnostics: EnableDdlLints);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ061");
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("Oracle")]
    public void UnindexedForeignKeyReportsINQ061_OnNonAutoIndexingDialects(string dialect)
    {
        var result = RunGenerator(UnindexedFkSource, dialect: dialect, enableDiagnostics: EnableDdlLints);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ061");
    }

    [Fact]
    public void MySqlIsExemptBecauseItAutoIndexesForeignKeys()
    {
        // MySQL/InnoDB creates a backing index for every FK constraint, so the lint must not fire.
        var result = RunGenerator(UnindexedFkSource, dialect: "MySql", enableDiagnostics: EnableDdlLints);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ061");
    }

    [Fact]
    public void MySqlStillLintsWhenForeignKeyConstraintsAreSuppressed()
    {
        // With GenerateForeignKeys = false there is no constraint, so MySQL does not auto-index — lint applies.
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Book", GenerateForeignKeys = false)]
            public sealed class Book
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey("AuthorId", "Author", "Id")]
                public long AuthorId { get; set; }
            }

            [InquiryTable("Author")]
            public sealed class Author
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }
            }
            """;

        var result = RunGenerator(source, dialect: "MySql", enableDiagnostics: EnableDdlLints);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ061");
    }

    // ---- INQ062: decimal column relies on the default precision/scale -----------------------------

    private const string DecimalSource = """
        using Inquiry.Entities;

        namespace Demo;

        [InquiryTable("Invoice")]
        public sealed class Invoice
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Amount")]
            public decimal Amount { get; set; }
        }
        """;

    [Fact]
    public void DecimalWithoutPrecisionReportsINQ062AsInfo_Sqlite()
    {
        var result = RunGenerator(DecimalSource, enableDiagnostics: EnableDdlLints);

        var lint = Assert.Single(result.RunResult.Diagnostics, d => d.Id == "INQ062");
        Assert.Equal(DiagnosticSeverity.Info, lint.Severity);
        Assert.Contains("Amount", lint.GetMessage());
    }

    [Fact]
    public void DecimalLintIsOffByDefaultWithoutOptIn_Sqlite()
    {
        var result = RunGenerator(DecimalSource);
        AssertNoErrors(result);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ062");
    }

    [Fact]
    public void DecimalLintIsDialectAgnostic_FiresOnMySql()
    {
        // INQ062 has no dialect exemption (unlike INQ061's MySQL FK case); the FK-auto-index restructure
        // must not accidentally suppress it.
        var result = RunGenerator(DecimalSource, dialect: "MySql", enableDiagnostics: EnableDdlLints);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ062");
    }

    [Fact]
    public void ScaleWithoutPrecisionStillReportsINQ062_Sqlite()
    {
        // Scale without Precision is silently ignored by DecimalSpec (which gates on Precision > 0), so
        // the column still takes the default precision — the lint must fire.
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Invoice")]
            public sealed class Invoice
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Amount", Scale = 4)]
                public decimal Amount { get; set; }
            }
            """;

        var result = RunGenerator(source, enableDiagnostics: EnableDdlLints);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ062");
    }

    [Theory]
    [InlineData("[InquiryColumn(\"Amount\", Precision = 19, Scale = 4)]")]   // explicit precision/scale
    [InlineData("[InquiryColumn(\"Amount\", Precision = 10)]")]              // precision alone is enough
    [InlineData("[InquiryColumn(\"Amount\", SqlType = \"NUMERIC(19,4)\")]")]  // explicit SqlType override
    public void ExplicitDecimalStorageDoesNotReportINQ062(string columnAttribute)
    {
        var source = $$"""
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Invoice")]
            public sealed class Invoice
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                {{columnAttribute}}
                public decimal Amount { get; set; }
            }
            """;

        var result = RunGenerator(source, enableDiagnostics: EnableDdlLints);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ062");
    }
}
