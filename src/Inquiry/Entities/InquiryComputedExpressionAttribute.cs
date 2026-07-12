namespace Inquiry.Entities;

/// <summary>Overrides a computed-column SQL expression for one provider.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class InquiryComputedExpressionAttribute : Attribute
{
    /// <summary>Creates a provider-specific computed-expression override.</summary>
    /// <param name="providerId">Stable lowercase provider id.</param>
    /// <param name="expression">Raw provider SQL expression.</param>
    public InquiryComputedExpressionAttribute(string providerId, string expression)
    {
        if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Provider id cannot be empty.", nameof(providerId));
        if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException("Expression cannot be empty.", nameof(expression));
        ProviderId = providerId;
        Expression = expression;
    }

    /// <summary>Gets the stable lowercase provider id.</summary>
    public string ProviderId { get; }
    /// <summary>Gets the raw provider SQL expression.</summary>
    public string Expression { get; }
}
