using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class EmployeeTerritoryStore : InquiryStore<EmployeeTerritory>
{

    [InquirySelectAll]
    public partial IAsyncEnumerable<EmployeeTerritory> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<EmployeeTerritory?> SelectByKeyAsync(int employeeID, string territoryID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("EmployeeID")]
    public partial IAsyncEnumerable<EmployeeTerritory> SelectByEmployeeAsync(int employeeID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(EmployeeTerritory employeeTerritory, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteByKeyAsync(int employeeID, string territoryID, CancellationToken cancellationToken = default);
}
