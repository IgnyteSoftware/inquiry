using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// CRUD operations for Northwind employees.
/// </summary>
public sealed class EmployeeService
{
    private readonly EmployeeStore _store;

    public EmployeeService(EmployeeStore store)
    {
        _store = store;
    }

    public async Task<List<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Employee>();
        await foreach (var e in _store.SelectAllAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(e);
        }
        return list;
    }

    public Task<Employee?> GetByKeyAsync(int? employeeID, CancellationToken cancellationToken = default)
        => _store.SelectByKeyAsync(employeeID, cancellationToken);

    public Task<Employee?> CreateAsync(Employee employee, CancellationToken cancellationToken = default)
        => _store.InsertReturningAsync(employee, cancellationToken);

    public Task<bool> UpdateAsync(Employee employee, CancellationToken cancellationToken = default)
        => _store.UpdateAsync(employee, cancellationToken);

    public Task<bool> DeleteAsync(int? employeeID, CancellationToken cancellationToken = default)
        => _store.DeleteByKeyAsync(employeeID, cancellationToken);
}
