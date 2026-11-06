using System.Text.Json.Serialization;
using Inquiry.Entities;
using Inquiry.Stores;

[assembly: InquiryJsonContext(typeof(Inquiry.AotSmoke.SmokeJsonContext))]

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

    [InquirySelectAllByPredicate]
    [InquiryWhere("Name", Compare.In)]
    public partial Task<IReadOnlyList<Widget>> SelectByNamesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Widget widget, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Widget widget, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);
}

public sealed class WidgetSpec
{
    public string Color { get; set; } = string.Empty;

    public List<int> Sizes { get; set; } = [];
}

// The assembly default context serializes Spec with camel-case names. Tags uses the per-property override.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WidgetSpec))]
internal sealed partial class SmokeJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SmokeTagsJsonContext : JsonSerializerContext;

[InquiryTable("TGadget")]
public sealed class Gadget
{
    [InquiryKey]
    public Guid Key { get; set; } = Guid.NewGuid();

    [InquiryColumn]
    [InquiryJson]
    public WidgetSpec? Spec { get; set; }

    [InquiryColumn]
    [InquiryJson(Context = typeof(SmokeTagsJsonContext))]
    public Dictionary<string, string> Tags { get; set; } = [];
}

public partial class GadgetStore : InquiryStore<Gadget>
{
    [InquirySelectOneByKey]
    public partial Task<Gadget?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Gadget gadget, CancellationToken cancellationToken = default);
}
