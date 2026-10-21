// Polyfill so C# records with init-only setters compile on netstandard2.0.
namespace System.Runtime.CompilerServices;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2094:Classes should not be empty", Justification = "The compiler needs this empty type to support init-only setters on netstandard2.0.")]
internal static class IsExternalInit { }
