using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Abstractions;

/// <summary>
/// Compile-time description of one provider primitive read in a generated materializer.
/// <see cref="Ordinal"/> is the C# expression for the column ordinal, such as <c>2</c> or <c>ordinals[2]</c>.
/// </summary>
public readonly record struct ReaderExpressionContext(
    string Ordinal,
    string LogicalTypeName,
    string ProviderTypeName,
    SpecialType ProviderSpecialType,
    ReaderResultRole Role = ReaderResultRole.Column,
    bool ProviderIsGuid = false,
    bool ProviderIsByteArray = false,
    bool ProviderIsDateOnly = false,
    bool ProviderIsTimeOnly = false);

public enum ReaderResultRole
{
    Column,
    Count,
}
