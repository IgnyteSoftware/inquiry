using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class CustomerCustomerDemoStore : InquiryStore<CustomerCustomerDemo>
{

    [InquirySelectAll]
    public partial IAsyncEnumerable<CustomerCustomerDemo> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<CustomerCustomerDemo?> SelectByKeyAsync(string customerID, string customerTypeID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(CustomerCustomerDemo entry, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public partial Task<bool> DeleteByKeyAsync(string customerID, string customerTypeID, CancellationToken cancellationToken = default);
}
