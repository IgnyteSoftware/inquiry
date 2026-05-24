using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Infrastructure;

internal static class StoreMethodValidation
{
    public static bool HasOnlyCancellationToken(IMethodSymbol method)
    {
        return HasCancellationToken(method) && method.Parameters.Length == 1;
    }

    public static bool HasKeyAndCancellationToken(IMethodSymbol method, EntityModel entity)
    {
        return HasCancellationToken(method) &&
            method.Parameters.Length == 2 &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, entity.Key.Type.Symbol);
    }

    public static bool HasFieldAndCancellationToken(IMethodSymbol method, ColumnModel fieldColumn)
    {
        return HasCancellationToken(method) &&
            method.Parameters.Length == 2 &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, fieldColumn.Type.Symbol);
    }

    public static bool HasEntityAndCancellationToken(IMethodSymbol method, EntityModel entity)
    {
        return HasCancellationToken(method) &&
            method.Parameters.Length == 2 &&
            SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, entity.Symbol);
    }

    private static bool HasCancellationToken(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
        {
            return false;
        }

        var last = method.Parameters[method.Parameters.Length - 1];
        return GeneratorHelpers.IsCancellationToken(last.Type);
    }
}
