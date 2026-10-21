using System.ComponentModel.DataAnnotations;
using Inquiry.Paging;

namespace Inquiry.Sample.Data;

public enum ProductSort { Name, NameDescending, Price, PriceDescending }

public sealed record ProductSearch(
    string? Name = null, int? CategoryId = null, bool Unassigned = false,
    decimal? Minimum = null, decimal? Maximum = null, bool InStock = false,
    ProductSort Sort = ProductSort.Name, int Offset = 0, int Limit = 10);

public sealed class ProductWorkspace(
    IInquiry inquiry, IWorkspaceProductStore products, WorkspaceCategoryStore categories)
{
    public Task<IReadOnlyList<WorkspaceCategory>> CategoriesAsync(CancellationToken ct = default)
        => categories.ListAsync(ct);

    public async Task<InquiryPagedResult<WorkspaceProduct>> SearchAsync(int workspaceId, ProductSearch search, CancellationToken ct = default)
    {
        using var scope = Enter(workspaceId);
        if (search.Minimum < 0 || search.Maximum < 0 || search.Minimum > search.Maximum)
        {
            throw new ValidationException("Enter a valid price range.");
        }

        if (search.Offset < 0 || search.Limit is < 1 or > 100)
        {
            throw new ValidationException("Choose a page size between 1 and 100.");
        }

        var name = string.IsNullOrWhiteSpace(search.Name) ? null : "%" + search.Name.Trim() + "%";
        int? stock = search.InStock ? 1 : null;
        return await ((search.Sort, search.Unassigned) switch
        {
            (ProductSort.Name, false) => products.NameAsync(name, search.Minimum, search.Maximum, stock, search.CategoryId, search.Offset, search.Limit, ct),
            (ProductSort.NameDescending, false) => products.NameDescendingAsync(name, search.Minimum, search.Maximum, stock, search.CategoryId, search.Offset, search.Limit, ct),
            (ProductSort.Price, false) => products.PriceAsync(name, search.Minimum, search.Maximum, stock, search.CategoryId, search.Offset, search.Limit, ct),
            (ProductSort.PriceDescending, false) => products.PriceDescendingAsync(name, search.Minimum, search.Maximum, stock, search.CategoryId, search.Offset, search.Limit, ct),
            (ProductSort.Name, true) => products.NameUnassignedAsync(name, search.Minimum, search.Maximum, stock, search.Offset, search.Limit, ct),
            (ProductSort.NameDescending, true) => products.NameDescendingUnassignedAsync(name, search.Minimum, search.Maximum, stock, search.Offset, search.Limit, ct),
            (ProductSort.Price, true) => products.PriceUnassignedAsync(name, search.Minimum, search.Maximum, stock, search.Offset, search.Limit, ct),
            (ProductSort.PriceDescending, true) => products.PriceDescendingUnassignedAsync(name, search.Minimum, search.Maximum, stock, search.Offset, search.Limit, ct),
            _ => throw new ValidationException("Choose a supported sort order.")
        });
    }

    public async Task<InventoryTotals> TotalsAsync(int workspaceId, CancellationToken ct = default)
    {
        using var scope = Enter(workspaceId);
        // Handwritten SQL does not inherit generated filters. Keep both predicates explicit.
        return (await inquiry.QuerySingleOrDefaultAsync<InventoryTotals>(
            $"""
            SELECT COUNT(*), COALESCE(SUM(Stock), 0), COALESCE(SUM(Price * Stock), 0)
            FROM ShowcaseProducts WHERE WorkspaceId = {workspaceId} AND IsDeleted = 0
            """, ct))!;
    }

    public async Task<WorkspaceProduct?> FindAsync(int workspaceId, int id, CancellationToken ct = default)
    {
        using var scope = Enter(workspaceId);
        return await products.FindAsync(id, ct);
    }

    public async Task<WorkspaceProduct?> SaveAsync(int workspaceId, WorkspaceProduct product, CancellationToken ct = default)
    {
        using var scope = Enter(workspaceId);
        if (product.WorkspaceId != workspaceId || product.IsDeleted)
        {
            throw new ValidationException("This product does not belong to the current workspace.");
        }

        product.Name = (product.Name ?? "").Trim();
        product.Description = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description.Trim();
        Validator.ValidateObject(product, new ValidationContext(product), validateAllProperties: true);
        if (product.CategoryId is not null && !(await categories.ListAsync(ct)).Any(c => c.Id == product.CategoryId))
        {
            throw new ValidationException("Choose an existing category.");
        }

        if (product.Id == 0)
        {
            return await products.CreateAsync(product, ct);
        }

        return await inquiry.ExecuteInTransactionAsync<WorkspaceProduct?>(async (_, token) =>
        {
            var current = await products.FindAsync(product.Id, token);
            if (current is null || current.Version != product.Version)
            {
                return null;
            }

            return await products.SaveAsync(product, token);
        }, cancellationToken: ct);
    }

    public async Task<bool> ArchiveAsync(int workspaceId, WorkspaceProduct product, CancellationToken ct = default)
    {
        using var scope = Enter(workspaceId);
        if (product.WorkspaceId != workspaceId || product.Id == 0)
        {
            throw new ValidationException("Choose a saved product in this workspace.");
        }

        return await inquiry.ExecuteInTransactionAsync(async (_, token) =>
        {
            var current = await products.FindAsync(product.Id, token);
            if (current is null || current.Version != product.Version)
            {
                return false;
            }

            return await products.ArchiveAsync(product, token);
        }, cancellationToken: ct);
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await inquiry.ExecuteInTransactionAsync(async (_, token) =>
        {
            if ((await categories.ListAsync(token)).Count != 0)
            {
                return;
            }

            await categories.SeedAsync(new[]
            {
                new WorkspaceCategory { Id = 1, Name = "Coffee & tea" },
                new WorkspaceCategory { Id = 2, Name = "Pantry" },
                new WorkspaceCategory { Id = 3, Name = "Kitchen" },
                new WorkspaceCategory { Id = 4, Name = "Gifts" }
            }, token);
            string[] names = ["House blend", "Earl Grey", "Olive oil", "Ceramic mug", "Gift hamper", "Espresso roast",
                "Wildflower honey", "Linen apron", "Matcha tea", "Sea salt", "Pour-over set", "Tasting box",
                "Decaf blend", "Cocoa powder", "Glass carafe", "Breakfast bundle", "Chamomile", "Maple syrup",
                "Measuring spoons", "Weekend hamper", "Cold brew", "Fruit preserve", "Tea strainer", "Discovery set"];
            int[] categoryIds = [1, 1, 2, 3, 4, 1, 2, 3, 1, 2, 3, 4, 1, 2, 3, 4, 1, 2, 3, 4, 1, 2, 3, 4];
            foreach (var workspaceId in new[] { 1, 2 })
            {
                var rows = names.Select((name, index) => new WorkspaceProduct
                {
                    WorkspaceId = workspaceId,
                    Name = workspaceId == 1 ? name : "Market " + name,
                    CategoryId = index % 7 == 0 ? null : categoryIds[index],
                    Price = decimal.Round(5.50m + index * 2.25m + workspaceId, 2),
                    Stock = index % 5 == 0 ? 0 : index * 3 + workspaceId,
                    Description = "A considered addition to your everyday essentials."
                });
                await products.SeedAsync(rows, token);
            }
        }, cancellationToken: ct);
    }

    private static IDisposable Enter(int workspaceId)
    {
        if (workspaceId is not (1 or 2))
        {
            throw new ValidationException("Choose Northwind or Market House.");
        }

        return InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["WorkspaceId"] = workspaceId });
    }
}
