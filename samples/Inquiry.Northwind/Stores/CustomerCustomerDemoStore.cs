using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public abstract partial class CustomerCustomerDemoStore : InquiryStore<CustomerCustomerDemo>
{
    protected CustomerCustomerDemoStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<CustomerCustomerDemo> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<CustomerCustomerDemo?> SelectByKeyAsync(string customerID, string customerTypeID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(CustomerCustomerDemo entry, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(string customerID, string customerTypeID, CancellationToken cancellationToken = default);
}
