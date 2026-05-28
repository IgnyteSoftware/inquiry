using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

internal sealed class EntityRegistrationModel
{
    public EntityRegistrationModel(INamedTypeSymbol entityType, string materializerTypeName, string structMaterializerTypeName)
    {
        EntityType = entityType;
        MaterializerTypeName = materializerTypeName;
        StructMaterializerTypeName = structMaterializerTypeName;
    }

    public INamedTypeSymbol EntityType { get; }

    /// <summary>Class materializer registered as singleton in DI; used by ad-hoc IInquiry queries.</summary>
    public string MaterializerTypeName { get; }

    /// <summary>
    /// Struct materializer used by generated stores. Passed as <c>default(TMaterializer)</c> so
    /// no DI lookup is needed, and the JIT specializes the pipeline body per concrete struct.
    /// </summary>
    public string StructMaterializerTypeName { get; }
}
