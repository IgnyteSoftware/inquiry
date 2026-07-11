using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Abstractions;

/// <summary>Compile-time description of an effective provider value before boxing.</summary>
public readonly record struct ParameterValueExpressionContext(
    string ValueExpression,
    string ProviderTypeName,
    SpecialType ProviderSpecialType,
    bool ProviderIsDateOnly = false,
    bool ProviderIsTimeOnly = false,
    bool ProviderIsDateTimeOffset = false);
