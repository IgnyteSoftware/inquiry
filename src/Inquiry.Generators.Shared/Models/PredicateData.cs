using Inquiry.Generators.Abstractions;

namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable description of one <c>[InquiryWhere]</c> criterion captured in source order during
/// discovery. <see cref="Field"/> is the raw attribute argument (property or column name); it is
/// resolved against the entity's columns in the combined emit stage, mirroring how
/// <c>SelectAllByField</c> field names are resolved. <see cref="Op"/> reuses the analyzer-facing
/// <see cref="SqlCompareOp"/> so no extra enum has to round-trip through the cached model.
/// </summary>
internal sealed record PredicateData(
    string Field,
    SqlCompareOp Op,
    bool IsOr)
{
    /// <summary>
    /// JSON path (<c>$.a.b</c>) when this criterion filters inside a JSON column ([InquiryWhere.JsonPath]),
    /// or null for an ordinary column comparison. When set, <see cref="Field"/> names the JSON text column
    /// and the WHERE renders the dialect's JSON extraction of this path instead of the bare column.
    /// </summary>
    public string? JsonPath { get; init; }
}
