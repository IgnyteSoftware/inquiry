using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

internal sealed class StoreRegistrationModel
{
    public StoreRegistrationModel(INamedTypeSymbol storeType)
    {
        StoreType = storeType;
    }

    public INamedTypeSymbol StoreType { get; }
}
