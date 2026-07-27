using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable replacement for the old <c>RelationModel</c>. References the child entity by its
/// fully-qualified name; the child's columns/key are resolved from the entity set at emit time.
/// </summary>
internal sealed record RelationData(
    string PropertyName,
    string ForeignKeyProperty,
    string ChildEntityFullyQualifiedName,
    bool IsCollection,
    LocationData? Location = null)
{
    /// <summary>
    /// True for an <c>[InquiryManyToMany]</c> association resolved through a junction table. When set,
    /// <see cref="ForeignKeyProperty"/> is unused (the foreign keys live on the junction, named by
    /// <see cref="JunctionParentForeignKeyProperty"/> / <see cref="JunctionChildForeignKeyProperties"/>),
    /// <see cref="IsCollection"/> is always true, and <see cref="JunctionEntityFullyQualifiedName"/>
    /// references the mapped junction entity.
    /// </summary>
    public bool IsManyToMany { get; init; }

    /// <summary>The mapped junction (link) entity's fully-qualified name, for a many-to-many relation.</summary>
    public string? JunctionEntityFullyQualifiedName { get; init; }

    /// <summary>The junction property referencing this entity's key (many-to-many).</summary>
    public string? JunctionParentForeignKeyProperty { get; init; }

    /// <summary>
    /// The junction properties referencing the related entity's key (many-to-many), in key order — one
    /// per key column of the related entity. <see cref="EquatableArray{T}"/> rather than a plain array
    /// because this model flows through the incremental pipeline and needs sequence equality for caching.
    /// Empty when the attribute was malformed; arity other than 1 is rejected by validation today.
    /// </summary>
    public EquatableArray<string> JunctionChildForeignKeyProperties { get; init; }
}
