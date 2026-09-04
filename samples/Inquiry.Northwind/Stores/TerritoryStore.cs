using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class TerritoryStore : InquiryStore<Territory>
{

    [InquirySelectAll]
    public partial IAsyncEnumerable<Territory> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Territory?> SelectByKeyAsync(string territoryID, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<Territory> SelectAllWithRegionAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<Territory?> SelectByKeyWithRegionAsync(string territoryID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("RegionID")]
    public partial IAsyncEnumerable<Territory> SelectByRegionAsync(int regionID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Territory territory, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Territory territory, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteByKeyAsync(string territoryID, CancellationToken cancellationToken = default);
}
