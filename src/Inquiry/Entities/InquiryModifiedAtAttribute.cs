using System;

namespace Inquiry.Entities;

/// <summary>
/// Marks a <see cref="DateTime"/> or <see cref="DateTimeOffset"/> column as the entity's
/// last-modified timestamp. Generated insert, update, and upsert methods (including their batch
/// forms) assign <c>UtcNow</c> before binding, so every write through a generated store advances
/// it. The assignment mutates the entity, so the caller observes the stamped value. At most one
/// per entity.
/// </summary>
/// <remarks>
/// Set-based mutations (<c>[InquiryUpdateWhere]</c>) never touch auditing columns. Timestamps are
/// generated client-side in UTC.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryModifiedAtAttribute : InquiryColumnAttribute
{
    /// <summary>Initializes the attribute mapping the property to a column of the same name.</summary>
    public InquiryModifiedAtAttribute()
    {
    }

    /// <summary>Initializes the attribute with an explicit column name.</summary>
    public InquiryModifiedAtAttribute(string name)
        : base(name)
    {
    }
}
