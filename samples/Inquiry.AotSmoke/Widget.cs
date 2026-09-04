using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.AotSmoke;

[InquiryTable("TWidget")]
public sealed class Widget
{
    [InquiryKey]
    public Guid Key { get; set; } = Guid.NewGuid();

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn]
    public bool IsActive { get; set; }
}

public partial class WidgetStore : InquiryStore<Widget>
{
    [InquirySelectAll]
    public partial Task<IReadOnlyList<Widget>> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Widget?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Widget widget, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Widget widget, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);
}
