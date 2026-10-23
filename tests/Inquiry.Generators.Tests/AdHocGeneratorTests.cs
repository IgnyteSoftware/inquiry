namespace Inquiry.Generators.Tests;

/// <summary>
/// Ad-hoc DTOs: <c>[InquiryAdHoc]</c> emits class + struct materializers that map every publicly
/// settable property to the result column with its name, and registers the class materializer in DI so
/// the ad-hoc <c>IInquiry.Query*</c> path can resolve it — no entity, table, or store involved.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string SalesReportSource = """
        using System;
        using Inquiry.Entities;

        namespace Demo;

        [InquiryAdHoc]
        public sealed class CategorySales
        {
            public string Category { get; set; } = string.Empty;

            public decimal TotalAmount { get; set; }

            public int OrderCount { get; set; }

            public DateTime? LastOrderedAt { get; set; }
        }
        """;

    [Fact]
    public void AdHocDtoEmitsNameMappedMaterializers()
    {
        var result = RunGenerator(SalesReportSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CategorySales.InquiryAdHoc.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("class CategorySalesInquiryAdHocMaterializer", text);
        Assert.Contains("readonly struct CategorySalesInquiryAdHocStructMaterializer", text);
        Assert.Contains("public bool IsInquirySequentialAccessSafe => true;", text);
        Assert.DoesNotContain("IInquiryEntityMaterializer<global::Demo.CategorySales>.IsInquirySequentialAccessSafe", text);
        Assert.Contains("value0 = reader.GetString(0);", text);
        Assert.Contains("value2 = reader.GetInt32(2);", text);
        Assert.Contains("IInquiryResultSetBinder<global::Demo.CategorySales, global::Inquiry.Materialization.IInquiryEntityMaterializer<global::Demo.CategorySales>>", text);
        Assert.Contains("IInquiryResultSetBinder<global::Demo.CategorySales, CategorySalesInquiryAdHocStructMaterializer>", text);
        Assert.Contains("new string[] { \"Category\", \"TotalAmount\", \"OrderCount\", \"LastOrderedAt\" }", text);
        Assert.Contains("value1 = reader.GetDecimal(ordinals[1]);", text);
        Assert.Contains("value3 = reader.IsDBNull(ordinals[3]) ? (global::System.DateTime?)null : reader.GetDateTime(ordinals[3]);", text);
    }

    [Fact]
    public void AdHocDtoRegistersClassMaterializerInDi()
    {
        var result = RunGenerator(SalesReportSource);
        AssertNoErrors(result);

        var registration = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedServiceRegistration.g.cs", StringComparison.Ordinal));
        var text = registration.GetText().ToString();

        Assert.Contains("TryAddSingleton<global::Inquiry.Materialization.IInquiryEntityMaterializer<global::Demo.CategorySales>, global::Demo.CategorySalesInquiryAdHocMaterializer>(services);", text);
    }

    [Fact]
    public void AdHocDtoAloneEmitsRegistrationWithoutEntitiesOrStores()
    {
        // SalesReportSource declares no [InquiryTable] entity and no store. The "nothing to
        // generate" early-out must still count ad-hoc DTOs, or a reporting-only assembly would
        // silently get no AddInquiryGeneratedStores() registration.
        var result = RunGenerator(SalesReportSource);
        AssertNoErrors(result);

        Assert.Contains(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedServiceRegistration.g.cs", StringComparison.Ordinal));
        // No schema file: there are no entities to emit DDL for.
        Assert.DoesNotContain(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void AdHocDtoSkipsUnsettableStaticAndPrivateSetProperties()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryAdHoc]
            public sealed class Slim
            {
                public static string Ignored { get; set; } = string.Empty;

                public string Name { get; set; } = string.Empty;

                public string Display => Name.ToUpperInvariant();

                public int Hidden { get; private set; }

                public long Count { get; set; }
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Slim.InquiryAdHoc.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Skipped properties need no column: Name and Count are the only mapped properties.
        Assert.Contains("new string[] { \"Name\", \"Count\" }", text);
        Assert.Contains("value0 = reader.GetString(0);", text);
        Assert.Contains("value1 = reader.GetInt64(1);", text);
        Assert.DoesNotContain("Ignored", text);
        Assert.DoesNotContain("Display", text);
        Assert.DoesNotContain("Hidden", text);
    }

    [Fact]
    public void AdHocRecordWithInitPropertiesAndEnumsCompiles()
    {
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            public enum Severity { Low, High }

            [InquiryAdHoc]
            public sealed record AlertRow
            {
                public Guid AlertId { get; init; }

                [InquiryEnumAsString]
                public Severity Severity { get; init; }

                public Severity NumericSeverity { get; init; }
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("AlertRow.InquiryAdHoc.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("value0 = reader.GetGuid(0);", text);
        Assert.Contains("value1 = global::System.Enum.Parse<global::Demo.Severity>(reader.GetString(1));", text);
        Assert.Contains("value1 = global::System.Enum.Parse<global::Demo.Severity>(reader.GetString(ordinals[1]));", text);
        Assert.Contains("value2 = unchecked((global::Demo.Severity)reader.GetInt32(2));", text);
        // The record's synthesized EqualityContract is get-only and must not be mapped.
        Assert.DoesNotContain("EqualityContract", text);
    }

    [Fact]
    public void AdHocDtoWithNoMappablePropertiesReportsINQ045()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryAdHoc]
            public sealed class Empty
            {
                public string Computed => "nothing settable";
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ045");
        Assert.DoesNotContain(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Empty.InquiryAdHoc.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void AdHocDtoWithPropertiesThatDifferOnlyByCaseReportsINQ106()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryAdHoc]
            public sealed class Ambiguous
            {
                public int Id { get; set; }

                public int ID { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ106" &&
            d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("'Id' and 'ID'", StringComparison.Ordinal));
        Assert.DoesNotContain(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Ambiguous.InquiryAdHoc.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void PositionalRecordAdHocDtoReportsINQ046()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryAdHoc]
            public sealed record RegionTotal(string Region, decimal Total);
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ046");
        Assert.DoesNotContain(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("RegionTotal.InquiryAdHoc.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void AbstractAdHocDtoReportsINQ046()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryAdHoc]
            public abstract class BaseReport
            {
                public string Name { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ046");
    }

    [Fact]
    public void AdHocDtoCoexistsWithEntityAndStoreRegistrations()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Customer")]
            public sealed class Customer
            {
                [InquiryKey]
                public string Id { get; set; } = string.Empty;

                [InquiryColumn("CompanyName")]
                public string Name { get; set; } = string.Empty;
            }

            [InquiryAdHoc]
            public sealed class CustomerCount
            {
                public string Name { get; set; } = string.Empty;

                public int Count { get; set; }
            }

            public partial class CustomerStore : Inquiry.Stores.InquiryStore<Demo.Customer>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<Customer>> AllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var registration = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedServiceRegistration.g.cs", StringComparison.Ordinal));
        var text = registration.GetText().ToString();

        Assert.Contains("IInquiryEntityMaterializer<global::Demo.Customer>, global::Demo.CustomerInquiryEntityMaterializer>(services);", text);
        Assert.Contains("IInquiryEntityMaterializer<global::Demo.CustomerCount>, global::Demo.CustomerCountInquiryAdHocMaterializer>(services);", text);
        Assert.Contains("TryAddScoped<global::Demo.CustomerStore>(services);", text);
    }
}
