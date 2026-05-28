using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// CRUD for Northwind shippers — IDENTITY-keyed, no eager-load relations.
/// </summary>
public sealed class ShipperService
{
    private readonly ShipperStore _store;

    public ShipperService(ShipperStore store)
    {
        _store = store;
    }

    public async Task<List<Shipper>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = await _store.SelectAllAsync(cancellationToken).ConfigureAwait(false);
        return (List<Shipper>)list;
    }

    public Task<Shipper?> GetByKeyAsync(int? shipperID, CancellationToken cancellationToken = default)
        => _store.SelectByKeyAsync(shipperID, cancellationToken);

    public Task<Shipper?> CreateAsync(Shipper shipper, CancellationToken cancellationToken = default)
        => _store.InsertReturningAsync(shipper, cancellationToken);

    public Task<bool> UpdateAsync(Shipper shipper, CancellationToken cancellationToken = default)
        => _store.UpdateAsync(shipper, cancellationToken);

    public Task<bool> DeleteAsync(int? shipperID, CancellationToken cancellationToken = default)
        => _store.DeleteByKeyAsync(shipperID, cancellationToken);
}
