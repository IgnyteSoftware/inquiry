using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable model for an <c>[InquiryProjection(typeof(Entity))]</c> result type — a flat,
/// read-only subset of an entity's columns. Mirrors the materializer-relevant parts of
/// <see cref="EntityData"/> (no key/relations/mutations). The store emitter builds a SELECT over these
/// columns against the parent entity's table; the materializer reads them by SELECT-list ordinal.
/// </summary>
internal sealed record ProjectionData(
    string FullyQualifiedName,
    string Name,
    string? Namespace,
    string EntityFullyQualifiedName,
    EquatableArray<ColumnData> Columns,
    string ClassMaterializerName,
    string StructMaterializerName,
    string ClassMaterializerFullName,
    string StructMaterializerFullName,
    bool IsMapped,
    EquatableArray<DiagnosticData> Diagnostics);
