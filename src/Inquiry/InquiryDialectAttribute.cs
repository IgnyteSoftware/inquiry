namespace Inquiry;

/// <summary>
/// Identifies the SQL dialect a provider assembly targets, so the Inquiry source generator can
/// emit pre-built provider-specific SQL as <c>const</c> fields on each generated store class.
/// </summary>
/// <remarks>
/// The generator looks for this attribute on every referenced assembly (and on the consuming
/// assembly itself, which takes precedence if present) to decide which SQL builder to use.
/// Exactly one dialect must resolve for store SQL to be generated; the official provider
/// packages (<c>Inquiry.Sqlite</c>, <c>Inquiry.PostgreSql</c>, <c>Inquiry.SqlServer</c>) ship
/// this attribute pre-applied, so a typical consumer never needs to add it manually.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDialectAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryDialectAttribute"/> class.
    /// </summary>
    /// <param name="name">
    /// The dialect identifier. Currently recognised values: <c>"Sqlite"</c>, <c>"PostgreSql"</c>,
    /// <c>"SqlServer"</c>. Any other value produces an <c>INQ013</c> generator diagnostic.
    /// </param>
    public InquiryDialectAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Gets the dialect identifier.</summary>
    public string Name { get; }
}
