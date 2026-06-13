using System;

namespace Inquiry.Entities;

/// <summary>
/// Maps a class (or record) to a read-only database <b>view</b> (or any keyless, read-only
/// projection), the EF keyless-entity / TypeORM <c>@ViewEntity</c> analog. Apply
/// <c>[InquiryColumn]</c> to the properties that map the view's columns, exactly as for an
/// <see cref="InquiryTableAttribute"/> entity.
/// </summary>
/// <remarks>
/// <para>
/// A view-mapped entity is <b>read-only</b>: a store over it (<c>InquiryStore&lt;TView&gt;</c>) may
/// declare only SELECT, aggregate, and count operations; any mutation (insert/update/upsert/delete,
/// batch, or set-based) is a build error (<c>INQ052</c>).
/// </para>
/// <para>
/// No <see cref="InquiryKeyAttribute"/> is required — views are keyless by default. The schema
/// generator never emits DDL for a view (the view is defined in the database, not created by
/// Inquiry), and no foreign-key constraints are generated.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InquiryViewAttribute : Attribute
{
    /// <summary>Initializes the attribute, binding the entity to a view of the same name.</summary>
    public InquiryViewAttribute()
    {
    }

    /// <summary>Initializes the attribute with an explicit view name.</summary>
    /// <param name="viewName">The database view (or table-like object) name to select from.</param>
    public InquiryViewAttribute(string viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be empty.", nameof(viewName));
        }

        ViewName = viewName;
    }

    /// <summary>The view name to select from, or null to use the class name.</summary>
    public string? ViewName { get; }

    /// <summary>Optional schema qualifier for the view.</summary>
    public string? Schema { get; set; }
}
