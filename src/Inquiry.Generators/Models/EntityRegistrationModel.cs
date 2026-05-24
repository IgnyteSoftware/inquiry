using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

internal sealed class EntityRegistrationModel
{
    public EntityRegistrationModel(INamedTypeSymbol entityType, string metadataTypeName, string materializerTypeName)
    {
        EntityType = entityType;
        MetadataTypeName = metadataTypeName;
        MaterializerTypeName = materializerTypeName;
    }

    public INamedTypeSymbol EntityType { get; }

    public string MetadataTypeName { get; }

    public string MaterializerTypeName { get; }
}
