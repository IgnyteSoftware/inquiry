namespace Inquiry.Stores;

/// <summary>
/// Generates a method that selects at most one row by a single mapped property or column. The method
/// returns <c>Task&lt;TEntity?&gt;</c>, or <c>Task&lt;TProjection?&gt;</c> for an
/// <c>[InquiryProjection]</c> of the entity, and takes one parameter of the field's type ahead of
/// the cancellation token.
/// </summary>
/// <remarks>
/// The cardinality rule matches <c>QuerySingleOrDefaultAsync</c>: no matching row returns
/// <see langword="null"/>, and more than one matching row throws
/// <see cref="System.InvalidOperationException"/>. The generated query reads at most two rows, so the
/// check does not read every match. To filter on several fields, use
/// <see cref="InquirySelectAllByPredicateAttribute"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectOneByFieldAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquirySelectOneByFieldAttribute"/> class.
    /// </summary>
    /// <param name="field">The mapped property or column name used in the generated WHERE clause.</param>
    public InquirySelectOneByFieldAttribute(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("The field name cannot be empty.", nameof(field));
        }

        Field = field;
    }

    /// <summary>
    /// Gets the mapped property or column name used in the generated WHERE clause.
    /// </summary>
    public string Field { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the generated query includes soft-deleted rows. Has an
    /// effect only when the entity declares an <c>[InquirySoftDelete]</c> column: when false (the
    /// default) the query auto-appends the active filter (<c>= 0</c> / <c>IS NULL</c>); when true the
    /// query is emitted unfiltered so a soft-deleted row can be returned.
    /// </summary>
    public bool IncludeDeleted { get; set; }

    /// <summary>
    /// Gets or sets the row-level lock mode for the generated query. The default is
    /// <see cref="InquiryLockMode.None"/> (no locking). Use within a transaction to acquire
    /// pessimistic locks on the selected rows.
    /// </summary>
    public InquiryLockMode LockMode { get; set; }

    /// <summary>
    /// Gets or sets the mapped property or column names the generated query leaves out of its SELECT
    /// list, for example <c>Exclude = new[] { nameof(Announcement.Image) }</c>. The omitted properties
    /// keep the value the entity constructor or property initializer gives them. A key, concurrency
    /// token, soft-delete or global-filter column, foreign key a relation joins on, or
    /// <c>required</c> property cannot be excluded (INQ122), and neither can a projection result. Do not
    /// write an entity read this way back with a full <c>[InquiryUpdate]</c>: that update would
    /// overwrite the omitted columns.
    /// </summary>
    public string[]? Exclude { get; set; }
}
