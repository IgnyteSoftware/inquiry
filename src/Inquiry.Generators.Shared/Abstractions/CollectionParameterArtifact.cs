namespace Inquiry.Generators.Abstractions;

/// <summary>Compile-time database artifact required by one collection-parameter transport.</summary>
public sealed record CollectionParameterArtifact(
    string Identity,
    string Schema,
    string Name,
    string RuntimeTypeName,
    string SchemaDdl,
    string CreateDdl,
    string ValidationName,
    string ElementSignature);
