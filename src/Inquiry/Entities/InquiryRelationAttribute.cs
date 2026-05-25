namespace Inquiry.Entities;

/// <summary>
/// Marks a navigation property as a related-entity collection or reference.
/// The property is NOT mapped to a database column; it is populated by eager-loading store methods.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InquiryRelationAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="InquiryRelationAttribute"/>.
    /// </summary>
    /// <param name="foreignKeyProperty">
    /// The property name on the child entity that holds the foreign key reference back to this entity's key.
    /// </param>
    public InquiryRelationAttribute(string foreignKeyProperty)
    {
        if (string.IsNullOrWhiteSpace(foreignKeyProperty))
        {
            throw new ArgumentException("Foreign key property name cannot be empty.", nameof(foreignKeyProperty));
        }

        ForeignKeyProperty = foreignKeyProperty;
    }

    /// <summary>
    /// Gets the property name on the child entity that references this entity's key.
    /// </summary>
    public string ForeignKeyProperty { get; }
}
