using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

internal sealed class EntityRegistrationModel
{
    public EntityRegistrationModel(INamedTypeSymbol entityType, string materializerTypeName)
    {
        EntityType = entityType;
        MaterializerTypeName = materializerTypeName;
    }

    public INamedTypeSymbol EntityType { get; }

    public string MaterializerTypeName { get; }
}
