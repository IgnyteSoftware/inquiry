using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class RegionStore : InquiryStore<Region>
{

    [InquirySelectAll]
    public partial IAsyncEnumerable<Region> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Region?> SelectByKeyAsync(int regionID, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<Region> SelectAllWithTerritoriesAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<Region?> SelectByKeyWithTerritoriesAsync(int regionID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Region region, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Region region, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public partial Task<bool> DeleteByKeyAsync(int regionID, CancellationToken cancellationToken = default);

    [InquiryDeleteAll]
    public partial Task<int> DeleteAllAsync(IEnumerable<int> regionIDs, CancellationToken cancellationToken = default);

    // Batch insert/update over a region collection.
    [InquiryInsertAll]
    public partial Task<int> InsertAllAsync(IEnumerable<Region> regions, CancellationToken cancellationToken = default);

    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IEnumerable<Region> regions, CancellationToken cancellationToken = default);
}
