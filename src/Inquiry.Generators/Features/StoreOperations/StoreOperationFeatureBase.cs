using System.Text;
using System.Linq;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Features.StoreOperations;

internal abstract class StoreOperationFeatureBase : IStoreOperationFeature
{
    public abstract string AttributeName { get; }

    public StoreMethodModel? CreateMethod(SourceProductionContext context, IMethodSymbol method, AttributeData attribute, EntityModel entity)
    {
        if (!HasSupportedReturnType(method.ReturnType, entity))
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.UnsupportedReturnType, method.Locations.FirstOrDefault(), method.Name, method.ReturnType.ToDisplayString()));
            return null;
        }

        var model = CreateValidatedMethod(context, method, attribute, entity);
        if (model is null)
        {
            return null;
        }

        return model;
    }

    public abstract void GenerateMethod(StringBuilder source, StoreMethodModel method, EntityModel entity);

    protected abstract bool HasSupportedReturnType(ITypeSymbol returnType, EntityModel entity);

    protected abstract StoreMethodModel? CreateValidatedMethod(SourceProductionContext context, IMethodSymbol method, AttributeData attribute, EntityModel entity);

    protected static void ReportInvalidParameters(SourceProductionContext context, IMethodSymbol method)
    {
        context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.InvalidParameters, method.Locations.FirstOrDefault(), method.Name));
    }

    protected static string EntityType(EntityModel entity)
    {
        return entity.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    protected static void AppendMethodHeader(StringBuilder source, IMethodSymbol method, bool isAsync)
    {
        var returnType = method.ReturnType.ToDisplayString(KnownSymbols.FullyQualifiedNullableFormat);
        var asyncModifier = isAsync ? "async " : string.Empty;
        source.AppendLine($"    public override {asyncModifier}{returnType} {method.Name}({GeneratorHelpers.GetParameterDeclaration(method)})");
        source.AppendLine("    {");
    }

    protected static string CancellationParameter(IMethodSymbol method)
    {
        return method.Parameters[method.Parameters.Length - 1].Name;
    }

    protected static string EntityOrValueParameter(IMethodSymbol method)
    {
        return method.Parameters.Length > 1 ? method.Parameters[0].Name : "entity";
    }
}
