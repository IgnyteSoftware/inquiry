using System;

namespace Inquiry.Stores;

/// <summary>
/// Generates a method that selects rows by one or more mapped properties or columns.
/// Multiple fields are combined with AND in the WHERE clause; method parameters must match
/// the listed field order and types.
/// </summary>
/// <remarks>
/// When no fields are supplied (<c>[InquirySelectAllByField]</c>), the filter columns are
/// <b>derived from the method name</b> (the Spring Data convention): the segment after <c>By</c>,
/// split on <c>And</c> word boundaries, names the fields — e.g. <c>SelectByCountryAndCityAsync</c>
/// filters on <c>Country</c> and <c>City</c>. Each derived field is resolved against the entity's
/// mapped properties/columns just like an explicit one (an unknown field is a compile error).
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectAllByFieldAttribute : Attribute
{
    /// <summary>
    /// Initializes a field-less attribute whose filter columns are derived from the method name
    /// (see the type remarks).
    /// </summary>
    public InquirySelectAllByFieldAttribute()
    {
        Fields = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquirySelectAllByFieldAttribute"/> class.
    /// </summary>
    /// <param name="fields">One or more mapped property or column names. At least one must be supplied.</param>
    public InquirySelectAllByFieldAttribute(params string[] fields)
    {
        if (fields is null || fields.Length == 0)
        {
            throw new ArgumentException("At least one field must be supplied.", nameof(fields));
        }

        for (var i = 0; i < fields.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(fields[i]))
            {
                throw new ArgumentException("Field names cannot be empty.", nameof(fields));
            }
        }

        Fields = fields;
    }

    /// <summary>
    /// Gets the mapped property or column names used in the generated WHERE clause, in declaration order.
    /// </summary>
    public IReadOnlyList<string> Fields { get; }

    /// <summary>
    /// Gets or sets a compile-time ORDER BY specification, e.g. <c>"Name ASC, Id DESC"</c>. Each item
    /// is <c>field [ASC|DESC]</c>; fields are resolved against the entity's mapped properties or columns
    /// and quoted at generation time (an unknown field is a compile error). Direction defaults to ASC.
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the method emits an offset-paginated query
    /// (<c>LIMIT/OFFSET</c>, or <c>OFFSET … FETCH</c> on SQL Server). When true the method must take an
    /// <c>int offset</c> and <c>int limit</c> (in that order) after the field parameters and ahead of
    /// the cancellation token, and <see cref="OrderBy"/> is required for a deterministic page order.
    /// </summary>
    public bool Paged { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the generated query includes soft-deleted rows. Has an
    /// effect only when the entity declares an <c>[InquirySoftDelete]</c> column: when false (the
    /// default) the query auto-appends the active filter (<c>= 0</c> / <c>IS NULL</c>); when true the
    /// query is emitted unfiltered so soft-deleted rows are returned.
    /// </summary>
    public bool IncludeDeleted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the generated query uses <c>SELECT DISTINCT</c> instead
    /// of <c>SELECT</c>.
    /// </summary>
    public bool Distinct { get; set; }
}
