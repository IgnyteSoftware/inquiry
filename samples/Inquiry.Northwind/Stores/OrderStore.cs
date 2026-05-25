using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public abstract partial class OrderStore : InquiryStore<Order>
{
    protected OrderStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<Order> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<Order?> SelectByKeyAsync(int? orderID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("CustomerID")]
    public abstract IAsyncEnumerable<Order> SelectByCustomerAsync(string? customerID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("CustomerID", "EmployeeID")]
    public abstract IAsyncEnumerable<Order> SelectByCustomerAndEmployeeAsync(string? customerID, int? employeeID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Order order, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public abstract Task<Order?> InsertReturningAsync(Order order, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Order order, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(int? orderID, CancellationToken cancellationToken = default);
}
