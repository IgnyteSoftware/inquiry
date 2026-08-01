using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// Operations against the two composite-key bridge tables — <c>CustomerCustomerDemo</c>
/// (Customer ↔ CustomerDemographic) and the parent <c>CustomerDemographics</c> type table.
/// Exercises Inquiry's string + string composite key support.
/// </summary>
public sealed class DemographicsService
{
    private readonly CustomerDemographicStore _demographics;
    private readonly CustomerCustomerDemoStore _bridge;

    public DemographicsService(
        CustomerDemographicStore demographics,
        CustomerCustomerDemoStore bridge)
    {
        _demographics = demographics;
        _bridge = bridge;
    }

    public async Task<List<CustomerDemographic>> GetDemographicsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<CustomerDemographic>();
        await foreach (var d in _demographics.SelectAllAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(d);
        }
        return list;
    }

    public async Task<List<CustomerCustomerDemo>> GetAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<CustomerCustomerDemo>();
        await foreach (var a in _bridge.SelectAllAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(a);
        }
        return list;
    }

    public Task<int> CreateDemographicAsync(CustomerDemographic demographic, CancellationToken cancellationToken = default)
        => _demographics.InsertAsync(demographic, cancellationToken);

    public Task<bool> UpdateDemographicAsync(CustomerDemographic demographic, CancellationToken cancellationToken = default)
        => _demographics.UpdateAsync(demographic, cancellationToken);

    public Task<bool> DeleteDemographicAsync(string customerTypeID, CancellationToken cancellationToken = default)
        => _demographics.DeleteByKeyAsync(customerTypeID, cancellationToken);

    public Task<int> AssignAsync(string customerID, string customerTypeID, CancellationToken cancellationToken = default)
        => _bridge.InsertAsync(new CustomerCustomerDemo { CustomerID = customerID, CustomerTypeID = customerTypeID }, cancellationToken);

    public Task<bool> UnassignAsync(string customerID, string customerTypeID, CancellationToken cancellationToken = default)
        => _bridge.DeleteByKeyAsync(customerID, customerTypeID, cancellationToken);
}
