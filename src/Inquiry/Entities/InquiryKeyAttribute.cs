namespace Inquiry.Entities;

/// <summary>
/// Marks the single primary key property for an Inquiry entity.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryKeyAttribute : InquiryColumnAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryKeyAttribute"/> class.
    /// </summary>
    public InquiryKeyAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryKeyAttribute"/> class.
    /// </summary>
    public InquiryKeyAttribute(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether the database generates the key
    /// (for example, IDENTITY or AUTOINCREMENT). Generated keys are excluded
    /// from INSERT statements.
    /// </summary>
    public bool IsGenerated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether insert/upsert methods assign a sequential,
    /// time-ordered GUID when the key is unset (<see cref="Guid.Empty"/> or <see langword="null"/>).
    /// The layout is dialect-aware: UUIDv7 on most providers, a SQL Server-optimized layout via
    /// <see cref="InquiryGuid.NewSqlServerSequential"/> for <c>uniqueidentifier</c>. The assignment
    /// mutates the entity, so the caller observes the generated key after the call; an explicitly
    /// supplied key is never overwritten. Only valid on a <see cref="Guid"/> (or nullable Guid)
    /// key that is not <see cref="IsGenerated"/> and has no database default.
    /// </summary>
    public bool SequentialGuid { get; set; }
}
