using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests.Fixtures;

public abstract partial class OrganizationStore : InquiryStore<Organization>
{
    protected OrganizationStore(IInquiry inquiry)
        : base(inquiry)
    {
    }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("IsActive")]
    public abstract IAsyncEnumerable<Organization> SelectByIsActiveAsync(bool isActive, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Organization organization, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    public IAsyncEnumerable<Organization> SelectWithInquiryAsync(CancellationToken cancellationToken = default)
    {
        return Inquiry.QueryAsync<Organization>("SELECT [Key], [Name], [IsActive] FROM [TOrganization]", cancellationToken);
    }
}
