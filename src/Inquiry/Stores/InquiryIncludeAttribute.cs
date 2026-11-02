namespace Inquiry.Stores;

/// <summary>
/// Limits an <see cref="InquirySelectAllEagerAttribute"/> or <see cref="InquirySelectOneByKeyEagerAttribute"/>
/// method to the named navigation. Apply once per navigation to load more than one. A method without
/// this attribute loads every navigation declared with <c>[InquiryRelation]</c> or <c>[InquiryManyToMany]</c>.
/// </summary>
/// <remarks>
/// Loading is one level deep. Each name must match a relation navigation on the store's entity exactly,
/// and may appear only once; otherwise the build fails with INQ107. Use <c>nameof</c> so a rename
/// stays in step. Navigations that are not named stay at their initialized value.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class InquiryIncludeAttribute : Attribute
{
    /// <summary>Gets the name of the navigation property this method loads.</summary>
    public string RelationName { get; }

    /// <summary>Loads the named navigation in this eager method.</summary>
    /// <param name="relationName">The navigation property name, for example <c>nameof(Order.Customer)</c>.</param>
    public InquiryIncludeAttribute(string relationName) => RelationName = relationName;
}
