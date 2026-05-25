using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public abstract partial class RegionStore : InquiryStore<Region>
{
    protected RegionStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<Region> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<Region?> SelectByKeyAsync(int regionID, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public abstract IAsyncEnumerable<Region> SelectAllWithTerritoriesAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public abstract Task<Region?> SelectByKeyWithTerritoriesAsync(int regionID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Region region, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Region region, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(int regionID, CancellationToken cancellationToken = default);
}
