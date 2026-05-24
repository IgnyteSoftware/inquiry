using System;
using System.Linq;
using System.Text;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Features.StoreOperations;

internal sealed class SelectAllByFieldFeature : StoreOperationFeatureBase
{
    public override string AttributeName => "InquirySelectAllByFieldAttribute";

    protected override bool HasSupportedReturnType(ITypeSymbol returnType, EntityModel entity)
    {
        return GeneratorHelpers.IsGenericType(returnType, "System.Collections.Generic.IAsyncEnumerable<T>", entity.Symbol);
    }

    protected override StoreMethodModel? CreateValidatedMethod(SourceProductionContext context, IMethodSymbol method, AttributeData attribute, EntityModel entity)
    {
        var selectedField = GeneratorHelpers.GetConstructorString(attribute);
        var fieldColumn = selectedField is null ? null : entity.Columns.FirstOrDefault(c =>
            string.Equals(c.PropertyName, selectedField, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.ColumnName, selectedField, StringComparison.OrdinalIgnoreCase));

        if (fieldColumn is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.UnknownField, method.Locations.FirstOrDefault(), method.Name, selectedField));
            return null;
        }

        if (!StoreMethodValidation.HasFieldAndCancellationToken(method, fieldColumn))
        {
            ReportInvalidParameters(context, method);
            return null;
        }

        return new StoreMethodModel(method, this, fieldColumn);
    }

    public override void GenerateMethod(StringBuilder source, StoreMethodModel method, EntityModel entity)
    {
        var fieldColumn = method.FieldColumn!;
        var valueParameter = EntityOrValueParameter(method.Symbol);
        AppendMethodHeader(source, method.Symbol, isAsync: false);
        source.AppendLine($"        return _inquiry.QueryAsync<{EntityType(entity)}>(");
        source.AppendLine($"            _sqlStatements.SelectByField(new global::Inquiry.Sql.InquirySqlColumn(\"{GeneratorHelpers.Escape(fieldColumn.PropertyName)}\", \"{GeneratorHelpers.Escape(fieldColumn.ColumnName)}\", isKey: {GeneratorHelpers.BooleanLiteral(fieldColumn.IsKey)}, isGenerated: {GeneratorHelpers.BooleanLiteral(fieldColumn.IsGenerated)})),");
        source.AppendLine($"            new {{ value = {valueParameter} }},");
        source.AppendLine($"            {CancellationParameter(method.Symbol)});");
        source.AppendLine("    }");
    }
}
