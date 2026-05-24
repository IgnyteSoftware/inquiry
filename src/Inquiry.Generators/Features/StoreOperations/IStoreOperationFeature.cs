using System.Text;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Features.StoreOperations;

internal interface IStoreOperationFeature
{
    string AttributeName { get; }

    StoreMethodModel? CreateMethod(SourceProductionContext context, IMethodSymbol method, AttributeData attribute, EntityModel entity);

    void GenerateMethod(StringBuilder source, StoreMethodModel method, EntityModel entity);
}
