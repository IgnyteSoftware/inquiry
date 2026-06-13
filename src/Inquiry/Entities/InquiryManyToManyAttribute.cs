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
    /// <param name="childForeignKeyProperty">The junction property holding the foreign key to the related entity's key.</param>
    public InquiryManyToManyAttribute(Type junctionEntity, string parentForeignKeyProperty, string childForeignKeyProperty)
    {
        JunctionEntity = junctionEntity ?? throw new ArgumentNullException(nameof(junctionEntity));
        if (string.IsNullOrWhiteSpace(parentForeignKeyProperty))
        {
            throw new ArgumentException("Parent foreign-key property name cannot be empty.", nameof(parentForeignKeyProperty));
        }

        if (string.IsNullOrWhiteSpace(childForeignKeyProperty))
        {
            throw new ArgumentException("Child foreign-key property name cannot be empty.", nameof(childForeignKeyProperty));
        }

        ParentForeignKeyProperty = parentForeignKeyProperty;
        ChildForeignKeyProperty = childForeignKeyProperty;
    }

    /// <summary>Gets the mapped junction (link) entity type.</summary>
    public Type JunctionEntity { get; }

    /// <summary>Gets the junction property that references this entity's key.</summary>
    public string ParentForeignKeyProperty { get; }

    /// <summary>Gets the junction property that references the related entity's key.</summary>
    public string ChildForeignKeyProperty { get; }
}
