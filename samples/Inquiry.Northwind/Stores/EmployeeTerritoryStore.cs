using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public abstract partial class EmployeeTerritoryStore : InquiryStore<EmployeeTerritory>
{
    protected EmployeeTerritoryStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<EmployeeTerritory> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<EmployeeTerritory?> SelectByKeyAsync(int employeeID, string territoryID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("EmployeeID")]
    public abstract IAsyncEnumerable<EmployeeTerritory> SelectByEmployeeAsync(int employeeID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(EmployeeTerritory employeeTerritory, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(int employeeID, string territoryID, CancellationToken cancellationToken = default);
}
