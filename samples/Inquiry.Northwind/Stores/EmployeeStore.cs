using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class EmployeeStore : InquiryStore<Employee>
{

    [InquirySelectAll]
    public partial IAsyncEnumerable<Employee> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Employee?> SelectByKeyAsync(int? employeeID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Employee employee, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public partial Task<Employee?> InsertReturningAsync(Employee employee, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Employee employee, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public partial Task<bool> DeleteByKeyAsync(int? employeeID, CancellationToken cancellationToken = default);
}
