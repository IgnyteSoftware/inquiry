using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// CRUD for Northwind suppliers. Suppliers use an IDENTITY-backed key.
/// </summary>
public sealed class SupplierService
{
    private readonly SupplierStore _store;

    public SupplierService(SupplierStore store)
    {
        _store = store;
    }

    public async Task<List<Supplier>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Supplier>();
        await foreach (var s in _store.SelectAllAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(s);
        }
        return list;
    }

    public Task<Supplier?> GetByKeyAsync(int? supplierID, CancellationToken cancellationToken = default)
        => _store.SelectByKeyAsync(supplierID, cancellationToken);

    public Task<Supplier?> CreateAsync(Supplier supplier, CancellationToken cancellationToken = default)
        => _store.InsertReturningAsync(supplier, cancellationToken);

    public Task<bool> UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default)
        => _store.UpdateAsync(supplier, cancellationToken);

    public Task<bool> DeleteAsync(int? supplierID, CancellationToken cancellationToken = default)
        => _store.DeleteByKeyAsync(supplierID, cancellationToken);
}
