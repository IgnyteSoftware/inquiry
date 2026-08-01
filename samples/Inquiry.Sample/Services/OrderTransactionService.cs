using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// Inserts an <see cref="Order"/> together with its <c>Order Details</c> rows inside a single
/// <see cref="IInquiryTransaction"/>. Demonstrates that generated stores automatically
/// participate in the active transaction — both the IDENTITY-keyed <see cref="OrderStore"/>
/// (via <c>InsertReturning</c> to surface the new <c>OrderID</c>) and the composite-key
/// <see cref="OrderDetailStore"/> are called normally, with the ambient transaction wiring
/// in <c>DefaultInquiry</c> routing all of their commands through one connection + commit.
/// </summary>
public sealed class OrderTransactionService
{
    private readonly IInquiry _inquiry;
    private readonly OrderStore _orders;
    private readonly OrderDetailStore _orderDetails;
    private readonly ProductStore _products;

    public OrderTransactionService(IInquiry inquiry, OrderStore orders, OrderDetailStore orderDetails, ProductStore products)
    {
        _inquiry = inquiry;
        _orders = orders;
        _orderDetails = orderDetails;
        _products = products;
    }

    public sealed record Result(int OrderID, int ProductLines);

    public async Task<Result> RunAsync(string customerID, CancellationToken cancellationToken = default)
    {
        // Pick a couple of products to put on the new order. The seed always inserts at least 2.
        var allProducts = await _products.SelectAllAsync(cancellationToken).ConfigureAwait(false);
        var picks = allProducts.Take(2).ToList();

        if (picks.Count == 0)
        {
            throw new InvalidOperationException("No products available — seed data missing.");
        }

        await using var tx = await _inquiry.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var insertedOrder = await _orders.InsertReturningAsync(
            new Order { CustomerID = customerID, OrderDate = DateTime.UtcNow, Freight = 5.00m },
            cancellationToken).ConfigureAwait(false);

        if (insertedOrder is null || insertedOrder.OrderID is null)
        {
            throw new InvalidOperationException("Order insert did not return a row.");
        }

        foreach (var p in picks)
        {
            await _orderDetails.InsertAsync(new OrderDetail
            {
                OrderID = insertedOrder.OrderID.Value,
                ProductID = p.ProductID ?? throw new InvalidOperationException("Product missing ProductID."),
                UnitPrice = p.UnitPrice ?? 0m,
                Quantity = 1,
                Discount = 0f,
            }, cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new Result(insertedOrder.OrderID.Value, picks.Count);
    }
}
