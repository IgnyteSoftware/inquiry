using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class CustomerStore : InquiryStore<Customer>
{

    [InquirySelectAll]
    public partial Task<IReadOnlyList<Customer>> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Customer?> SelectByKeyAsync(string customerID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("Country")]
    public partial Task<IReadOnlyList<Customer>> SelectByCountryAsync(string? country, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<Customer?> InsertReturningAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<Customer?> UpdateReturningAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public partial Task<int> UpsertAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public partial Task<Customer?> UpsertReturningAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteByKeyAsync(string customerID, CancellationToken cancellationToken = default);
}
