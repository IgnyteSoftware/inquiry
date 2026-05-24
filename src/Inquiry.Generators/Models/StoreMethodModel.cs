using Microsoft.CodeAnalysis;
using Inquiry.Generators.Features.StoreOperations;

namespace Inquiry.Generators.Models;

internal sealed class StoreMethodModel
{
    public StoreMethodModel(IMethodSymbol symbol, IStoreOperationFeature feature, ColumnModel? fieldColumn = null)
    {
        Symbol = symbol;
        Feature = feature;
        FieldColumn = fieldColumn;
    }

    public IMethodSymbol Symbol { get; }

    public IStoreOperationFeature Feature { get; }

    public ColumnModel? FieldColumn { get; }
}
