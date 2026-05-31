namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable description of a store-method parameter. <see cref="TypeDisplay"/> is the
/// fully-qualified, nullable-annotated type (used for parameter declarations and generic argument
/// lists). <see cref="ComparisonDisplay"/> is the <c>FullyQualifiedFormat</c> rendering (no nullable
/// reference annotation), which equals what <c>SymbolEqualityComparer.Default</c> would compare for
/// the positional key/field parameter-type validation.
/// </summary>
internal sealed record ParameterData(
    string Name,
    string TypeDisplay,
    string ComparisonDisplay,
    bool IsCancellationToken)
{
    /// <summary>
    /// When the parameter is an <c>IEnumerable&lt;T&gt;</c> (used by <c>Compare.In</c>), the
    /// <c>FullyQualifiedFormat</c> of its element type <c>T</c>; otherwise null. Lets the predicate
    /// validator confirm an IN collection's element type matches the filtered column without a symbol.
    /// </summary>
    public string? ElementComparisonDisplay { get; init; }
}
