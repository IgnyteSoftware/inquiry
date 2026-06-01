using System;

namespace Inquiry.Entities;

/// <summary>
/// Marks a read-only result type that projects a subset of an entity's columns (a DTO). Apply to a
/// class or record whose <c>[InquiryColumn]</c> properties name the columns to select; a store method
/// returning <c>Task&lt;IReadOnlyList&lt;TProjection&gt;&gt;</c> or
/// <c>IAsyncEnumerable&lt;TProjection&gt;</c> then selects only those columns from the entity's table.
/// </summary>
/// <remarks>
/// A projection has no key, relations, or mutations — it is a flat, read-only shape. Its materializer
/// reads each column by its position in the projected SELECT list, so the declared property order
/// defines the SELECT order. v1 supports projections on entities without a soft-delete column.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InquiryProjectionAttribute : Attribute
{
    /// <summary>Initializes the projection, binding it to the entity type it projects.</summary>
    /// <param name="entityType">The <c>[InquiryTable]</c> entity this is a projection of.</param>
    public InquiryProjectionAttribute(Type entityType)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
    }

    /// <summary>The entity type this projection selects from.</summary>
    public Type EntityType { get; }
}
