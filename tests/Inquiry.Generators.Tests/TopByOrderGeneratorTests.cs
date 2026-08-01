using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// SelectTopByOrder emission tests: <c>[InquirySelectTopByOrder("col")]</c> emits a
/// <c>SELECT … ORDER BY col ASC LIMIT 1</c> (or dialect equivalent) const and the method
/// uses the single-row query path.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string TopByOrderEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TSale")]
        public sealed class Sale
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("Amount")]
            public decimal Amount { get; set; }

            [InquiryColumn("Region")]
            public string Region { get; set; } = string.Empty;
        }
        """;

    private static string SaleStore(string methods) =>
        TopByOrderEntity + "\n\npublic partial class SaleStore : Inquiry.Stores.InquiryStore<Demo.Sale>\n{\n" + methods + "\n}\n";

    private static string GetSaleStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("SaleStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void TopByOrderEmitsOrderByAndLimit_Sqlite()
    {
        var result = RunGenerator(SaleStore("""
            [InquirySelectTopByOrder("Amount")]
            public partial Task<Sale?> GetCheapestAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetSaleStore(result);

        Assert.Contains("ORDER BY \\\"Amount\\\" ASC LIMIT 1", text);
        Assert.Contains("_sqlTop_GetCheapestAsync", text);
    }

    [Fact]
    public void TopByOrderDescendingEmitsDescOrderByAndLimit_Sqlite()
    {
        var result = RunGenerator(SaleStore("""
            [InquirySelectTopByOrder("Amount", Descending = true)]
            public partial Task<Sale?> GetMostExpensiveAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetSaleStore(result);

        Assert.Contains("ORDER BY \\\"Amount\\\" DESC LIMIT 1", text);
    }

    [Fact]
    public void TopByOrderUsesGeneratedGuaranteedSingleOrDefault()
    {
        var result = RunGenerator(SaleStore("""
            [InquirySelectTopByOrder("Amount")]
            public partial Task<Sale?> GetCheapestAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetSaleStore(result);

        Assert.Contains("QueryGeneratedSingleOrDefaultAsync<global::Demo.Sale, byte,", text);
        Assert.Contains("new global::Inquiry.Commands.InquiryGeneratedCommand<byte>(_sqlTop_GetCheapestAsync, default, static (_, _) => { })", text);
    }

    [Fact]
    public void TopByOrder_SqlServer()
    {
        var result = RunGenerator(SaleStore("""
            [InquirySelectTopByOrder("Amount")]
            public partial Task<Sale?> GetCheapestAsync(CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetSaleStore(result);

        Assert.Contains("ORDER BY [Amount] ASC OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY", text);
    }

    [Fact]
    public void TopByOrder_Oracle()
    {
        var result = RunGenerator(SaleStore("""
            [InquirySelectTopByOrder("Amount")]
            public partial Task<Sale?> GetCheapestAsync(CancellationToken cancellationToken = default);
            """), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetSaleStore(result);

        Assert.Contains("ORDER BY", text);
        Assert.Contains("OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY", text);
    }

    [Fact]
    public void TopByOrder_PostgreSql()
    {
        var result = RunGenerator(SaleStore("""
            [InquirySelectTopByOrder("Amount")]
            public partial Task<Sale?> GetCheapestAsync(CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetSaleStore(result);

        Assert.Contains("LIMIT 1", text);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void TopByOrder_MySql(string dialect)
    {
        var result = RunGenerator(SaleStore("""
            [InquirySelectTopByOrder("Amount")]
            public partial Task<Sale?> GetCheapestAsync(CancellationToken cancellationToken = default);
            """), dialect: dialect);
        AssertNoErrors(result);
        var text = GetSaleStore(result);

        Assert.Contains("LIMIT 1", text);
    }

    [Fact]
    public void TopByOrderUnknownColumnReportsINQ007()
    {
        var result = RunGenerator(SaleStore("""
            [InquirySelectTopByOrder("Amountt")]
            public partial Task<Sale?> GetCheapestAsync(CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ007" && d.Severity == DiagnosticSeverity.Error);
        // The method must be dropped rather than crashing the run: no ORDER BY const is emitted and
        // the generator does not fault (CS8785).
        Assert.DoesNotContain(result.GeneratorDiagnostics, static d => d.Id == "CS8785");
        Assert.DoesNotContain(
            result.RunResult.GeneratedTrees,
            static t => t.GetText().ToString().Contains("_sqlTop_GetCheapestAsync", StringComparison.Ordinal));
    }
}
