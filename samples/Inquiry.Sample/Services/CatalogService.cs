using Inquiry.Sample.Models;
using Inquiry.Sample.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// Catalog operations spanning <see cref="Category"/> and <see cref="Product"/>.
/// Demonstrates Inquiry's eager-loading via <c>[InquirySelectAllEager]</c> and
/// <c>[InquirySelectOneByKeyEager]</c>.
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

    public Task<Category?> GetCategoryWithProductsAsync(Guid key, CancellationToken cancellationToken = default)
        => _categories.SelectByKeyWithProductsAsync(key, cancellationToken);

    public Task<int> CreateCategoryAsync(Category category, CancellationToken cancellationToken = default)
        => _categories.InsertAsync(category, cancellationToken);

    public Task<int> UpsertProductAsync(Product product, CancellationToken cancellationToken = default)
        => _products.UpsertAsync(product, cancellationToken);

    public Task<bool> DeleteProductAsync(Guid key, CancellationToken cancellationToken = default)
        => _products.DeleteByKeyAsync(key, cancellationToken);
}
