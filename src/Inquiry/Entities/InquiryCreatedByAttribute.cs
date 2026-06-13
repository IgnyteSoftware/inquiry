using System;

namespace Inquiry.Entities;

/// <summary>
/// Marks a <see cref="string"/> column as the entity's creator. Generated insert and upsert
/// methods assign <see cref="Inquiry.InquiryAuditContext.CurrentUser"/> when the property is unset
/// (null or empty), and generated update methods exclude the column from the UPDATE SET (and bind
/// list), so the stored creator can never be clobbered. The assignment mutates the entity, so the
/// caller observes the stamped value. At most one per entity.
/// </summary>
/// <remarks>
/// Set the ambient user with <see cref="Inquiry.InquiryAuditContext.BeginScope"/> (typically once per
/// request). The column must be a <see cref="string"/> (nullable allowed) and must not be a key,
/// database-generated, database-defaulted, the soft-delete indicator, or a concurrency token.
/// Set-based mutations (<c>[InquiryUpdateWhere]</c>) never touch auditing columns.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryCreatedByAttribute : InquiryColumnAttribute
{
    /// <summary>Initializes the attribute mapping the property to a column of the same name.</summary>
    public InquiryCreatedByAttribute()
    {
    }

    /// <summary>Initializes the attribute with an explicit column name.</summary>
    public InquiryCreatedByAttribute(string name)
        : base(name)
    {
    }
}
