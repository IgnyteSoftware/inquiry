namespace Inquiry.Entities;

/// <summary>
/// Marks a collection navigation property as a many-to-many association resolved through a junction
/// (link) table — the EF <c>HasMany().WithMany()</c> / explicit join-entity analog. The junction must
/// itself be a mapped Inquiry entity (<c>[InquiryTable]</c>); this attribute names the junction's two
/// foreign-key properties: the one referencing <em>this</em> entity's key and the one referencing the
/// related (child) entity's key.
/// </summary>
/// <remarks>
/// The property is not mapped to a column; it is populated by eager-loading store methods
/// (<c>[InquirySelectOneByKeyEager]</c> / <c>[InquirySelectAllEager]</c>), which read the related rows by
/// joining through the junction. Writing associations is done through the junction entity's own store
/// (insert/delete a junction row). The related entity must have a single-column key (INQ063). Apply this
/// to a collection property (<c>List&lt;T&gt;</c> / <c>IReadOnlyList&lt;T&gt;</c> / …) — a non-collection
/// property is rejected (INQ063).
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryManyToManyAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryManyToManyAttribute"/> class.
    /// </summary>
    /// <param name="junctionEntity">The mapped junction (link) entity type.</param>
    /// <param name="parentForeignKeyProperty">The junction property holding the foreign key to this entity's key.</param>
    /// <param name="childForeignKeyProperties">
    /// The junction properties holding the foreign key to the related entity's key — one per key column,
    /// in key order. A single name is the common case and the only arity accepted today; the parameter is
    /// <see langword="params"/> so a composite-key related entity can name each column without a
    /// source-breaking signature change.
    /// </param>
    public InquiryManyToManyAttribute(Type junctionEntity, string parentForeignKeyProperty, params string[] childForeignKeyProperties)
    {
        JunctionEntity = junctionEntity ?? throw new ArgumentNullException(nameof(junctionEntity));
        if (string.IsNullOrWhiteSpace(parentForeignKeyProperty))
        {
            throw new ArgumentException("Parent foreign-key property name cannot be empty.", nameof(parentForeignKeyProperty));
        }

        if (childForeignKeyProperties is null || childForeignKeyProperties.Length == 0)
        {
            throw new ArgumentException("At least one child foreign-key property name is required.", nameof(childForeignKeyProperties));
        }

        foreach (var name in childForeignKeyProperties)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Child foreign-key property names cannot be empty.", nameof(childForeignKeyProperties));
            }
        }

        ParentForeignKeyProperty = parentForeignKeyProperty;
        ChildForeignKeyProperties = childForeignKeyProperties;
    }

    /// <summary>Gets the mapped junction (link) entity type.</summary>
    public Type JunctionEntity { get; }

    /// <summary>Gets the junction property that references this entity's key.</summary>
    public string ParentForeignKeyProperty { get; }

    /// <summary>
    /// Gets the junction properties that reference the related entity's key, in key order. Exactly one
    /// name is accepted today; see INQ063.
    /// </summary>
    public IReadOnlyList<string> ChildForeignKeyProperties { get; }
}
