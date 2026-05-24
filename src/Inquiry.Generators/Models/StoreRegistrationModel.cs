using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

internal sealed class StoreRegistrationModel
{
    public StoreRegistrationModel(INamedTypeSymbol storeType, string generatedTypeName)
    {
        StoreType = storeType;
        GeneratedTypeName = generatedTypeName;
    }

    public INamedTypeSymbol StoreType { get; }

    public string GeneratedTypeName { get; }
}
