using System;

namespace Inquiry.Entities;

/// <summary>
/// Marks a <see cref="DateTime"/> or <see cref="DateTimeOffset"/> column as the entity's creation
/// timestamp. Generated insert and upsert methods assign <c>UtcNow</c> when the property is unset
/// (default / <see langword="null"/>), and generated update methods exclude the column from the
/// UPDATE SET (and bind list), so the stored creation time can never be clobbered — even when
/// updating an entity instance that was constructed rather than loaded. The assignment mutates the
/// entity, so the caller observes the stamped value. At most one per entity.
/// </summary>
/// <remarks>
/// Set-based mutations (<c>[InquiryUpdateWhere]</c>) never touch auditing columns. Timestamps are
/// generated client-side in UTC; for database-clock stamping use
/// <c>[InquiryColumn(DefaultExpression = …)]</c> instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryCreatedAtAttribute : InquiryColumnAttribute
{
    /// <summary>Initializes the attribute mapping the property to a column of the same name.</summary>
    public InquiryCreatedAtAttribute()
    {
    }

    /// <summary>Initializes the attribute with an explicit column name.</summary>
    public InquiryCreatedAtAttribute(string name)
        : base(name)
    {
    }
}
