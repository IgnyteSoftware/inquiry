using System.Linq;

namespace Inquiry.Generators.Tests;

/// <summary>
/// INQ065 (#103): [InquiryColumn] Length/Precision/Scale are read as raw ints with no validation, so a
/// negative Length, a Precision past the portable SQL maximum of 38, or a Scale exceeding its Precision
/// would produce invalid DDL (DECIMAL(99, …)) or break the generated binder. The diagnostic flags them at
/// the property; the SQL Server DDL also maps an over-fixed-width Length to a MAX type rather than emitting
/// an illegal NVARCHAR(5000).
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private static string ColumnRangeSource(string columnAttribute) => $$"""
        using Inquiry.Entities;

        namespace Demo;

        [InquiryTable("Money")]
        public sealed class Money
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            {{columnAttribute}}
            public decimal Amount { get; set; }
        }
        """;

    [Fact]
    public void PrecisionAbove38_ReportsInq065()
    {
        var result = RunGenerator(ColumnRangeSource("[InquiryColumn(\"Amount\", Precision = 99, Scale = 2)]"));
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ065");
    }

    [Fact]
    public void ScaleExceedingPrecision_ReportsInq065()
    {
        var result = RunGenerator(ColumnRangeSource("[InquiryColumn(\"Amount\", Precision = 2, Scale = 5)]"));
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ065");
    }

    [Fact]
    public void NegativeLength_ReportsInq065()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Tag")]
            public sealed class Tag
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Name", Length = -1)]
                public string Name { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ065");
    }

    [Fact]
    public void ValidMetadata_DoesNotReportInq065()
    {
        var result = RunGenerator(ColumnRangeSource("[InquiryColumn(\"Amount\", Precision = 18, Scale = 2)]"));
        AssertNoErrors(result);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ065");
    }

    // An over-fixed-width Length is not a range error (it's a legal MAX column), so it must NOT fire INQ065;
    // the SQL Server DDL maps it to NVARCHAR(MAX) instead of the illegal NVARCHAR(5000).
    [Fact]
    public void SqlServerOverLengthString_MapsToNvarcharMaxWithoutDiagnostic()
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

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ065");

        var ddl = ExtractSchemaDdl(result);
        Assert.Contains("[Body] NVARCHAR(MAX)", ddl);
        Assert.DoesNotContain("NVARCHAR(5000)", ddl);
    }
}
