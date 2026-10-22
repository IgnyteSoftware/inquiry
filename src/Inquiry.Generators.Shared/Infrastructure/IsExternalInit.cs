// Polyfill that lets C# 9 records and init-only setters compile against netstandard2.0, which
// does not ship System.Runtime.CompilerServices.IsExternalInit. Internal so it does not leak.
namespace System.Runtime.CompilerServices;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell",
    "S2094:Classes should not be empty",
    Justification = "The compiler needs this type to exist by name; it has no members by design.")]
internal static class IsExternalInit
{
}
