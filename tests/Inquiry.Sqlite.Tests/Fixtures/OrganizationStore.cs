namespace Inquiry.Sqlite.Tests;

public abstract partial class OrganizationStore : InquiryStore<Organization>
{
    protected OrganizationStore(IInquiry inquiry)
        : base(inquiry)
    {
    }

    [InquirySelect]
    public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectByKey]
    public abstract Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    [InquirySelectByField("IsActive")]
    public abstract IAsyncEnumerable<Organization> SelectByIsActiveAsync(bool isActive, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Organization organization, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);

    [InquiryDeleteByKey]
    public abstract Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    public IAsyncEnumerable<Organization> SelectWithInquiryAsync(CancellationToken cancellationToken = default)
    {
        return _inquiry.QueryAsync<Organization>("SELECT [Key], [Name], [IsActive] FROM [TOrganization]", cancellationToken);
    }
}
