using System.Text;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Features.StoreOperations;

internal sealed class SelectOneByKeyFeature : StoreOperationFeatureBase
{
    public override string AttributeName => "InquirySelectOneByKeyAttribute";

    protected override bool HasSupportedReturnType(ITypeSymbol returnType, EntityModel entity)
    {
        return GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", entity.Symbol);
    }

    protected override StoreMethodModel? CreateValidatedMethod(SourceProductionContext context, IMethodSymbol method, AttributeData attribute, EntityModel entity)
    {
        if (!StoreMethodValidation.HasKeyAndCancellationToken(method, entity))
        {
            ReportInvalidParameters(context, method);
            return null;
        }

        return new StoreMethodModel(method, this);
    }

    public override void GenerateMethod(StringBuilder source, StoreMethodModel method, EntityModel entity)
    {
        var keyParameter = EntityOrValueParameter(method.Symbol);
        AppendMethodHeader(source, method.Symbol, isAsync: true);
        source.AppendLine($"        return await _inquiry.QuerySingleOrDefaultAsync<{EntityType(entity)}>(");
        source.AppendLine("            _sqlStatements.SelectByKey,");
        source.AppendLine($"            new {{ key = {keyParameter} }},");
        source.AppendLine($"            {CancellationParameter(method.Symbol)}).ConfigureAwait(false);");
        source.AppendLine("    }");
    }
}
