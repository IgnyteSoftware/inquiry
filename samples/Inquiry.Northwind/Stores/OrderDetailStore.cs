using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public abstract partial class OrderDetailStore : InquiryStore<OrderDetail>
{
    protected OrderDetailStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<OrderDetail> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<OrderDetail?> SelectByKeyAsync(int orderID, int productID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("OrderID")]
    public abstract IAsyncEnumerable<OrderDetail> SelectByOrderAsync(int orderID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(OrderDetail orderDetail, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(OrderDetail orderDetail, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(int orderID, int productID, CancellationToken cancellationToken = default);
}
