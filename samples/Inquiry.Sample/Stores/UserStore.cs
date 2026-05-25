using Inquiry.Sample.Models;
using Inquiry.Stores;

namespace Inquiry.Sample.Stores;

public abstract partial class UserStore : InquiryStore<User>
{
    protected UserStore(IInquiry inquiry)
        : base(inquiry)
    {
    }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<User> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public abstract Task<User?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("Email")]
    public abstract IAsyncEnumerable<User> SelectByEmailAsync(string email, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(User user, CancellationToken cancellationToken = default);

    [InquiryBulkInsert]
    public abstract Task<int> BulkInsertAsync(IEnumerable<User> users, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(User user, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public abstract Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);
}
