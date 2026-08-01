namespace Inquiry.Generators.Abstractions;

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

/// <summary>Compile-time database artifact required by one collection-parameter transport.</summary>
public sealed record CollectionParameterArtifact(
    string Identity,
    string Schema,
    string Name,
    string RuntimeTypeName,
    string SchemaDdl,
    string CreateDdl,
    string ValidationName,
    string ElementSignature,
    string RuntimeDescriptorTypeName,
    string RuntimeDescriptorFieldName,
    string RuntimeDescriptorExpression,
    string ValidationSql);

/// <summary>Provider-owned input to exact collection transport resolution.</summary>
public sealed record CollectionParameterContext(
    string? OwningSchema,
    string OperationName,
    IColumn Column,
    bool ElementIsNullable);

/// <summary>Provider-owned diagnostic produced while resolving a collection transport.</summary>
public sealed record CollectionParameterDiagnostic(
    DiagnosticDescriptor Descriptor,
    string? Facet,
    string FailureMessage,
    ImmutableArray<string> MessageArguments);

/// <summary>Atomic provider resolution: one artifact/descriptor or one build-time error.</summary>
public sealed record CollectionParameterResolution(
    CollectionParameterArtifact? Artifact,
    CollectionParameterDiagnostic? Diagnostic)
{
    public bool IsValid => Diagnostic is null;
}

/// <summary>Provider-owned input used to emit a resolved collection binding.</summary>
public sealed record CollectionParameterBindingContext(
    CollectionParameterResolution Resolution,
    string CommandExpression,
    string ParameterName,
    string ValueExpression);

/// <summary>Input for resolving a stored-procedure TVP parameter (no entity column available).</summary>
public sealed record ProcedureTvpContext(
    string OperationName,
    string ParameterName,
    string TvpTypeName,
    DbTypeClass ElementTypeClass,
    bool ElementIsNullable,
    bool IsUnicode,
    int Length,
    int Precision,
    int Scale);

/// <summary>Result of resolving a stored-procedure TVP: the descriptor expression, or a diagnostic.</summary>
public sealed record ProcedureTvpResolution(
    string? DescriptorExpression,
    string? DescriptorTypeFqn,
    string? BinderFqn,
    CollectionParameterDiagnostic? Diagnostic)
{
    public bool IsValid => Diagnostic is null;
}
