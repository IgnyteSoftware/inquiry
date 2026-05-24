using System.Text;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Features.StoreOperations;

internal sealed class SelectAllFeature : StoreOperationFeatureBase
{
    public override string AttributeName => "InquirySelectAllAttribute";

    protected override bool HasSupportedReturnType(ITypeSymbol returnType, EntityModel entity)
    {
        return GeneratorHelpers.IsGenericType(returnType, "System.Collections.Generic.IAsyncEnumerable<T>", entity.Symbol);
    }

    protected override StoreMethodModel? CreateValidatedMethod(SourceProductionContext context, IMethodSymbol method, AttributeData attribute, EntityModel entity)
    {
        if (!StoreMethodValidation.HasOnlyCancellationToken(method))
        {
            ReportInvalidParameters(context, method);
            return null;
        }

        return new StoreMethodModel(method, this);
    }

    public override void GenerateMethod(StringBuilder source, StoreMethodModel method, EntityModel entity)
    {
        AppendMethodHeader(source, method.Symbol, isAsync: false);
        source.AppendLine($"        return _inquiry.QueryAsync<{EntityType(entity)}>(new global::Inquiry.Commands.InquiryCommandDefinition(_sqlStatements.SelectAll), {CancellationParameter(method.Symbol)});");
        source.AppendLine("    }");
    }
}
