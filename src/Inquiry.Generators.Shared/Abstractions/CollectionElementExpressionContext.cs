using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Abstractions;

public readonly record struct CollectionElementExpressionContext(
    string ValueExpression,
    string ProviderTypeName,
    SpecialType ProviderSpecialType);

public readonly record struct CollectionElementExpression(
    string ValueExpression,
    string StorageTypeName,
    bool IsTransformed);
