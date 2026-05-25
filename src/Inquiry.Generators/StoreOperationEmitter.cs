using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Inquiry.Generators;

/// <summary>
/// Single source of truth for which CRUD operation a store method maps to,
/// how its signature must look, and what code to emit for it.
/// </summary>
internal static class StoreOperationEmitter
{
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
                case "InquirySelectAllEagerAttribute":
                    attribute = candidate;
                    return StoreOperation.SelectAllEager;
                case "InquirySelectOneByKeyAttribute":
                    attribute = candidate;
                    return StoreOperation.SelectOneByKey;
                case "InquirySelectOneByKeyEagerAttribute":
                    attribute = candidate;
                    return StoreOperation.SelectOneByKeyEager;
                case "InquirySelectAllByFieldAttribute":
                    attribute = candidate;
                    return StoreOperation.SelectAllByField;
                case "InquiryInsertAttribute":
                    attribute = candidate;
                    return StoreOperation.Insert;
                case "InquiryUpdateAttribute":
                    attribute = candidate;
                    return StoreOperation.Update;
                case "InquiryUpsertAttribute":
                    attribute = candidate;
                    return StoreOperation.Upsert;
                case "InquiryDeleteOneByKeyAttribute":
                    attribute = candidate;
                    return StoreOperation.DeleteOneByKey;
                case "InquiryStoredProcedureAttribute":
                    attribute = candidate;
                    return StoreOperation.StoredProcedure;
            }
        }

        attribute = null;
        return StoreOperation.None;
    }

    public static StoreMethodModel? Validate(
        SourceProductionContext context,
        IMethodSymbol method,
        StoreOperation operation,
        AttributeData attribute,
        EntityModel entity)
    {
        var returnsEntity = operation is StoreOperation.Insert or StoreOperation.Update or StoreOperation.Upsert &&
            GeneratorHelpers.GetNamedBool(attribute, "ReturnEntity");

        if (!HasSupportedReturnType(operation, method.ReturnType, entity, returnsEntity))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.UnsupportedReturnType,
                method.Locations.FirstOrDefault(),
                method.Name,
                method.ReturnType.ToDisplayString()));
            return null;
        }

        ColumnModel? fieldColumn = null;
        if (operation == StoreOperation.SelectAllByField)
        {
            var selectedField = GeneratorHelpers.GetConstructorString(attribute);
            fieldColumn = selectedField is null ? null : entity.Columns.FirstOrDefault(c =>
                string.Equals(c.PropertyName, selectedField, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.ColumnName, selectedField, StringComparison.OrdinalIgnoreCase));

            if (fieldColumn is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InquiryDiagnosticDescriptors.UnknownField,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    selectedField));
                return null;
            }
        }

        string? procedureName = null;
        if (operation == StoreOperation.StoredProcedure)
        {
            procedureName = GeneratorHelpers.GetConstructorString(attribute);
            if (string.IsNullOrEmpty(procedureName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InquiryDiagnosticDescriptors.UnsupportedReturnType,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    "StoredProcedure requires a non-empty procedure name"));
                return null;
            }
        }

        if (!HasSupportedParameters(method, operation, entity, fieldColumn))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.InvalidParameters,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        return new StoreMethodModel(method, operation, fieldColumn, procedureName, returnsEntity);
    }

    public static void Emit(StringBuilder source, StoreMethodModel method, EntityModel entity, Dictionary<string, EntityModel> relationChildEntities)
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
                source.AppendLine($"        return Inquiry.QueryAsync<{entityType}>(_sqlSelectAll, {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectAllEager:
                EmitSelectAllEager(source, symbol, parameters, entityType, cancellation, entity, relationChildEntities);
                break;

            case StoreOperation.SelectOneByKey:
                AppendHeader(source, symbol, parameters, isAsync: true);
                source.AppendLine($"        return await Inquiry.QuerySingleOrDefaultAsync<{entityType}>(");
                source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(");
                source.AppendLine("                _sqlSelectByKey,");
                source.AppendLine("                new global::Inquiry.Parameters.InquiryParameter[]");
                source.AppendLine("                {");
                source.AppendLine($"                    new global::Inquiry.Parameters.InquiryParameter(\"key\", {firstParameter}),");
                source.AppendLine("                }),");
                source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectOneByKeyEager:
                EmitSelectOneByKeyEager(source, symbol, parameters, entityType, cancellation, firstParameter, entity, relationChildEntities);
                break;

            case StoreOperation.SelectAllByField:
                AppendHeader(source, symbol, parameters, isAsync: false);
                source.AppendLine($"        return Inquiry.QueryAsync<{entityType}>(");
                source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(");
                source.AppendLine($"                _sqlSelectBy_{method.FieldColumn!.PropertyName},");
                source.AppendLine("                new global::Inquiry.Parameters.InquiryParameter[]");
                source.AppendLine("                {");
                source.AppendLine($"                    new global::Inquiry.Parameters.InquiryParameter(\"value\", {firstParameter}),");
                source.AppendLine("                }),");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.Insert:
                AppendHeader(source, symbol, parameters, isAsync: false);
                if (method.ReturnsEntity)
                {
                    source.AppendLine($"        return Inquiry.QuerySingleOrDefaultAsync<{entityType}>(");
                    AppendMutationCommand(source, "_sqlInsertReturning", entity, firstParameter, indent: "            ");
                    source.AppendLine($"            {cancellation});");
                }
                else
                {
                    source.AppendLine("        return Inquiry.ExecuteAsync(");
                    AppendMutationCommand(source, "_sqlInsert", entity, firstParameter, indent: "            ");
                    source.AppendLine($"            {cancellation});");
                }
                source.AppendLine("    }");
                break;

            case StoreOperation.Update:
                AppendHeader(source, symbol, parameters, isAsync: true);
                if (method.ReturnsEntity)
                {
                    source.AppendLine($"        return await Inquiry.QuerySingleOrDefaultAsync<{entityType}>(");
                    AppendMutationCommand(source, "_sqlUpdateReturning", entity, firstParameter, indent: "            ", includeKey: true);
                    source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
                }
                else
                {
                    source.AppendLine("        return await Inquiry.ExecuteAsync(");
                    AppendMutationCommand(source, "_sqlUpdate", entity, firstParameter, indent: "            ", includeKey: true);
                    source.AppendLine($"            {cancellation}).ConfigureAwait(false) > 0;");
                }
                source.AppendLine("    }");
                break;

            case StoreOperation.Upsert:
                AppendHeader(source, symbol, parameters, isAsync: false);
                if (method.ReturnsEntity)
                {
                    source.AppendLine($"        return Inquiry.QuerySingleOrDefaultAsync<{entityType}>(");
                    AppendMutationCommand(source, "_sqlUpsertReturning", entity, firstParameter, indent: "            ");
                    source.AppendLine($"            {cancellation});");
                }
                else
                {
                    source.AppendLine("        return Inquiry.ExecuteAsync(");
                    AppendMutationCommand(source, "_sqlUpsert", entity, firstParameter, indent: "            ");
                    source.AppendLine($"            {cancellation});");
                }
                source.AppendLine("    }");
                break;

            case StoreOperation.DeleteOneByKey:
                AppendHeader(source, symbol, parameters, isAsync: true);
                source.AppendLine("        return await Inquiry.ExecuteAsync(");
                source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(");
                source.AppendLine("                _sqlDeleteByKey,");
                source.AppendLine("                new global::Inquiry.Parameters.InquiryParameter[]");
                source.AppendLine("                {");
                source.AppendLine($"                    new global::Inquiry.Parameters.InquiryParameter(\"key\", {firstParameter}),");
                source.AppendLine("                }),");
                source.AppendLine($"            {cancellation}).ConfigureAwait(false) > 0;");
                source.AppendLine("    }");
                break;

            case StoreOperation.StoredProcedure:
                EmitStoredProcedure(source, symbol, parameters, entityType, cancellation, method.ProcedureName!, entity);
                break;
        }
    }

    // ---- Private emit helpers ----

    private static void AppendMutationCommand(
        StringBuilder source,
        string sqlField,
        EntityModel entity,
        string entityParameter,
        string indent,
        bool includeKey = false)
    {
        source.AppendLine($"{indent}new global::Inquiry.Commands.InquiryCommand(");
        source.AppendLine($"{indent}    {sqlField},");
        source.AppendLine($"{indent}    new global::Inquiry.Parameters.InquiryParameter[]");
        source.AppendLine($"{indent}    {{");

        foreach (var column in entity.Columns.Where(c => includeKey ? c.IsKey || !c.IsGenerated : !c.IsGenerated))
        {
            source.AppendLine($"{indent}        new global::Inquiry.Parameters.InquiryParameter(\"{GeneratorHelpers.Escape(column.PropertyName)}\", {entityParameter}.{column.PropertyName}),");
        }

        source.AppendLine($"{indent}    }}),");
    }

    private static void EmitStoredProcedure(StringBuilder source, IMethodSymbol symbol, string parameters, string entityType, string cancellation, string procedureName, EntityModel entity)
    {
        // Build parameter array from method parameters (all except trailing CancellationToken)
        var procParams = symbol.Parameters.Take(symbol.Parameters.Length - 1).ToArray();
        var returnType = symbol.ReturnType;

        var isAsyncEnum = IsAsyncEnumerable(returnType, out _);
        var isTask = returnType.Name == "Task" && returnType is INamedTypeSymbol { TypeArguments.Length: 1 };
        var isAsync = !isAsyncEnum;

        AppendHeader(source, symbol, parameters, isAsync: isAsync);

        source.AppendLine("        var _cmd = new global::Inquiry.Commands.InquiryCommand(");
        source.AppendLine($"            \"{GeneratorHelpers.Escape(procedureName)}\",");

        if (procParams.Length > 0)
        {
            source.AppendLine("            new global::Inquiry.Parameters.InquiryParameter[]");
            source.AppendLine("            {");
            foreach (var p in procParams)
            {
                source.AppendLine($"                new global::Inquiry.Parameters.InquiryParameter(\"{GeneratorHelpers.Escape(p.Name)}\", (object?){p.Name} ?? global::System.DBNull.Value),");
            }
            source.AppendLine("            },");
        }
        else
        {
            source.AppendLine("            global::System.Array.Empty<global::Inquiry.Parameters.InquiryParameter>(),");
        }

        source.AppendLine("            global::System.Data.CommandType.StoredProcedure);");

        if (isAsyncEnum)
        {
            source.AppendLine($"        return Inquiry.QueryAsync<{entityType}>(_cmd, {cancellation});");
        }
        else if (isTask && symbol.ReturnType is INamedTypeSymbol taskType)
        {
            var inner = taskType.TypeArguments[0];
            if (SymbolEqualityComparer.Default.Equals(inner, entity.Symbol))
            {
                source.AppendLine($"        return await Inquiry.QuerySingleOrDefaultAsync<{entityType}>(_cmd, {cancellation}).ConfigureAwait(false);");
            }
            else
            {
                // Task<int>
                source.AppendLine($"        return await Inquiry.ExecuteAsync(_cmd, {cancellation}).ConfigureAwait(false);");
            }
        }

        source.AppendLine("    }");
    }

    private static void EmitSelectOneByKeyEager(StringBuilder source, IMethodSymbol symbol, string parameters, string entityType, string cancellation, string firstParam, EntityModel entity, Dictionary<string, EntityModel> relationChildEntities)
    {
        AppendHeader(source, symbol, parameters, isAsync: true);
        source.AppendLine($"        var _entity = await Inquiry.QuerySingleOrDefaultAsync<{entityType}>(");
        source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(");
        source.AppendLine("                _sqlSelectByKey,");
        source.AppendLine("                new global::Inquiry.Parameters.InquiryParameter[]");
        source.AppendLine("                {");
        source.AppendLine($"                    new global::Inquiry.Parameters.InquiryParameter(\"key\", {firstParam}),");
        source.AppendLine("                }),");
        source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
        source.AppendLine("        if (_entity is not null)");
        source.AppendLine("        {");
        foreach (var relation in entity.Relations)
        {
            var childType = relation.ChildEntitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var fieldName = $"_sql_{relation.PropertyName}";
            if (relation.IsCollection)
            {
                // One-to-many: load children filtered by parent's key.
                source.AppendLine($"            var _{relation.PropertyName}_list = new global::System.Collections.Generic.List<{childType}>();");
                source.AppendLine($"            await foreach (var _child in Inquiry.QueryAsync<{childType}>(");
                source.AppendLine("                new global::Inquiry.Commands.InquiryCommand(");
                source.AppendLine($"                    {fieldName},");
                source.AppendLine("                    new global::Inquiry.Parameters.InquiryParameter[]");
                source.AppendLine("                    {");
                source.AppendLine($"                        new global::Inquiry.Parameters.InquiryParameter(\"value\", _entity.{entity.Key.PropertyName}),");
                source.AppendLine("                    }),");
                source.AppendLine($"                {cancellation}).ConfigureAwait(false))");
                source.AppendLine($"                _{relation.PropertyName}_list.Add(_child);");
                source.AppendLine($"            _entity.{relation.PropertyName} = _{relation.PropertyName}_list;");
            }
            else
            {
                // Many-to-one: load single parent using the current entity's FK value.
                source.AppendLine($"            _entity.{relation.PropertyName} = await Inquiry.QuerySingleOrDefaultAsync<{childType}>(");
                source.AppendLine("                new global::Inquiry.Commands.InquiryCommand(");
                source.AppendLine($"                    {fieldName},");
                source.AppendLine("                    new global::Inquiry.Parameters.InquiryParameter[]");
                source.AppendLine("                    {");
                source.AppendLine($"                        new global::Inquiry.Parameters.InquiryParameter(\"value\", _entity.{relation.ForeignKeyProperty}),");
                source.AppendLine("                    }),");
                source.AppendLine($"                {cancellation}).ConfigureAwait(false);");
            }
        }
        source.AppendLine("        }");
        source.AppendLine("        return _entity;");
        source.AppendLine("    }");
    }

    private static void EmitSelectAllEager(StringBuilder source, IMethodSymbol symbol, string parameters, string entityType, string cancellation, EntityModel entity, Dictionary<string, EntityModel> relationChildEntities)
    {
        var parametersWithAttr = GeneratorHelpers.GetParameterDeclaration(symbol, enumeratorCancellation: true);
        AppendHeader(source, symbol, parametersWithAttr, isAsync: true);
        source.AppendLine($"        var _entities = new global::System.Collections.Generic.List<{entityType}>();");
        source.AppendLine($"        await foreach (var _e in Inquiry.QueryAsync<{entityType}>(_sqlSelectAll, {cancellation}).ConfigureAwait(false))");
        source.AppendLine("            _entities.Add(_e);");
        source.AppendLine("        if (_entities.Count == 0)");
        source.AppendLine("            yield break;");
        source.AppendLine();

        foreach (var relation in entity.Relations)
        {
            var childType = relation.ChildEntitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var fieldName = $"_sql_{relation.PropertyName}";

            if (relation.IsCollection)
            {
                // One-to-many: load all children, group by their FK value.
                source.AppendLine($"        var _allChildren_{relation.PropertyName} = new global::System.Collections.Generic.List<{childType}>();");
                source.AppendLine($"        await foreach (var _c in Inquiry.QueryAsync<{childType}>({fieldName}_All, {cancellation}).ConfigureAwait(false))");
                source.AppendLine($"            _allChildren_{relation.PropertyName}.Add(_c);");
                source.AppendLine($"        var _grouped_{relation.PropertyName} = new global::System.Collections.Generic.Dictionary<object, global::System.Collections.Generic.List<{childType}>>();");
                source.AppendLine($"        foreach (var _c in _allChildren_{relation.PropertyName})");
                source.AppendLine("        {");
                source.AppendLine($"            var _fkVal = (object)_c.{relation.ForeignKeyProperty};");
                source.AppendLine($"            if (!_grouped_{relation.PropertyName}.TryGetValue(_fkVal, out var _grp))");
                source.AppendLine("            {");
                source.AppendLine($"                _grp = new global::System.Collections.Generic.List<{childType}>();");
                source.AppendLine($"                _grouped_{relation.PropertyName}[_fkVal] = _grp;");
                source.AppendLine("            }");
                source.AppendLine("            _grp.Add(_c);");
                source.AppendLine("        }");
            }
            else
            {
                // Many-to-one: load all parents into a dict keyed by parent key.
                var relatedKeyProperty = relationChildEntities[relation.PropertyName].Key.PropertyName;
                source.AppendLine($"        var _parents_{relation.PropertyName} = new global::System.Collections.Generic.Dictionary<object, {childType}>();");
                source.AppendLine($"        await foreach (var _p in Inquiry.QueryAsync<{childType}>({fieldName}_All, {cancellation}).ConfigureAwait(false))");
                source.AppendLine($"            _parents_{relation.PropertyName}[(object)_p.{relatedKeyProperty}] = _p;");
            }
        }

        source.AppendLine("        foreach (var _entity in _entities)");
        source.AppendLine("        {");
        foreach (var relation in entity.Relations)
        {
            var childType = relation.ChildEntitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (relation.IsCollection)
            {
                source.AppendLine($"            _entity.{relation.PropertyName} = _grouped_{relation.PropertyName}.TryGetValue((object)_entity.{entity.Key.PropertyName}, out var _rel_{relation.PropertyName}) ? _rel_{relation.PropertyName} : new global::System.Collections.Generic.List<{childType}>();");
            }
            else
            {
                source.AppendLine($"            _entity.{relation.PropertyName} = _parents_{relation.PropertyName}.TryGetValue((object)_entity.{relation.ForeignKeyProperty}, out var _rel_{relation.PropertyName}) ? _rel_{relation.PropertyName} : null;");
            }
        }
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        foreach (var _entity in _entities)");
        source.AppendLine("            yield return _entity;");
        source.AppendLine("    }");
    }

    private static void AppendHeader(StringBuilder source, IMethodSymbol method, string parameters, bool isAsync)
    {
        var returnType = method.ReturnType.ToDisplayString(KnownSymbols.FullyQualifiedNullableFormat);
        var asyncModifier = isAsync ? "async " : string.Empty;
        source.AppendLine($"    public override {asyncModifier}{returnType} {method.Name}({parameters})");
        source.AppendLine("    {");
    }

    private static bool HasSupportedReturnType(StoreOperation operation, ITypeSymbol returnType, EntityModel entity, bool returnsEntity)
    {
        return operation switch
        {
            StoreOperation.SelectAll or StoreOperation.SelectAllEager or StoreOperation.SelectAllByField =>
                GeneratorHelpers.IsGenericType(returnType, "System.Collections.Generic.IAsyncEnumerable<T>", entity.Symbol),
            StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", entity.Symbol),
            StoreOperation.Insert or StoreOperation.Upsert when returnsEntity =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", entity.Symbol),
            StoreOperation.Insert or StoreOperation.Upsert =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Int32),
            StoreOperation.Update when returnsEntity =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", entity.Symbol),
            StoreOperation.Update or StoreOperation.DeleteOneByKey =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Boolean),
            StoreOperation.StoredProcedure =>
                IsValidStoredProcReturnType(returnType, entity),
            _ => false,
        };
    }

    private static bool IsValidStoredProcReturnType(ITypeSymbol returnType, EntityModel entity)
    {
        // IAsyncEnumerable<TEntity>
        if (GeneratorHelpers.IsGenericType(returnType, "System.Collections.Generic.IAsyncEnumerable<T>", entity.Symbol))
            return true;
        // Task<TEntity?>
        if (GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", entity.Symbol))
            return true;
        // Task<int>
        if (GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Int32))
            return true;
        return false;
    }

    private static bool HasSupportedParameters(IMethodSymbol method, StoreOperation operation, EntityModel entity, ColumnModel? fieldColumn)
    {
        if (method.Parameters.Length == 0 || !GeneratorHelpers.IsCancellationToken(method.Parameters[method.Parameters.Length - 1].Type))
        {
            return false;
        }

        return operation switch
        {
            StoreOperation.SelectAll or StoreOperation.SelectAllEager =>
                method.Parameters.Length == 1,
            StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager or StoreOperation.DeleteOneByKey =>
                method.Parameters.Length == 2 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, entity.Key.Type.Symbol),
            StoreOperation.SelectAllByField =>
                method.Parameters.Length == 2 &&
                fieldColumn is not null &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, fieldColumn.Type.Symbol),
            StoreOperation.Insert or StoreOperation.Update or StoreOperation.Upsert =>
                method.Parameters.Length == 2 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, entity.Symbol),
            StoreOperation.StoredProcedure =>
                true, // any parameters allowed
            _ => false,
        };
    }

    private static bool IsAsyncEnumerable(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        elementType = null;
        if (type is INamedTypeSymbol { IsGenericType: true } named &&
            named.TypeArguments.Length == 1 &&
            named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                == "global::System.Collections.Generic.IAsyncEnumerable<T>")
        {
            elementType = named.TypeArguments[0];
            return true;
        }
        return false;
    }
}
