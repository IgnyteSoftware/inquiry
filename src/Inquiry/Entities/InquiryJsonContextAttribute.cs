namespace Inquiry.Entities;

/// <summary>
/// Names the default source-generated <c>System.Text.Json.Serialization.JsonSerializerContext</c> for every
/// <see cref="InquiryJsonAttribute"/> property in the assembly. Inquiry then serializes those properties
/// through the context's metadata, which is safe under trimming and NativeAOT.
/// </summary>
/// <remarks>
/// <para>
/// The context must declare <c>[JsonSerializable(typeof(T))]</c> for each JSON property type <c>T</c>.
/// A missing type is build error <c>INQ110</c>. Inquiry does not fall back to reflection.
/// </para>
/// <para>
/// <see cref="InquiryJsonAttribute.Context"/> overrides this default for one property. Without either
/// attribute, <see cref="InquiryJsonAttribute"/> uses reflection-based serialization.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class InquiryJsonContextAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="InquiryJsonContextAttribute"/> class.</summary>
    /// <param name="contextType">The source-generated <c>JsonSerializerContext</c> type.</param>
    public InquiryJsonContextAttribute(Type contextType)
    {
        ContextType = contextType;
    }

    /// <summary>Gets the source-generated <c>JsonSerializerContext</c> type.</summary>
    public Type ContextType { get; }
}
