using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// Reads orders and their composite-key <c>Order Details</c> lines. Exposes both the
/// IDENTITY-keyed <see cref="OrderStore"/> and the composite-key
/// <see cref="OrderDetailStore"/>; the by-field selectors
/// (<see cref="OrderStore.SelectByCustomerAsync"/>,
/// <see cref="OrderDetailStore.SelectByOrderAsync"/>) demonstrate
/// <c>[InquirySelectAllByField]</c> on both kinds of keys.
/// </summary>
public sealed class OrderService
{
    private readonly OrderStore _orders;
    private readonly OrderDetailStore _orderDetails;

    public OrderService(OrderStore orders, OrderDetailStore orderDetails)
    {
        _orders = orders;
        _orderDetails = orderDetails;
    }

    public async Task<List<Order>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Order>();
        await foreach (var o in _orders.SelectAllAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(o);
        }
        return list;
    }

    public async Task<List<Order>> GetByCustomerAsync(string? customerID, CancellationToken cancellationToken = default)
    {
        var list = new List<Order>();
        await foreach (var o in _orders.SelectByCustomerAsync(customerID, cancellationToken).ConfigureAwait(false))
        {
            list.Add(o);
        }
        return list;
    }

    public async Task<List<OrderDetail>> GetDetailsAsync(int orderID, CancellationToken cancellationToken = default)
    {
        var list = new List<OrderDetail>();
        await foreach (var d in _orderDetails.SelectByOrderAsync(orderID, cancellationToken).ConfigureAwait(false))
        {
            list.Add(d);
        }
        return list;
    }

    public Task<bool> UpdateOrderAsync(Order order, CancellationToken cancellationToken = default)
        => _orders.UpdateAsync(order, cancellationToken);

    /// <summary>
    /// Deletes the order and its detail lines in one transaction so the foreign-key
    /// dependency from <c>Order Details</c> → <c>Orders</c> never becomes invalid mid-way.
    /// Demonstrates a multi-store transaction across an IDENTITY-keyed parent and a
    /// composite-key child.
    /// </summary>
    public async Task<bool> DeleteOrderAsync(IInquiry inquiry, int orderID, CancellationToken cancellationToken = default)
    {
        await using var tx = await inquiry.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var lines = new List<OrderDetail>();
        await foreach (var d in _orderDetails.SelectByOrderAsync(orderID, cancellationToken).ConfigureAwait(false))
        {
            lines.Add(d);
        }
        foreach (var d in lines)
        {
            await _orderDetails.DeleteByKeyAsync(d.OrderID, d.ProductID, cancellationToken).ConfigureAwait(false);
        }

        var deleted = await _orders.DeleteByKeyAsync(orderID, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }
}
