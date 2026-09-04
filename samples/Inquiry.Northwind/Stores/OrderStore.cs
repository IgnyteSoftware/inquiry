using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class OrderStore : InquiryStore<Order>
{

    [InquirySelectAll]
    public partial IAsyncEnumerable<Order> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Order?> SelectByKeyAsync(int? orderID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("CustomerID")]
    public partial IAsyncEnumerable<Order> SelectByCustomerAsync(string? customerID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("CustomerID", "EmployeeID")]
    public partial IAsyncEnumerable<Order> SelectByCustomerAndEmployeeAsync(string? customerID, int? employeeID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Order order, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public partial Task<Order?> InsertReturningAsync(Order order, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Order order, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteByKeyAsync(int? orderID, CancellationToken cancellationToken = default);
}
