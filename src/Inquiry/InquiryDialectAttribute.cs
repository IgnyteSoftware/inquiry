namespace Inquiry;

/// <summary>
/// Identifies the SQL dialect a provider assembly targets, so the Inquiry source generator can
/// emit pre-built provider-specific SQL as <c>const</c> fields on each generated store class.
/// </summary>
/// <remarks>
/// Each provider's analyzer ships a generator that hardcodes its own dialect. At codegen time
/// the generator inspects this attribute on the consuming assembly (explicit override) and on
/// referenced assemblies (provider runtime DLLs ship it pre-applied) to decide whether it should
/// emit for this compilation. A consumer that references multiple provider packages can apply
/// this attribute explicitly to disambiguate; otherwise ambiguity surfaces as <c>INQ014</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDialectAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryDialectAttribute"/> class.
    /// </summary>
    /// <param name="name">
    /// The dialect identifier. The shipped provider packages use <c>"Sqlite"</c>,
    /// <c>"PostgreSql"</c>, and <c>"SqlServer"</c>. An unrecognised value (one that no installed
    /// provider analyzer handles) leaves the partial store methods without implementations.
    /// </param>
    public InquiryDialectAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Gets the dialect identifier.</summary>
    public string Name { get; }
}
