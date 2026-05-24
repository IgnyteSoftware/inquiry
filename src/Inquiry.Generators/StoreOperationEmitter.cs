using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using System;
using System.Linq;
using System.Text;

namespace Inquiry.Generators;

/// <summary>
/// Single source of truth for which CRUD operation a store method maps to,
/// how its signature must look, and what code to emit for it.
/// </summary>
internal static class StoreOperationEmitter
{
    /// <summary>
    /// Resolves the operation attribute on a method (if any). Returns <see cref="StoreOperation.None"/>
    /// when the method carries no recognized operation attribute.
    /// </summary>
    public static StoreOperation GetOperation(IMethodSymbol method, out AttributeData? attribute)
    {
        foreach (var candidate in method.GetAttributes())
        {
            if (!GeneratorHelpers.IsStoreAttribute(candidate))
            {
                continue;
            }

            switch (candidate.AttributeClass?.Name)
            {
                case "InquirySelectAllAttribute":
                    attribute = candidate;
                    return StoreOperation.SelectAll;
                case "InquirySelectOneByKeyAttribute":
                    attribute = candidate;
                    return StoreOperation.SelectOneByKey;
                case "InquirySelectAllByFieldAttribute":
                    attribute = candidate;
                    return StoreOperation.SelectAllByField;
                case "InquiryInsertAttribute":
                    attribute = candidate;
                    return StoreOperation.Insert;
                case "InquiryUpdateAttribute":
                    attribute = candidate;
                    return StoreOperation.Update;
                case "InquiryDeleteOneByKeyAttribute":
                    attribute = candidate;
                    return StoreOperation.DeleteOneByKey;
            }
        }

        attribute = null;
        return StoreOperation.None;
    }

    /// <summary>
    /// Validates a method's signature against its operation and returns a populated
    /// <see cref="StoreMethodModel"/>, or <see langword="null"/> when validation fails.
    /// Diagnostics are reported via <paramref name="context"/>.
    /// </summary>
    public static StoreMethodModel? Validate(
        SourceProductionContext context,
        IMethodSymbol method,
        StoreOperation operation,
        AttributeData attribute,
        EntityModel entity)
    {
        if (!HasSupportedReturnType(operation, method.ReturnType, entity))
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.UnsupportedReturnType, method.Locations.FirstOrDefault(), method.Name, method.ReturnType.ToDisplayString()));
            return null;
        }

        // For SelectAllByField, resolve the target column before parameter validation —
        // parameter type must match it.
        ColumnModel? fieldColumn = null;
        if (operation == StoreOperation.SelectAllByField)
        {
            var selectedField = GeneratorHelpers.GetConstructorString(attribute);
            fieldColumn = selectedField is null ? null : entity.Columns.FirstOrDefault(c =>
                string.Equals(c.PropertyName, selectedField, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.ColumnName, selectedField, StringComparison.OrdinalIgnoreCase));

            if (fieldColumn is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.UnknownField, method.Locations.FirstOrDefault(), method.Name, selectedField));
                return null;
            }
        }

        if (!HasSupportedParameters(method, operation, entity, fieldColumn))
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.InvalidParameters, method.Locations.FirstOrDefault(), method.Name));
            return null;
        }

        return new StoreMethodModel(method, operation, fieldColumn);
    }

    /// <summary>
    /// Emits the override body for a store method.
    /// </summary>
    public static void Emit(StringBuilder source, StoreMethodModel method, EntityModel entity)
    {
        var symbol = method.Symbol;
        var entityType = entity.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var cancellation = symbol.Parameters[symbol.Parameters.Length - 1].Name;
        var firstParameter = symbol.Parameters.Length > 1 ? symbol.Parameters[0].Name : "entity";
        var parameters = GeneratorHelpers.GetParameterDeclaration(symbol);

        switch (method.Operation)
        {
            case StoreOperation.SelectAll:
                AppendHeader(source, symbol, parameters, isAsync: false);
                source.AppendLine($"        return Inquiry.QueryAsync<{entityType}>(new global::Inquiry.Commands.InquiryCommandDefinition(_sqlStatements.SelectAll), {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectOneByKey:
                AppendHeader(source, symbol, parameters, isAsync: true);
                source.AppendLine($"        return await Inquiry.QuerySingleOrDefaultAsync<{entityType}>(");
                source.AppendLine("            _sqlStatements.SelectByKey,");
                source.AppendLine($"            new {{ key = {firstParameter} }},");
                source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectAllByField:
                AppendHeader(source, symbol, parameters, isAsync: false);
                source.AppendLine($"        return Inquiry.QueryAsync<{entityType}>(");
                source.AppendLine($"            _sqlStatements.SelectByField[\"{GeneratorHelpers.Escape(method.FieldColumn!.PropertyName)}\"],");
                source.AppendLine($"            new {{ value = {firstParameter} }},");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.Insert:
                AppendHeader(source, symbol, parameters, isAsync: false);
                source.AppendLine("        return Inquiry.ExecuteAsync(");
                source.AppendLine("            _sqlStatements.Insert,");
                source.AppendLine("            new");
                source.AppendLine("            {");
                foreach (var column in entity.Columns.Where(c => !c.IsGenerated))
                {
                    source.AppendLine($"                {column.PropertyName} = {firstParameter}.{column.PropertyName},");
                }
                source.AppendLine("            },");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.Update:
                AppendHeader(source, symbol, parameters, isAsync: true);
                source.AppendLine("        return await Inquiry.ExecuteAsync(");
                source.AppendLine("            _sqlStatements.Update,");
                source.AppendLine("            new");
                source.AppendLine("            {");
                foreach (var column in entity.Columns.Where(c => c.IsKey || !c.IsGenerated))
                {
                    source.AppendLine($"                {column.PropertyName} = {firstParameter}.{column.PropertyName},");
                }
                source.AppendLine("            },");
                source.AppendLine($"            {cancellation}).ConfigureAwait(false) > 0;");
                source.AppendLine("    }");
                break;

            case StoreOperation.DeleteOneByKey:
                AppendHeader(source, symbol, parameters, isAsync: true);
                source.AppendLine("        return await Inquiry.ExecuteAsync(");
                source.AppendLine("            _sqlStatements.DeleteByKey,");
                source.AppendLine($"            new {{ key = {firstParameter} }},");
                source.AppendLine($"            {cancellation}).ConfigureAwait(false) > 0;");
                source.AppendLine("    }");
                break;
        }
    }

    private static void AppendHeader(StringBuilder source, IMethodSymbol method, string parameters, bool isAsync)
    {
        var returnType = method.ReturnType.ToDisplayString(KnownSymbols.FullyQualifiedNullableFormat);
        var asyncModifier = isAsync ? "async " : string.Empty;
        source.AppendLine($"    public override {asyncModifier}{returnType} {method.Name}({parameters})");
        source.AppendLine("    {");
    }

    private static bool HasSupportedReturnType(StoreOperation operation, ITypeSymbol returnType, EntityModel entity)
    {
        return operation switch
        {
            StoreOperation.SelectAll or StoreOperation.SelectAllByField =>
                GeneratorHelpers.IsGenericType(returnType, "System.Collections.Generic.IAsyncEnumerable<T>", entity.Symbol),
            StoreOperation.SelectOneByKey =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", entity.Symbol),
            StoreOperation.Insert =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Int32),
            StoreOperation.Update or StoreOperation.DeleteOneByKey =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Boolean),
            _ => false,
        };
    }

    private static bool HasSupportedParameters(IMethodSymbol method, StoreOperation operation, EntityModel entity, ColumnModel? fieldColumn)
    {
        if (method.Parameters.Length == 0 || !GeneratorHelpers.IsCancellationToken(method.Parameters[method.Parameters.Length - 1].Type))
        {
            return false;
        }

        return operation switch
        {
            StoreOperation.SelectAll =>
                method.Parameters.Length == 1,
            StoreOperation.SelectOneByKey or StoreOperation.DeleteOneByKey =>
                method.Parameters.Length == 2 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, entity.Key.Type.Symbol),
            StoreOperation.SelectAllByField =>
                method.Parameters.Length == 2 &&
                fieldColumn is not null &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, fieldColumn.Type.Symbol),
            StoreOperation.Insert or StoreOperation.Update =>
                method.Parameters.Length == 2 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, entity.Symbol),
            _ => false,
        };
    }
}
