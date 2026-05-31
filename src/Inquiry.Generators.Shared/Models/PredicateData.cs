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
    bool IsOr);
