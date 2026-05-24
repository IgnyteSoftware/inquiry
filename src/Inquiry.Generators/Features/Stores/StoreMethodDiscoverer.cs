using System.Collections.Immutable;
using System.Linq;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Features.StoreOperations;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Features.Stores;

internal static class StoreMethodDiscoverer
{
    public static ImmutableArray<StoreMethodModel> Discover(
        SourceProductionContext context,
        INamedTypeSymbol storeSymbol,
        EntityModel entity,
        ImmutableArray<IStoreOperationFeature> features)
    {
        var methods = ImmutableArray.CreateBuilder<StoreMethodModel>();

        foreach (var method in storeSymbol.GetMembers().OfType<IMethodSymbol>().Where(static m => m.MethodKind == MethodKind.Ordinary))
        {
            if (!TryGetFeature(method, features, out var feature, out var operationAttribute))
            {
                continue;
            }

            if (!method.IsAbstract)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.MethodMustBeAbstract, method.Locations.FirstOrDefault(), method.Name));
                continue;
            }

            var model = feature.CreateMethod(context, method, operationAttribute, entity);
            if (model is not null)
            {
                methods.Add(model);
            }
        }

        return methods.ToImmutable();
    }

    private static bool TryGetFeature(
        IMethodSymbol method,
        ImmutableArray<IStoreOperationFeature> features,
        out IStoreOperationFeature feature,
        out AttributeData attribute)
    {
        foreach (var candidate in method.GetAttributes())
        {
            if (!GeneratorHelpers.IsStoreAttribute(candidate))
            {
                continue;
            }

            foreach (var registeredFeature in features)
            {
                if (candidate.AttributeClass?.Name == registeredFeature.AttributeName)
                {
                    feature = registeredFeature;
                    attribute = candidate;
                    return true;
                }
            }
        }

        feature = null!;
        attribute = null!;
        return false;
    }
}
