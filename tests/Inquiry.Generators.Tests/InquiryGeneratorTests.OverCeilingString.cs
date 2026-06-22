using System.Linq;

namespace Inquiry.Generators.Tests;

/// <summary>
/// #113: a string Length beyond the dialect's fixed-width ceiling (SQL Server nvarchar 4000 / varchar 8000,
/// Oracle VARCHAR2 4000) maps to an unbounded text type (NVARCHAR(MAX) / CLOB). For a KEY or indexed column
/// that type cannot be keyed/indexed, so INQ031/INQ032 now fire (the over-ceiling case is folded into
/// MapsToUnboundedString); and Oracle maps an over-ceiling Length to CLOB rather than the illegal
/// VARCHAR2(>4000).
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void SqlServer_OverCeilingStringKey_ReportsInq031()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey("Code", Length = 5000)]
                public string Code { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ031");
    }

    [Fact]
    public void SqlServer_OverCeilingIndexedString_ReportsInq032AndSkipsIndex()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Slug", Length = 5000, IsIndexed = true)]
                public string Slug { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ032");

        var ddl = ExtractSchemaDdl(result);
        Assert.DoesNotContain("IX_Doc_Slug", ddl); // over-ceiling MAX column: index skipped, not emitted invalid
    }

    [Fact]
    public void Oracle_OverCeilingString_MapsToClobNotInvalidVarchar2()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Body", Length = 5000)]
                public string Body { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);

        var ddl = ExtractSchemaDdl(result);
        Assert.Contains("CLOB", ddl);
        Assert.DoesNotContain("VARCHAR2(5000)", ddl); // would be illegal — Oracle VARCHAR2 caps at 4000
    }

    [Fact]
    public void SqlServer_BoundaryStringKey4000_IsBoundedWithNoDiagnostic()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey("Code", Length = 4000)]
                public string Code { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ031");

        var ddl = ExtractSchemaDdl(result);
        Assert.Contains("NVARCHAR(4000)", ddl);
    }

    // The ansi (non-unicode) ceiling is 8000, a distinct code path from the unicode 4000 ceiling.
    [Fact]
    public void SqlServer_BoundaryAnsiStringKey8000_IsBoundedWithNoDiagnostic()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey("Code", Length = 8000, IsUnicode = false)]
                public string Code { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ031");

        var ddl = ExtractSchemaDdl(result);
        Assert.Contains("VARCHAR(8000)", ddl);
        Assert.DoesNotContain("VARCHAR(MAX)", ddl);
    }

    // Just over the unicode ceiling flips to MAX and fires INQ031 — locks the 4000/4001 boundary.
    [Fact]
    public void SqlServer_JustOverCeilingStringKey4001_FlipsToMaxAndReportsInq031()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey("Code", Length = 4001)]
                public string Code { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ031");

        var ddl = ExtractSchemaDdl(result);
        Assert.Contains("NVARCHAR(MAX)", ddl);
    }

    // MySQL's VARCHAR ceiling (~16383 utf8mb4 chars): an over-ceiling key maps to LONGTEXT and fires INQ031.
    [Fact]
    public void MySql_OverCeilingStringKey_MapsToLongTextAndReportsInq031()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey("Code", Length = 20000)]
                public string Code { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "MySql");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ031");

        var ddl = ExtractSchemaDdl(result);
        Assert.Contains("LONGTEXT", ddl);
        Assert.DoesNotContain("VARCHAR(20000)", ddl);
    }
}
