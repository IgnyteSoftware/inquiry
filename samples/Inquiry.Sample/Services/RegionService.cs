using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// CRUD + eager-load operations for Northwind regions and their child territories.
/// Demonstrates <see cref="RegionStore.SelectAllWithTerritoriesAsync"/> (eager all) and
/// <see cref="TerritoryStore.SelectByRegionAsync"/> (by-field lookup) — both generated from
/// declarative attributes on the abstract stores.
/// </summary>
public sealed class RegionService
{
    private readonly RegionStore _regions;
    private readonly TerritoryStore _territories;

    public RegionService(RegionStore regions, TerritoryStore territories)
    {
        _regions = regions;
        _territories = territories;
    }

    public async Task<List<Region>> GetAllWithTerritoriesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Region>();
        await foreach (var r in _regions.SelectAllWithTerritoriesAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(r);
        }
        return list;
    }

    public Task<int> CreateRegionAsync(Region region, CancellationToken cancellationToken = default)
        => _regions.InsertAsync(region, cancellationToken);

    public Task<bool> UpdateRegionAsync(Region region, CancellationToken cancellationToken = default)
        => _regions.UpdateAsync(region, cancellationToken);

    public Task<bool> DeleteRegionAsync(int regionID, CancellationToken cancellationToken = default)
        => _regions.DeleteByKeyAsync(regionID, cancellationToken);

    public Task<int> CreateTerritoryAsync(Territory territory, CancellationToken cancellationToken = default)
        => _territories.InsertAsync(territory, cancellationToken);

    public Task<bool> UpdateTerritoryAsync(Territory territory, CancellationToken cancellationToken = default)
        => _territories.UpdateAsync(territory, cancellationToken);

    public Task<bool> DeleteTerritoryAsync(string territoryID, CancellationToken cancellationToken = default)
        => _territories.DeleteByKeyAsync(territoryID, cancellationToken);
}
