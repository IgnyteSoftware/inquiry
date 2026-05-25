using Inquiry.Sample.Models;
using Inquiry.Sample.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// CRUD operations for organizations, surfaced to Blazor pages without exposing the
/// generated store directly. Each Blazor page injects the service, not the store.
/// </summary>
public sealed class OrganizationService
{
    private readonly OrganizationStore _store;

    public OrganizationService(OrganizationStore store)
    {
        _store = store;
    }

    public async Task<List<Organization>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Organization>();
        await foreach (var item in _store.SelectAllAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(item);
        }
        return list;
    }

    public Task<Organization?> GetByKeyAsync(Guid key, CancellationToken cancellationToken = default)
        => _store.SelectByKeyAsync(key, cancellationToken);

    public Task<int> CreateAsync(Organization organization, CancellationToken cancellationToken = default)
        => _store.InsertAsync(organization, cancellationToken);

    public Task<bool> UpdateAsync(Organization organization, CancellationToken cancellationToken = default)
        => _store.UpdateAsync(organization, cancellationToken);

    public Task<bool> DeleteAsync(Guid key, CancellationToken cancellationToken = default)
        => _store.DeleteByKeyAsync(key, cancellationToken);
}
