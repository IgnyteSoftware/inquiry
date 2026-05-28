using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public abstract partial class CustomerStore : InquiryStore<Customer>
{
    protected CustomerStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract Task<IReadOnlyList<Customer>> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<Customer?> SelectByKeyAsync(string customerID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("Country")]
    public abstract Task<IReadOnlyList<Customer>> SelectByCountryAsync(string? country, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public abstract Task<Customer?> InsertReturningAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryUpdate(ReturnEntity = true)]
    public abstract Task<Customer?> UpdateReturningAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public abstract Task<int> UpsertAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public abstract Task<Customer?> UpsertReturningAsync(Customer customer, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(string customerID, CancellationToken cancellationToken = default);
}
