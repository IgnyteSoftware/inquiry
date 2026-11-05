using System.Data;
using System.Data.Common;
using Inquiry.FeatureCatalog;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Testing;

namespace Inquiry.Tests.Shared;

/// <summary>
/// Shared seed, act and assert steps for generated <c>InquiryPagedResult&lt;T&gt;</c> methods (#403). Each
/// provider suite builds its own harness; the assertions live here so the six copies cannot drift.
/// </summary>
/// <remarks>
/// A paged result sends the page SELECT and the COUNT as one multi-result command. These checks prove
/// one finalized command with two result sets, one interceptor record, one connection checkout, and that
/// both statements see the same predicate, collection and global-filter parameters, each bound once.
/// </remarks>
internal static class PagedResultAssertions
{
    /// <summary>
    /// Creates a probe that records the name and direction of every parameter on each finalized command.
    /// </summary>
    internal static BatchExecutionProbe CreateProbe()
        => new(inspectFinalizedCommand: static command => command.Parameters
            .Cast<DbParameter>()
            .Select(static p => (p.ParameterName, p.Direction))
            .ToArray());

    internal static async Task PageAndTotalTravelAsOneCommandAsync(
        CategoryStore categories,
        ProductStore products,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await SeedProductsAsync(categories, products);
        Reset(probe, recorder);

        var page = await products.PageByDiscontinuedAsync(false, 0, 2);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal("Chai, Chang", string.Join(", ", page.Items.Select(static p => p.ProductName)));
        AssertOneCommandOnOneConnection(probe, recorder, "Products");
    }

    internal static async Task PagePastTheEndKeepsTheTotalAsync(
        CategoryStore categories,
        ProductStore products,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await SeedProductsAsync(categories, products);
        Reset(probe, recorder);

        var page = await products.PageByDiscontinuedAsync(false, 10, 5);

        // The page result set is empty, so the grid must still advance to the COUNT result set.
        Assert.Empty(page.Items);
        Assert.Equal(3, page.TotalCount);
        AssertOneCommandOnOneConnection(probe, recorder, "Products");
    }

    internal static async Task EmptyTableReturnsNoItemsAndZeroAsync(
        ProductStore products,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        Reset(probe, recorder);

        var page = await products.PageByDiscontinuedAsync(false, 0, 5);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        AssertOneCommandOnOneConnection(probe, recorder, "Products");
    }

    internal static async Task PredicateFiltersBothStatementsAsync(
        CategoryStore categories,
        ProductStore products,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await SeedProductsAsync(categories, products);
        Reset(probe, recorder);

        var page = await products.PageByDiscontinuedAsync(true, 0, 1);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("Chef Anton's Cajun", Assert.Single(page.Items).ProductName);
        AssertOneCommandOnOneConnection(probe, recorder, "Products");
    }

    /// <summary>
    /// NOT IN always expands a text sentinel, and the combined command carries it twice: once in the page
    /// SELECT and once in the COUNT. Both must expand to the same list.
    /// </summary>
    internal static async Task NotInCollectionFiltersBothStatementsAsync(
        CategoryStore categories,
        ProductStore products,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        var (c1, _) = await SeedProductsAsync(categories, products);
        Reset(probe, recorder);

        // NOT IN (c1) excludes the c1 products and, by SQL null semantics, the uncategorized one.
        var page = await products.PageNotInCategoriesAsync([c1], 0, 1);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("Aniseed Syrup", Assert.Single(page.Items).ProductName);
        AssertOneCommandOnOneConnection(probe, recorder, "Products");

        Reset(probe, recorder);

        // An empty NOT IN excludes nothing, in both statements.
        var all = await products.PageNotInCategoriesAsync([], 0, 2);

        Assert.Equal(5, all.TotalCount);
        Assert.Equal("Chai, Chang", string.Join(", ", all.Items.Select(static p => p.ProductName)));
        AssertOneCommandOnOneConnection(probe, recorder, "Products");
    }

    /// <summary>
    /// Inside a transaction the paged command uses the transaction's connection: no new checkout, and the
    /// grid releases the connection so the next operation on the same transaction runs.
    /// </summary>
    internal static async Task PagedResultRunsInsideATransactionAsync(
        IInquiry inquiry,
        CategoryStore categories,
        ProductStore products,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await SeedProductsAsync(categories, products);

        await using var transaction = await inquiry.BeginTransactionAsync(IsolationLevel.Serializable);
        Reset(probe, recorder);

        var page = await products.PageByDiscontinuedAsync(false, 0, 2);
        var next = await products.PageByDiscontinuedAsync(false, 2, 2);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, next.TotalCount);
        Assert.Equal("Aniseed Syrup", Assert.Single(next.Items).ProductName);
        Assert.Equal(0, probe.ConnectionOpenCount);
        Assert.Equal(2, probe.FinalizedCommands.Count);
        await transaction.CommitAsync();
    }

    /// <summary>
    /// The parameterized tenant filter and the constant <c>IsActive</c> filter apply to both statements.
    /// A rows-only or count-only filter would show up as a total that disagrees with the visible rows.
    /// </summary>
    internal static async Task GlobalFiltersApplyToBothStatementsAsync(
        TenantScopedDocStore store,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        // Writes carry no filter, so seeding needs no scope.
        await store.InsertAsync(new TenantScopedDoc { TenantId = 1, Title = "T1 a" });
        await store.InsertAsync(new TenantScopedDoc { TenantId = 1, Title = "T1 inactive", IsActive = false });
        await store.InsertAsync(new TenantScopedDoc { TenantId = 1, Title = "T1 b" });
        await store.InsertAsync(new TenantScopedDoc { TenantId = 2, Title = "T2 a" });
        await store.InsertAsync(new TenantScopedDoc { TenantId = 2, Title = "T2 b" });
        await store.InsertAsync(new TenantScopedDoc { TenantId = 2, Title = "T2 c" });

        using (InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = 1L }))
        {
            Reset(probe, recorder);
            var page = await store.PageAsync(0, 1);

            Assert.Equal(2, page.TotalCount);
            Assert.Equal("T1 a", Assert.Single(page.Items).Title);
            AssertOneCommandOnOneConnection(probe, recorder, "TenantScopedDoc");
        }

        using (InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = 2L }))
        {
            Reset(probe, recorder);
            var page = await store.PageAsync(1, 5);

            Assert.Equal(3, page.TotalCount);
            Assert.Equal("T2 b, T2 c", string.Join(", ", page.Items.Select(static d => d.Title)));
            AssertOneCommandOnOneConnection(probe, recorder, "TenantScopedDoc");
        }
    }

    internal static async Task<(int C1, int C2)> SeedProductsAsync(CategoryStore categories, ProductStore products)
    {
        await categories.InsertAsync(new Category { CategoryName = "Beverages" });
        await categories.InsertAsync(new Category { CategoryName = "Condiments" });
        var cats = new List<Category>();
        await foreach (var category in categories.SelectAllAsync())
        {
            cats.Add(category);
        }

        var c1 = cats.Single(static c => c.CategoryName == "Beverages").CategoryID!.Value;
        var c2 = cats.Single(static c => c.CategoryName == "Condiments").CategoryID!.Value;

        // Inserted in ProductID order, so "ORDER BY ProductID" returns them in this order.
        await products.InsertAsync(new Product { ProductName = "Chai", UnitPrice = 18m, UnitsInStock = 39, CategoryID = c1, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Chang", UnitPrice = 19m, UnitsInStock = 17, CategoryID = c1, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Aniseed Syrup", UnitPrice = 10m, UnitsInStock = 13, CategoryID = c2, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Chef Anton's Cajun", UnitPrice = 22m, UnitsInStock = 53, CategoryID = c2, Discontinued = true });
        await products.InsertAsync(new Product { ProductName = "Uncategorized", UnitPrice = 5m, UnitsInStock = 0, CategoryID = null, Discontinued = true });
        return (c1, c2);
    }

    private static void Reset(BatchExecutionProbe probe, RecordingCommandInterceptor recorder)
    {
        recorder.Clear();
        probe.Reset();
    }

    private static void AssertOneCommandOnOneConnection(BatchExecutionProbe probe, RecordingCommandInterceptor recorder, string table)
    {
        EagerGridCommandAssertions.AssertSingleGridCommand(probe, recorder, expectedResultSets: 2, table);
        Assert.Equal(1, probe.ConnectionOpenCount);

        var command = Assert.Single(probe.FinalizedCommands);
        if (command.CommandText.Contains("DBMS_SQL.RETURN_RESULT", StringComparison.Ordinal))
        {
            // Oracle returns both result sets from one PL/SQL block through implicit results.
            Assert.StartsWith("DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR SELECT ", command.CommandText, StringComparison.Ordinal);
            Assert.EndsWith("; DBMS_SQL.RETURN_RESULT(c); END;", command.CommandText, StringComparison.Ordinal);
        }

        // Each parameter is bound once and is an input. The COUNT statement reuses the page's parameters,
        // and no output cursor parameter is added to the Oracle block.
        var parameters = Assert.IsType<(string Name, ParameterDirection Direction)[]>(command.Metadata);
        Assert.Equal(parameters.Length, parameters.Select(static p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(parameters, static p => Assert.Equal(ParameterDirection.Input, p.Direction));
    }
}
