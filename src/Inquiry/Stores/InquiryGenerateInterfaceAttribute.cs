namespace Inquiry.Stores;

/// <summary>
/// Opts a generated store into interface generation. The generator emits a
/// <c>public partial interface I{StoreName}</c> in the store's namespace containing the signature of
/// every store method it implements (including default parameter values), and declares the generated
/// half of the store as implementing it. The interface is additionally registered in DI as a scoped
/// forward to the concrete store, so services can depend on (and mock) <c>I{StoreName}</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InquiryGenerateInterfaceAttribute : Attribute
{
}
