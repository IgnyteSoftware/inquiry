using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Oracle.Tests;

public sealed class OracleUnsupportedFixtureMarker;

[InquiryTable("TDefaultedKeyItem")]
public sealed class DefaultedKeyItem
{
    [InquiryKey(UseDatabaseDefault = true, Length = 255)]
    public string? Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}

public partial class DefaultedKeyItemStore : InquiryStore<DefaultedKeyItem>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<DefaultedKeyItem?> InsertReturningAsync(
        DefaultedKeyItem item,
        CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<DefaultedKeyItem?> UpsertReturningAsync(
        DefaultedKeyItem item,
        CancellationToken cancellationToken = default);
}

[InquiryTable("OraGeneratedKeyOnlyItem")]
public sealed class OraGeneratedKeyOnlyItem
{
    [InquiryKey(IsGenerated = true)]
    public int? Id { get; set; }
}

public partial class OraGeneratedKeyOnlyItemStore : InquiryStore<OraGeneratedKeyOnlyItem>
{
    [InquiryUpsert]
    public partial Task<int> UpsertAsync(
        OraGeneratedKeyOnlyItem item,
        CancellationToken cancellationToken = default);
}

[InquiryTable("OraDefaultKeyOnlyItem")]
public sealed class OraDefaultKeyOnlyItem
{
    [InquiryKey(UseDatabaseDefault = true, Length = 32)]
    public string? Id { get; set; }
}

public partial class OraDefaultKeyOnlyItemStore : InquiryStore<OraDefaultKeyOnlyItem>
{
    [InquiryUpsert]
    public partial Task<int> UpsertAsync(
        OraDefaultKeyOnlyItem item,
        CancellationToken cancellationToken = default);
}
