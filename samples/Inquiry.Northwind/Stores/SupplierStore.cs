using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class SupplierStore : InquiryStore<Supplier>
{

    [InquirySelectAll]
    public partial IAsyncEnumerable<Supplier> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Supplier?> SelectByKeyAsync(int? supplierID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Supplier supplier, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public partial Task<Supplier?> InsertReturningAsync(Supplier supplier, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public partial Task<bool> DeleteByKeyAsync(int? supplierID, CancellationToken cancellationToken = default);
}
