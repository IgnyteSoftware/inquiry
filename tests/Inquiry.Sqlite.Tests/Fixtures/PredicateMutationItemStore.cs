using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests.Fixtures;

public partial class PredicateMutationItemStore : InquiryStore<PredicateMutationItem>
{

    [InquiryInsert]
    public partial Task<int> InsertAsync(PredicateMutationItem item, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<PredicateMutationItem>> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquiryWhere("Category")]
    public partial Task<int> RepriceCategoryAsync(decimal price, string category, CancellationToken cancellationToken = default);

    [InquiryDelete]
    [InquiryWhere("Category")]
    public partial Task<int> DeleteCategoryAsync(string category, CancellationToken cancellationToken = default);
}
