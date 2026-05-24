using System.Linq;
using System.Text;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Features.StoreOperations;

internal sealed class UpdateFeature : StoreOperationFeatureBase
{
    public override string AttributeName => "InquiryUpdateAttribute";

    protected override bool HasSupportedReturnType(ITypeSymbol returnType, EntityModel entity)
    {
        return GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Boolean);
    }

    protected override StoreMethodModel? CreateValidatedMethod(SourceProductionContext context, IMethodSymbol method, AttributeData attribute, EntityModel entity)
    {
        if (!StoreMethodValidation.HasEntityAndCancellationToken(method, entity))
        {
            ReportInvalidParameters(context, method);
            return null;
        }

        return new StoreMethodModel(method, this);
    }

    public override void GenerateMethod(StringBuilder source, StoreMethodModel method, EntityModel entity)
    {
        var entityParameter = EntityOrValueParameter(method.Symbol);
        AppendMethodHeader(source, method.Symbol, isAsync: true);
        source.AppendLine("        return await _inquiry.ExecuteAsync(");
        source.AppendLine("            _sqlStatements.Update,");
        source.AppendLine("            new");
        source.AppendLine("            {");
        foreach (var column in entity.Columns.Where(c => c.IsKey || !c.IsGenerated))
        {
            source.AppendLine($"                {column.PropertyName} = {entityParameter}.{column.PropertyName},");
        }

        source.AppendLine("            },");
        source.AppendLine($"            {CancellationParameter(method.Symbol)}).ConfigureAwait(false) > 0;");
        source.AppendLine("    }");
    }
}
