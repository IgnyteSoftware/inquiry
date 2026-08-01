namespace Inquiry.Stores;

/// <summary>
/// Generates this store method without the named <c>[InquiryGlobalFilter(Name = "…")]</c> predicate,
/// mirroring EF's <c>IgnoreQueryFilters(string[])</c>. Apply once per filter to bypass more than one.
/// Fully compile-time: the name must match a named filter on the store's entity (an unknown or
/// unnamed filter is a build error), the decision is resolved by the generator, and the method's SQL
/// stays a const string. Only named filters can be bypassed — an unnamed filter is a hard boundary —
/// and the soft-delete filter is not a global filter; soft-deleted rows are included via the
/// operation attribute's <c>IncludeDeleted</c> instead. Valid on select-shaped operations only.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class InquiryIgnoreFilterAttribute : Attribute
{
    /// <summary>The <see cref="Inquiry.Entities.InquiryGlobalFilterAttribute.Name"/> of the filter to bypass.</summary>
    public string FilterName { get; }

    /// <summary>Bypasses the named global filter for this generated method.</summary>
    /// <param name="filterName">The name assigned via <c>[InquiryGlobalFilter(Name = "…")]</c>.</param>
    public InquiryIgnoreFilterAttribute(string filterName) => FilterName = filterName;
}
