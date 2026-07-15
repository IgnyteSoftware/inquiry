using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Abstractions;

/// <summary>Compile-time description of one provider primitive read in a generated materializer.</summary>
public readonly record struct ReaderExpressionContext(
    int Ordinal,
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
