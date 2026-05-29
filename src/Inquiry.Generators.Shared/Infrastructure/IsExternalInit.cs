// Polyfill that lets C# 9 records and init-only setters compile against netstandard2.0, which
// does not ship System.Runtime.CompilerServices.IsExternalInit. Internal so it does not leak.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
