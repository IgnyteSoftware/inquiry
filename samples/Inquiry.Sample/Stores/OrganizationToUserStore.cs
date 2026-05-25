using Inquiry.Sample.Models;
using Inquiry.Stores;

namespace Inquiry.Sample.Stores;

public abstract partial class OrganizationToUserStore : InquiryStore<OrganizationToUser>
{
    protected OrganizationToUserStore(IInquiry inquiry)
        : base(inquiry)
    {
    }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<OrganizationToUser> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<OrganizationToUser?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>Returns membership rows for a given organization.</summary>
    [InquirySelectAllByField("TOrganizationKey")]
    public abstract IAsyncEnumerable<OrganizationToUser> SelectByOrganizationAsync(Guid organizationKey, CancellationToken cancellationToken = default);

    /// <summary>Returns membership rows for a given user.</summary>
    [InquirySelectAllByField("TUserKey")]
    public abstract IAsyncEnumerable<OrganizationToUser> SelectByUserAsync(Guid userKey, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(OrganizationToUser membership, CancellationToken cancellationToken = default);

    [InquiryBulkInsert]
    public abstract Task<int> BulkInsertAsync(IEnumerable<OrganizationToUser> memberships, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(OrganizationToUser membership, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);
}
