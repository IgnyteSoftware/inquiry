using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// CRUD + country-lookup operations for Northwind customers. Pages depend on this service,
/// not on the generated <see cref="CustomerStore"/>.
/// </summary>
public sealed class CustomerService
{
    private readonly CustomerStore _store;

    public CustomerService(CustomerStore store)
    {
        _store = store;
    }

    public async Task<List<Customer>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // The buffered store returns IReadOnlyList<T> backed by a List<T>; cast to surface
        // the concrete type to Blazor pages without re-copying.
        var list = await _store.SelectAllAsync(cancellationToken).ConfigureAwait(false);
        return (List<Customer>)list;
    }

    public Task<Customer?> GetByKeyAsync(string customerID, CancellationToken cancellationToken = default)
        => _store.SelectByKeyAsync(customerID, cancellationToken);

    public async Task<List<Customer>> GetByCountryAsync(string country, CancellationToken cancellationToken = default)
    {
        var list = await _store.SelectByCountryAsync(country, cancellationToken).ConfigureAwait(false);
        return (List<Customer>)list;
    }

    public Task<int> CreateAsync(Customer customer, CancellationToken cancellationToken = default)
        => _store.InsertAsync(customer, cancellationToken);

    public Task<bool> UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
        => _store.UpdateAsync(customer, cancellationToken);

    public Task<bool> DeleteAsync(string customerID, CancellationToken cancellationToken = default)
        => _store.DeleteByKeyAsync(customerID, cancellationToken);
}
