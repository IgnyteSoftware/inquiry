using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// Operations against the composite-key <c>EmployeeTerritories</c> bridge — the int + string
/// composite-key shape Inquiry supports out of the box.
/// </summary>
public sealed class EmployeeTerritoryService
{
    private readonly EmployeeTerritoryStore _store;

    public EmployeeTerritoryService(EmployeeTerritoryStore store)
    {
        _store = store;
    }

    public async Task<List<EmployeeTerritory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<EmployeeTerritory>();
        await foreach (var t in _store.SelectAllAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(t);
        }
        return list;
    }

    public async Task<List<EmployeeTerritory>> GetByEmployeeAsync(int employeeID, CancellationToken cancellationToken = default)
    {
        var list = new List<EmployeeTerritory>();
        await foreach (var t in _store.SelectByEmployeeAsync(employeeID, cancellationToken).ConfigureAwait(false))
        {
            list.Add(t);
        }
        return list;
    }

    public Task<int> AssignAsync(int employeeID, string territoryID, CancellationToken cancellationToken = default)
        => _store.InsertAsync(new EmployeeTerritory { EmployeeID = employeeID, TerritoryID = territoryID }, cancellationToken);

    public Task<bool> UnassignAsync(int employeeID, string territoryID, CancellationToken cancellationToken = default)
        => _store.DeleteByKeyAsync(employeeID, territoryID, cancellationToken);
}
