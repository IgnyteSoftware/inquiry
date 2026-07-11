using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string DeleteReturningSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TWidget")]
        public sealed class Widget
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryColumn] public string Name { get; set; } = string.Empty;
        }

        public partial class WidgetStore : InquiryStore<Widget>
        {
            [InquiryDeleteOneByKey(ReturnEntity = true)]
            public partial Task<Widget?> DeleteReturningAsync(long id, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void MariaDbDeleteReturningEmitsNativeSqlAndNullableEntityResult()
    {
        var result = RunGenerator(DeleteReturningSource, dialect: "MariaDb");
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains("_sqlDeleteReturning = \"DELETE FROM `TWidget` WHERE `Id` = @Id RETURNING `Id`, `Name`\"", text);
        Assert.Contains("QuerySingleOrDefaultAsync<global::Demo.Widget, long,", text);
        Assert.Contains("return await Inquiry.QuerySingleOrDefaultAsync", text);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("Oracle")]
    [InlineData("MySql")]
    public void UnsupportedDialectsReportINQ039AndEmitStub(string dialect)
    {
        var result = RunGenerator(DeleteReturningSource, dialect: dialect);

        Assert.Contains(result.RunResult.Diagnostics,
            d => d.Id == "INQ039" && d.Severity == DiagnosticSeverity.Warning);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("throw new global::System.NotSupportedException", text);
        Assert.DoesNotContain("_sqlDeleteReturning", text);
    }

    [Fact]
    public void MariaDbSoftDeleteReturningReportsINQ039InsteadOfPhysicallyDeleting()
    {
        var source = WidgetStore("""
            [InquiryDeleteOneByKey(ReturnEntity = true)]
            public partial Task<Widget?> DeleteReturningAsync(long id, CancellationToken cancellationToken = default);
            """);
        var result = RunGenerator(source, dialect: "MariaDb");

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ039");
        var text = GetWidgetStore(result);
        Assert.DoesNotContain("DELETE FROM", text);
        Assert.DoesNotContain("_sqlSoftDeleteReturning", text);
    }

    [Fact]
    public void MariaDbHardDeleteReturningOverridesSoftDeleteAndUsesNativeSql()
    {
        var source = WidgetStore("""
            [InquiryDeleteOneByKey(ReturnEntity = true, HardDelete = true)]
            public partial Task<Widget?> PurgeReturningAsync(long id, CancellationToken cancellationToken = default);
            """);
        var result = RunGenerator(source, dialect: "MariaDb");
        AssertNoErrors(result);

        Assert.Contains("_sqlDeleteReturning = \"DELETE FROM `TWidget` WHERE `Id` = @Id RETURNING `Id`, `Name`, `IsDeleted`\"", GetWidgetStore(result));
    }

    [Fact]
    public void MariaDbConcurrencyDeleteReturningBindsKeyAndTokenAndGuardsNull()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TDocument")]
            public sealed class Document
            {
                [InquiryKey] public long Id { get; set; }
                [InquiryColumn, InquiryConcurrencyToken] public int Version { get; set; }
                [InquiryColumn] public string Name { get; set; } = string.Empty;
            }

            public partial class DocumentStore : InquiryStore<Document>
            {
                [InquiryDeleteOneByKey(ReturnEntity = true)]
                public partial Task<Document?> DeleteReturningAsync(Document document, CancellationToken cancellationToken = default);
            }
            """;
        var result = RunGenerator(source, dialect: "MariaDb");
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("DocumentStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains("WHERE `Id` = @Id AND `Version` = @Version RETURNING", text);
        Assert.Contains("_e.Id", text);
        Assert.Contains("_e.Version", text);
        Assert.Contains("if (_result is null && Inquiry.ThrowOnConcurrencyConflict)", text);
    }

    [Fact]
    public void DeleteReturningRejectsNonEntityTask()
    {
        var source = DeleteReturningSource.Replace("Task<Widget?> DeleteReturningAsync", "Task<bool> DeleteReturningAsync", StringComparison.Ordinal);
        var result = RunGenerator(source, dialect: "MariaDb");

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ005");
    }

    [Fact]
    public void MariaDbCompositeKeyDeleteReturningBindsEveryKey()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TEnrollment")]
            public sealed class Enrollment
            {
                [InquiryKey] public long StudentId { get; set; }
                [InquiryKey] public int CourseId { get; set; }
                [InquiryColumn] public string Grade { get; set; } = string.Empty;
            }

            public partial class EnrollmentStore : InquiryStore<Enrollment>
            {
                [InquiryDeleteOneByKey(ReturnEntity = true)]
                public partial Task<Enrollment?> DeleteReturningAsync(long studentId, int courseId, CancellationToken cancellationToken = default);
            }
            """;
        var result = RunGenerator(source, dialect: "MariaDb");
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("EnrollmentStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains("DELETE FROM `TEnrollment` WHERE `StudentId` = @StudentId AND `CourseId` = @CourseId RETURNING", text);
        Assert.Contains("(studentId, courseId)", text);
        Assert.Contains("_keys.Item1", text);
        Assert.Contains("_keys.Item2", text);
        Assert.Contains("_p0.ParameterName = \"@StudentId\"", text);
        Assert.Contains("_p1.ParameterName = \"@CourseId\"", text);
    }
}
