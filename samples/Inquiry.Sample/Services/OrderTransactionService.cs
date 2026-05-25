using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// Inserts an <see cref="Order"/> together with its <c>Order Details</c> rows inside a single
/// <see cref="IInquiryTransaction"/>. Demonstrates two things:
/// <list type="bullet">
///   <item><see cref="OrderStore.InsertReturningAsync"/> retrieving the IDENTITY-assigned OrderID.</item>
///   <item>Falling back to raw SQL via <see cref="IInquiry.ExecuteAsync"/> for <c>Order Details</c>,
///         which has a composite primary key and therefore no generated store (see
///         Inquiry.Northwind/LIMITATIONS.md).</item>
/// </list>
/// </summary>
public sealed class OrderTransactionService
{
    private readonly IInquiry _inquiry;
    private readonly OrderStore _orders;
    private readonly ProductStore _products;

    public OrderTransactionService(IInquiry inquiry, OrderStore orders, ProductStore products)
    {
        _inquiry = inquiry;
        _orders = orders;
        _products = products;
    }

    public sealed record Result(int OrderID, int ProductLines);

    public async Task<Result> RunAsync(string customerID, CancellationToken cancellationToken = default)
    {
        // Pick a couple of products to put on the new order. The seed always inserts at least 2.
        var picks = new List<Product>();
        await foreach (var p in _products.SelectAllAsync(cancellationToken).ConfigureAwait(false))
        {
            picks.Add(p);
            if (picks.Count == 2) break;
        }

        if (picks.Count == 0)
        {
            throw new InvalidOperationException("No products available — seed data missing.");
        }

        await using var tx = await _inquiry.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        // Insert the Order (IDENTITY) via the generated store routed through the transactional facade.
        var insertedOrder = await tx.Inquiry
            .QuerySingleOrDefaultAsync<Order>(
                "INSERT INTO Orders (CustomerID, OrderDate, Freight) VALUES (@cid, @date, @freight) RETURNING *",
                new { cid = customerID, date = DateTime.UtcNow, freight = 5.00m },
                cancellationToken)
            .ConfigureAwait(false);

        if (insertedOrder is null || insertedOrder.OrderID is null)
        {
            throw new InvalidOperationException("Order insert did not return a row.");
        }

        // Order Details has a composite PK, so there is no generated store — raw SQL it is.
        foreach (var p in picks)
        {
            await tx.Inquiry.ExecuteAsync(
                """
                INSERT INTO "Order Details" (OrderID, ProductID, UnitPrice, Quantity, Discount)
                VALUES (@oid, @pid, @price, @qty, 0)
                """,
                new { oid = insertedOrder.OrderID, pid = p.ProductID, price = p.UnitPrice ?? 0m, qty = (short)1 },
                cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new Result(insertedOrder.OrderID.Value, picks.Count);
    }
}
