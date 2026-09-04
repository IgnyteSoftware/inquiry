namespace Inquiry.Stores;

/// <summary>
/// Marks a custom method attribute as a reusable collection of <see cref="InquiryWhereAttribute"/>
/// criteria. The generator expands the criteria declared on the custom attribute at each use site.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InquirySpecificationAttribute : Attribute
{
}
