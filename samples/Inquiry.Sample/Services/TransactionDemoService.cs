using Inquiry.Sample.Models;

namespace Inquiry.Sample.Services;

/// <summary>
/// Demonstrates Inquiry's transactional API by inserting a category and several products
/// inside a single <see cref="IInquiryTransaction"/>. The whole operation either commits
/// or rolls back atomically.
/// </summary>
public sealed class TransactionDemoService
{
    private readonly IInquiry _inquiry;

    public TransactionDemoService(IInquiry inquiry)
    {
        _inquiry = inquiry;
    }

    public sealed record Result(Guid CategoryKey, int ProductsInserted);

    public async Task<Result> RunAsync(int productCount = 3, CancellationToken cancellationToken = default)
    {
        var category = new Category
        {
            Key = Guid.NewGuid(),
            Name = $"Tx Demo {DateTime.UtcNow:HH:mm:ss}",
        };

        await using var tx = await _inquiry.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            await tx.Inquiry.ExecuteAsync(
                "INSERT INTO TCategory (Key, Name) VALUES (@Key, @Name)",
                new { category.Key, category.Name },
                cancellationToken).ConfigureAwait(false);

            for (var i = 1; i <= productCount; i++)
            {
                var product = new Product
                {
                    Key = Guid.NewGuid(),
                    Name = $"TX Product {i}",
                    Price = i * 9.99m,
                    CategoryKey = category.Key,
                };

                await tx.Inquiry.ExecuteAsync(
                    "INSERT INTO TProduct (Key, Name, Price, CategoryKey) VALUES (@Key, @Name, @Price, @CategoryKey)",
                    new { product.Key, product.Name, product.Price, product.CategoryKey },
                    cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new Result(category.Key, productCount);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
