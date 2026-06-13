using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Derived query methods: a field-less <c>[InquirySelectAllByField]</c> infers its filter columns
/// from the method name (<c>SelectByCountryAndCityAsync</c> → Country AND City), the Spring Data
/// convention done at compile time.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string DerivedEntityHeader = """
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
            public string CompanyName { get; set; } = string.Empty;

            [InquiryColumn("Country")]
            public string? Country { get; set; }

            [InquiryColumn("City")]
            public string? City { get; set; }
        }

        public partial class CustomerStore : InquiryStore<Customer>
        {
        """;

    [Fact]
    public void SingleFieldDerivedFromMethodName()
    {
        var source = DerivedEntityHeader + """
                [InquirySelectAllByField]
                public partial Task<IReadOnlyList<Customer>> SelectByCompanyNameAsync(string companyName, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("private const string _sqlSelectBy_CompanyName = \"SELECT \\\"Id\\\", \\\"CompanyName\\\", \\\"Country\\\", \\\"City\\\" FROM \\\"Customer\\\" WHERE \\\"CompanyName\\\" = @CompanyName\";", text);
    }

    [Fact]
    public void MultipleFieldsDerivedAndSplitOnAnd()
    {
        var source = DerivedEntityHeader + """
                [InquirySelectAllByField]
                public partial Task<IReadOnlyList<Customer>> SelectByCountryAndCityAsync(string country, string city, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("WHERE \\\"Country\\\" = @Country AND \\\"City\\\" = @City", text);
    }

    [Fact]
    public void ExplicitFieldsStillOverrideDerivation()
    {
        var source = DerivedEntityHeader + """
                [InquirySelectAllByField("City")]
                public partial Task<IReadOnlyList<Customer>> SelectByCompanyNameAsync(string city, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        // The explicit "City" wins over the name's "CompanyName".
        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("WHERE \\\"City\\\" = @City", text);
        Assert.DoesNotContain("@CompanyName", text);
    }

    [Fact]
    public void MethodNameWithoutByReportsINQ054()
    {
        var source = DerivedEntityHeader + """
                [InquirySelectAllByField]
                public partial Task<IReadOnlyList<Customer>> SelectEverythingAsync(string companyName, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ054");
    }

    [Fact]
    public void DerivedUnknownFieldReportsINQ007()
    {
        var source = DerivedEntityHeader + """
                [InquirySelectAllByField]
                public partial Task<IReadOnlyList<Customer>> SelectByNonsenseAsync(string nonsense, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ007");
    }
}
