using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class ShipperStore : InquiryStore<Shipper>
{

    [InquirySelectAll]
    public partial Task<IReadOnlyList<Shipper>> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Shipper?> SelectByKeyAsync(int? shipperID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("CompanyName")]
    public partial Task<IReadOnlyList<Shipper>> SelectByCompanyNameAsync(string companyName, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Shipper shipper, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<Shipper?> InsertReturningAsync(Shipper shipper, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Shipper shipper, CancellationToken cancellationToken = default);

#if !INQUIRY_ORACLE_TESTS
    [InquiryUpsert]
    public partial Task<int> UpsertAsync(Shipper shipper, CancellationToken cancellationToken = default);
#endif

    [InquiryDelete]
    public partial Task<bool> DeleteByKeyAsync(int? shipperID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAllAsync(IEnumerable<Shipper> shippers, CancellationToken cancellationToken = default);

    [InquiryBulkInsert]
    public partial Task<long> BulkInsertAsync(IEnumerable<Shipper> shippers, CancellationToken cancellationToken = default);
}
