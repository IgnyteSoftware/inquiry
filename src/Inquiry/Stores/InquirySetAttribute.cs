namespace Inquiry.Stores;

/// <summary>Declares one expression-based assignment on a predicate <see cref="InquiryUpdateAttribute"/>.</summary>
/// <remarks>
/// Use <c>{Field}</c> for mapped columns and <c>@parameter</c> for method parameters. For example,
/// <c>[InquirySet("Quantity", "{Quantity} + @amount")]</c>. Expressions are validated and rendered
/// for the selected provider at compile time.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class InquirySetAttribute : Attribute
{
    /// <summary>Initializes an expression-based assignment.</summary>
    public InquirySetAttribute(string field, string expression)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("Field name cannot be empty.", nameof(field));
        }

        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException("SET expression cannot be empty.", nameof(expression));
        }

        Field = field;
        Expression = expression;
    }

    /// <summary>Gets the mapped property or column assigned by this expression.</summary>
    public string Field { get; }

    /// <summary>Gets the compile-time expression template.</summary>
    public string Expression { get; }
}
