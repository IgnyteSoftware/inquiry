using System;

namespace Inquiry.Entities;

/// <summary>
/// Marks a <see cref="string"/> column as the entity's last modifier. Generated insert, update, and
/// upsert methods (including their batch forms) assign <see cref="Inquiry.InquiryAuditContext.CurrentUser"/>
/// before binding, so every write through a generated store advances it. The assignment mutates the
/// entity, so the caller observes the stamped value. At most one per entity.
/// </summary>
/// <remarks>
/// Set the ambient user with <see cref="Inquiry.InquiryAuditContext.BeginScope"/> (typically once per
/// request). The column must be a <see cref="string"/> (nullable allowed) and must not be a key,
/// database-generated, database-defaulted, the soft-delete indicator, or a concurrency token.
/// Predicate updates (<c>[InquiryUpdate]</c> with <c>[InquiryWhere]</c>) never touch auditing columns.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryModifiedByAttribute : InquiryColumnAttribute
{
    /// <summary>Initializes the attribute mapping the property to a column of the same name.</summary>
    public InquiryModifiedByAttribute()
    {
    }

    /// <summary>Initializes the attribute with an explicit column name.</summary>
    public InquiryModifiedByAttribute(string name)
        : base(name)
    {
    }
}
