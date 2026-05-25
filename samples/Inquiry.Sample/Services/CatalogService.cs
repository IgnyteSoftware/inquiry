using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// Catalog operations spanning <see cref="Category"/> and <see cref="Product"/>.
/// Uses Inquiry's eager-loading attributes via <see cref="CategoryStore.SelectAllWithProductsAsync"/>.
/// </summary>
public sealed class CatalogService
{
    private readonly CategoryStore _categories;
    private readonly ProductStore _products;

    public CatalogService(CategoryStore categories, ProductStore products)
    {
        _categories = categories;
        _products = products;
    }

    public async Task<List<Category>> GetCategoriesWithProductsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Category>();
        await foreach (var c in _categories.SelectAllWithProductsAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(c);
        }
        return list;
    }

    public Task<Category?> CreateCategoryAsync(Category category, CancellationToken cancellationToken = default)
        => _categories.InsertReturningAsync(category, cancellationToken);

    public Task<int> UpsertProductAsync(Product product, CancellationToken cancellationToken = default)
        => _products.UpsertAsync(product, cancellationToken);

    public Task<bool> DeleteProductAsync(int? productID, CancellationToken cancellationToken = default)
        => _products.DeleteByKeyAsync(productID, cancellationToken);
}
