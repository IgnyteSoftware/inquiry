using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public abstract partial class ShipperStore : InquiryStore<Shipper>
{
    protected ShipperStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<Shipper> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<Shipper?> SelectByKeyAsync(int? shipperID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("CompanyName")]
    public abstract IAsyncEnumerable<Shipper> SelectByCompanyNameAsync(string companyName, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Shipper shipper, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public abstract Task<Shipper?> InsertReturningAsync(Shipper shipper, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Shipper shipper, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public abstract Task<int> UpsertAsync(Shipper shipper, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(int? shipperID, CancellationToken cancellationToken = default);
}
