namespace Inquiry.Stores;

/// <summary>
/// Generates a method that updates the listed fields on every row matching one or more
/// <see cref="InquiryWhereAttribute"/> criteria — the set-based analog of
/// <see cref="InquiryUpdateAttribute"/> (compare EF Core's <c>ExecuteUpdate</c>). The method returns
/// <c>Task&lt;int&gt;</c> — the number of rows affected.
/// </summary>
/// <remarks>
/// Parameter convention: the method's first N non-<see cref="System.Threading.CancellationToken"/>
/// parameters supply the SET values (in <see cref="SetFields"/> order); the remaining parameters bind
/// the <see cref="InquiryWhereAttribute"/> criteria positionally as usual. Each SET field must map to
/// a mutable column — not a key, a database-generated column, the soft-delete indicator, or a
/// concurrency token. At least one <see cref="InquiryWhereAttribute"/> criterion is required: an
/// unfiltered set-based update is almost certainly a bug and is rejected at compile time. Use
/// <see cref="InquiryUpdateAllAttribute"/> to update a whole entity collection instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryUpdateWhereAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryUpdateWhereAttribute"/> class.
    /// </summary>
    /// <param name="setFields">
    /// One or more mapped property or column names the UPDATE assigns. At least one must be supplied.
    /// </param>
    public InquiryUpdateWhereAttribute(params string[] setFields)
    {
        if (setFields is null || setFields.Length == 0)
        {
            throw new ArgumentException("At least one SET field must be supplied.", nameof(setFields));
        }

        for (var i = 0; i < setFields.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(setFields[i]))
            {
                throw new ArgumentException("SET field names cannot be empty.", nameof(setFields));
            }
        }

        SetFields = setFields;
    }

    /// <summary>
    /// Gets the mapped property or column names assigned by the generated UPDATE, in declaration order.
    /// </summary>
    public IReadOnlyList<string> SetFields { get; }
}
