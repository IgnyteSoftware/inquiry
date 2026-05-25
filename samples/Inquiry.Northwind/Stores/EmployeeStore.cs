using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public abstract partial class EmployeeStore : InquiryStore<Employee>
{
    protected EmployeeStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<Employee> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<Employee?> SelectByKeyAsync(int? employeeID, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Employee employee, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public abstract Task<Employee?> InsertReturningAsync(Employee employee, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Employee employee, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(int? employeeID, CancellationToken cancellationToken = default);
}
