using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public abstract partial class TerritoryStore : InquiryStore<Territory>
{
    protected TerritoryStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<Territory> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<Territory?> SelectByKeyAsync(string territoryID, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public abstract IAsyncEnumerable<Territory> SelectAllWithRegionAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public abstract Task<Territory?> SelectByKeyWithRegionAsync(string territoryID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("RegionID")]
    public abstract IAsyncEnumerable<Territory> SelectByRegionAsync(int regionID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Territory territory, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Territory territory, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(string territoryID, CancellationToken cancellationToken = default);
}
