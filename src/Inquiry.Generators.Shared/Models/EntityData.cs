using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable replacement for the old <c>EntityModel</c>. Holds all entity facts the emitters
/// need as strings/primitives (no <see cref="Microsoft.CodeAnalysis.INamedTypeSymbol"/>), plus the
/// pre-computed materializer names and any discovery diagnostics. <see cref="IsMapped"/> is false
/// when the entity is unusable as a store target (no key, or a composite key containing a generated
/// column) — those entities are reported but excluded from the store-linking set, matching the old
/// "report and skip" behavior.
/// </summary>
internal sealed record EntityData(
    string FullyQualifiedName,
    string Name,
    string? Namespace,
    string TableName,
    string? Schema,
    EquatableArray<ColumnData> Columns,
    EquatableArray<ColumnData> Keys,
    EquatableArray<RelationData> Relations,
    string ClassMaterializerName,
    string StructMaterializerName,
    string ClassMaterializerFullName,
    string StructMaterializerFullName,
    bool IsMapped,
    EquatableArray<DiagnosticData> Diagnostics)
{
    /// <summary>
    /// The entity's single <c>[InquirySoftDelete]</c> column, or null when none is declared. Cached
    /// here so the store emitter can decide delete→update routing and SELECT filtering without rescanning.
    /// </summary>
    public ColumnData? SoftDeleteColumn { get; init; }

    /// <summary>
    /// The entity's single <c>[InquiryConcurrencyToken]</c> column, or null when none is declared.
    /// Cached so the store emitter can emit the conflict-throw branch only for token entities.
    /// </summary>
    public ColumnData? ConcurrencyToken { get; init; }

    /// <summary>
    /// Whether generated <c>CREATE TABLE</c> DDL emits <c>FOREIGN KEY</c> constraints for this
    /// entity's foreign-key columns (<see cref="InquiryTableAttribute.GenerateForeignKeys"/>). Default true.
    /// </summary>
    public bool GenerateForeignKeys { get; init; } = true;

    /// <summary>
    /// Whether this entity maps a database <c>[InquiryView]</c> rather than a table: it is read-only
    /// (a store over it may only declare SELECT/aggregate operations — mutations are rejected),
    /// keyless-permitted (no <c>[InquiryKey]</c> required), and skipped by the schema emitter (the
    /// view is defined in the database, not created by Inquiry).
    /// </summary>
    public bool IsView { get; init; }
}
