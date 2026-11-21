namespace Inquiry.Stores;

/// <summary>
/// Generates a method that selects a single row by primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectOneByKeyAttribute : Attribute
{
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
    /// token, soft-delete or global-filter column, foreign key a relation joins on, ORDER BY column, or
    /// <c>required</c> property cannot be excluded (INQ122). Do not write an entity read this way back
    /// with a full <c>[InquiryUpdate]</c>: that update would overwrite the omitted columns.
    /// </summary>
    public string[]? Exclude { get; set; }
}
