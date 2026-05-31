namespace Inquiry.Generators.Models;

/// <summary>The pagination mode requested on a select method.</summary>
internal enum Pagination
{
    None,
    Offset,
    Keyset,
}

/// <summary>
/// One parsed ORDER BY term. <see cref="Field"/> is the raw property or column name from the attribute
/// argument (resolved against the entity's columns and quoted in the emit stage, mirroring how
/// <c>SelectAllByField</c> field names are resolved). <see cref="Descending"/> is the parsed direction.
/// </summary>
internal sealed record OrderItem(
    string Field,
    bool Descending);
