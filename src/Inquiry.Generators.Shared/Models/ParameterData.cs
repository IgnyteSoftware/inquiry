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

    /// <summary>The collection element type with nullable wrappers/annotations removed.</summary>
    public string? ElementNonNullableComparisonDisplay { get; init; }

    /// <summary>Whether the collection element is a nullable value or annotated reference type.</summary>
    public bool ElementIsNullable { get; init; }

    /// <summary>
    /// Source rendering of the parameter's explicit default value (e.g. <c>default</c>, <c>null</c>,
    /// <c>true</c>, a quoted string, or a fully-qualified enum cast), or null when the parameter has
    /// none. Default values live on the user's partial declaration, so the generated implementation
    /// half must not repeat them — this is consumed only by the generated <c>I{StoreName}</c>
    /// interface signatures, which carry the defaults so optional arguments survive interface calls.
    /// </summary>
    public string? DefaultValueLiteral { get; init; }

    /// <summary>
    /// Fully-qualified <c>System.Data.DbType</c> expression inferred from the CLR type (e.g.
    /// <c>"global::System.Data.DbType.Int32"</c>), or null when the type has no portable mapping.
    /// Populated for stored-procedure input parameters so the generator emits <c>DbType</c>.
    /// </summary>
    public string? DbTypeExpression { get; init; }

    /// <summary>Whether the parameter's CLR type is a string type (for Size emission).</summary>
    public bool IsStringType { get; init; }

    /// <summary>Whether the parameter's CLR type is a decimal type (for Precision/Scale emission).</summary>
    public bool IsDecimalType { get; init; }

    /// <summary>Whether the parameter's CLR type is a binary type (byte[], for Size emission).</summary>
    public bool IsBinaryType { get; init; }

    /// <summary>Whether this parameter is an input/output parameter (<c>[InquiryParameter(IsInputOutput = true)]</c>).</summary>
    public bool IsInputOutput { get; init; }

    /// <summary>Declared length from <c>[InquiryParameter(Length = …)]</c>, or 0.</summary>
    public int DeclaredLength { get; init; }

    /// <summary>Whether the parameter is Unicode from <c>[InquiryParameter(IsUnicode = …)]</c>. Default true.</summary>
    public bool DeclaredIsUnicode { get; init; } = true;

    /// <summary>Declared precision from <c>[InquiryParameter(Precision = …)]</c>, or 0.</summary>
    public int DeclaredPrecision { get; init; }

    /// <summary>Declared scale from <c>[InquiryParameter(Scale = …)]</c>, or 0.</summary>
    public int DeclaredScale { get; init; }

    /// <summary>
    /// Precomputed value expression for stored-procedure parameter binding. Includes casts for
    /// enums (to underlying integer) and unsigned types (unchecked reinterpret to signed partner),
    /// matching the column-binder convention. Null when no special casting is needed (the emitter
    /// falls back to the default <c>(object?)name ?? DBNull.Value</c> pattern).
    /// </summary>
    public string? ProcedureValueExpression { get; init; }

    /// <summary>
    /// Schema-qualified SQL Server TVP type name from <c>[InquiryParameter(TvpTypeName = …)]</c>,
    /// required for collection parameters on stored-procedure methods. Null for non-TVP parameters.
    /// </summary>
    public string? TvpTypeName { get; init; }

    /// <summary>
    /// The <see cref="Inquiry.Generators.Abstractions.DbTypeClass"/> of the collection element type,
    /// or null when this parameter is not a collection. Populated for stored-procedure TVP resolution.
    /// </summary>
    public Inquiry.Generators.Abstractions.DbTypeClass? ElementDbTypeClass { get; init; }
}
