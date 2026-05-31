namespace Inquiry.Stores;

/// <summary>
/// Generates a method that selects rows matching one or more <see cref="InquiryWhereAttribute"/>
/// criteria. The criteria are combined (AND by default, OR opt-in per criterion) into the WHERE
/// clause and bind positionally to the method's parameters in declaration order.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectAllByPredicateAttribute : Attribute
{
}
