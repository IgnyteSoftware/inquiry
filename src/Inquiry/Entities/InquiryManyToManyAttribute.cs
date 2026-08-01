namespace Inquiry.Entities;

/// <summary>
/// Marks a collection navigation property as a many-to-many association resolved through a junction
/// (link) table — the EF <c>HasMany().WithMany()</c> / explicit join-entity analog. Either name a mapped
/// junction entity and its foreign-key properties with the three-argument constructor, or use the
/// parameterless form and let Inquiry synthesize the junction table.
/// </summary>
/// <remarks>
/// The property is not mapped to a column; it is populated by eager-loading store methods
/// (<c>[InquirySelectOneByKeyEager]</c> / <c>[InquirySelectAllEager]</c>), which read the related rows by
/// joining through the junction. Writing associations means inserting or deleting a junction row, which
/// needs an explicitly mapped junction entity and its own store — an auto-managed junction is read-only.
/// With an explicit junction the related entity may have a single-column or a composite key; name one
/// junction foreign-key property per key column.
/// <para>
/// Apply this to a collection property (<c>List&lt;T&gt;</c> / <c>IReadOnlyList&lt;T&gt;</c> / …) — a
/// non-collection property is rejected (INQ063). An unmapped junction or related type is INQ087, a
/// junction property that is not a mapped column is INQ088, foreign keys that do not pair with the
/// related entity's key are INQ089, and an auto-managed junction that cannot be synthesized is INQ090.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryManyToManyAttribute : Attribute
{
    /// <summary>
    /// Initializes an <strong>auto-managed</strong> many-to-many association: Inquiry synthesizes the
    /// junction table itself, so no junction entity has to be written.
    /// </summary>
    /// <remarks>
    /// The table and its two foreign-key columns are named from both mapped tables and their key
    /// columns, in an order-independent (ordinally sorted) order, so declaring the association from
    /// either side — or from both — describes the same table. Override any of them with
    /// <see cref="JunctionTable"/>, <see cref="JunctionSchema"/>, <see cref="ParentColumn"/>, and
    /// <see cref="ChildColumn"/>. <see cref="JunctionTable"/> and <see cref="JunctionSchema"/> name the
    /// same object from either side, so state them identically; <see cref="ParentColumn"/> and
    /// <see cref="ChildColumn"/> are relative to the declaring side, so the reverse navigation states the
    /// two swapped. Each side may declare one navigation — two separate associations between the same
    /// pair of entities need an explicitly mapped junction.
    /// <para>
    /// Auto-managed junctions are <strong>read-only</strong>: Inquiry emits their DDL and eager-loads
    /// through them, but writing an association still needs a junction row, which only an explicitly
    /// mapped junction entity can insert or delete. Use the three-argument constructor when you need to
    /// write links, or issue raw SQL against the generated table.
    /// </para>
    /// </remarks>
    public InquiryManyToManyAttribute()
    {
        ChildForeignKeyProperties = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a many-to-many association through an explicitly mapped junction entity.
    /// </summary>
    /// <param name="junctionEntity">The mapped junction (link) entity type.</param>
    /// <param name="parentForeignKeyProperty">The junction property holding the foreign key to this entity's key.</param>
    /// <param name="childForeignKeyProperties">
    /// The junction properties holding the foreign key to the related entity's key — one per key column,
    /// <strong>in the related entity's key-declaration order</strong>. A single name is the common case;
    /// a composite-key related entity names each of its key columns.
    /// <para>
    /// The pairing is positional, and both the generated SQL and the in-memory grouping follow it, so a
    /// transposed list is a silently wrong join rather than a compile error. Naming a property whose type
    /// does not match the key column opposite it is rejected (INQ089), which catches most transpositions —
    /// but two key columns of the same type are indistinguishable, so order still matters.
    /// </para>
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

    /// <summary>
    /// Gets the mapped junction (link) entity type, or <see langword="null"/> for an auto-managed
    /// junction that Inquiry synthesizes.
    /// </summary>
    public Type? JunctionEntity { get; }

    /// <summary>
    /// Gets the junction property that references this entity's key, or <see langword="null"/> for an
    /// auto-managed junction (whose column is named by convention or by <see cref="ParentColumn"/>).
    /// </summary>
    public string? ParentForeignKeyProperty { get; }

    /// <summary>
    /// Gets the junction properties that reference the related entity's key, in key order. Exactly one
    /// name is the common case; see INQ089. Empty for an auto-managed junction.
    /// </summary>
    public IReadOnlyList<string> ChildForeignKeyProperties { get; }

    /// <summary>
    /// Auto-managed junctions only: the synthesized table's name, overriding the derived default. Names
    /// the same object from either side, so state it identically on both.
    /// </summary>
    public string? JunctionTable { get; set; }

    /// <summary>
    /// Auto-managed junctions only: the synthesized table's schema, overriding the default (which is the
    /// schema shared by the two mapped tables). Names the same object from either side, so state it
    /// identically on both.
    /// </summary>
    public string? JunctionSchema { get; set; }

    /// <summary>
    /// Auto-managed junctions only: the synthesized column referencing <em>this</em> entity's key,
    /// overriding the derived default. Relative to the declaring side: the reverse navigation states this
    /// same column as its <see cref="ChildColumn"/>.
    /// </summary>
    public string? ParentColumn { get; set; }

    /// <summary>
    /// Auto-managed junctions only: the synthesized column referencing the related entity's key,
    /// overriding the derived default. Relative to the declaring side: the reverse navigation states this
    /// same column as its <see cref="ParentColumn"/>.
    /// </summary>
    public string? ChildColumn { get; set; }
}
