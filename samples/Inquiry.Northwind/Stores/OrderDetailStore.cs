using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class OrderDetailStore : InquiryStore<OrderDetail>
{

    [InquirySelectAll]
    public partial IAsyncEnumerable<OrderDetail> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<OrderDetail?> SelectByKeyAsync(int orderID, int productID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("OrderID")]
    public partial IAsyncEnumerable<OrderDetail> SelectByOrderAsync(int orderID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(OrderDetail orderDetail, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(OrderDetail orderDetail, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteByKeyAsync(int orderID, int productID, CancellationToken cancellationToken = default);
}
