using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class CustomerDemographicStore : InquiryStore<CustomerDemographic>
{

    [InquirySelectAll]
    public partial IAsyncEnumerable<CustomerDemographic> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<CustomerDemographic?> SelectByKeyAsync(string customerTypeID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(CustomerDemographic demographic, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(CustomerDemographic demographic, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public partial Task<bool> DeleteByKeyAsync(string customerTypeID, CancellationToken cancellationToken = default);
}
