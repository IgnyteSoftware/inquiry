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

        List<ColumnModel>? fieldColumns = null;
        if (operation == StoreOperation.SelectAllByField)
        {
            var selectedFields = GeneratorHelpers.GetConstructorStringArray(attribute);
            if (selectedFields is null || selectedFields.Length == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InquiryDiagnosticDescriptors.UnknownField,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    "<none>"));
                return null;
            }

            fieldColumns = new List<ColumnModel>(selectedFields.Length);
            foreach (var selectedField in selectedFields)
            {
                var resolved = entity.Columns.FirstOrDefault(c =>
                    string.Equals(c.PropertyName, selectedField, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.ColumnName, selectedField, StringComparison.OrdinalIgnoreCase));

                if (resolved is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.UnknownField,
                        method.Locations.FirstOrDefault(),
                        method.Name,
                        selectedField));
                    return null;
                }
                fieldColumns.Add(resolved);
            }
        }

        if (operation is StoreOperation.SelectAllEager or StoreOperation.SelectOneByKeyEager &&
            entity.Keys.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.EagerLoadingOnCompositeKeyParent,
                method.Locations.FirstOrDefault(),
                method.Name,
                entity.Symbol.Name));
            return null;
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

        if (!HasSupportedParameters(method, operation, entity, fieldColumns))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.InvalidParameters,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        // SelectAll-style methods may return either IAsyncEnumerable<T> (streaming) or
        // Task<IReadOnlyList<T>> (buffered). The buffered path is emitted as a tight
        // QueryListAsync call with no IAsyncEnumerable state machine.
        var returnsList = operation is StoreOperation.SelectAll or StoreOperation.SelectAllByField &&
            IsTaskOfReadOnlyList(method.ReturnType, entity.Symbol);

        return new StoreMethodModel(method, operation, fieldColumns, procedureName, returnsEntity, returnsList);
    }

    public static void Emit(StringBuilder source, StoreMethodModel method, EntityModel entity, Dictionary<string, EntityModel> relationChildEntities)
    {
        var symbol = method.Symbol;
        var entityType = entity.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var structMat = GeneratorHelpers.GetStructMaterializerFullName(entity.Symbol);
        var cancellation = symbol.Parameters[symbol.Parameters.Length - 1].Name;
        var firstParameter = symbol.Parameters.Length > 1 ? symbol.Parameters[0].Name : "entity";
        var parameters = GeneratorHelpers.GetParameterDeclaration(symbol);

        switch (method.Operation)
        {
            case StoreOperation.SelectAll:
                AppendHeader(source, symbol, parameters, isAsync: false);
                source.AppendLine(method.ReturnsList
                    ? $"        return Inquiry.QueryListAsync<{entityType}, {structMat}>("
                    : $"        return Inquiry.QueryAsync<{entityType}, {structMat}>(");
                source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(_sqlSelectAll),");
                source.AppendLine("            default,");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectAllEager:
                EmitSelectAllEager(source, symbol, parameters, entityType, cancellation, entity, relationChildEntities);
                break;

            case StoreOperation.SelectOneByKey:
                AppendHeader(source, symbol, parameters, isAsync: true);
                source.AppendLine($"        return await Inquiry.QuerySingleOrDefaultAsync<{entityType}, {structMat}>(");
                source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(");
                source.AppendLine("                _sqlSelectByKey,");
                AppendPositionalParameters(source, entity.Keys, symbol.Parameters, indent: "                ");
                source.AppendLine("            default,");
                source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectOneByKeyEager:
                EmitSelectOneByKeyEager(source, symbol, parameters, entityType, cancellation, entity, relationChildEntities);
                break;

            case StoreOperation.SelectAllByField:
                AppendHeader(source, symbol, parameters, isAsync: false);
                source.AppendLine(method.ReturnsList
                    ? $"        return Inquiry.QueryListAsync<{entityType}, {structMat}>("
                    : $"        return Inquiry.QueryAsync<{entityType}, {structMat}>(");
                source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(");
                source.AppendLine($"                _sqlSelectBy_{BuildFieldSuffix(method.FieldColumns)},");
                AppendPositionalParameters(source, method.FieldColumns, symbol.Parameters, indent: "                ");
                source.AppendLine("            default,");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.Insert:
                AppendHeader(source, symbol, parameters, isAsync: false);
                if (method.ReturnsEntity)
                {
                    source.AppendLine($"        return Inquiry.QuerySingleOrDefaultAsync<{entityType}, {structMat}>(");
                    AppendMutationCommand(source, "_sqlInsertReturning", entity, firstParameter, indent: "            ");
                    source.AppendLine("            default,");
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
                    source.AppendLine($"        return await Inquiry.QuerySingleOrDefaultAsync<{entityType}, {structMat}>(");
                    AppendMutationCommand(source, "_sqlUpdateReturning", entity, firstParameter, indent: "            ", includeKey: true);
                    source.AppendLine("            default,");
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
                    if (ShouldUseInsertWhenKeyIsNull(entity))
                    {
                        source.AppendLine($"        if ({firstParameter}.{entity.Key.PropertyName} is null)");
                        source.AppendLine("        {");
                        source.AppendLine($"            return Inquiry.QuerySingleOrDefaultAsync<{entityType}, {structMat}>(");
                        AppendMutationCommand(source, "_sqlInsertReturning", entity, firstParameter, indent: "                ");
                        source.AppendLine("                default,");
                        source.AppendLine($"                {cancellation});");
                        source.AppendLine("        }");
                        source.AppendLine();
                    }

                    source.AppendLine($"        return Inquiry.QuerySingleOrDefaultAsync<{entityType}, {structMat}>(");
                    AppendMutationCommand(source, "_sqlUpsertReturning", entity, firstParameter, indent: "            ", includeKey: true);
                    source.AppendLine("            default,");
                    source.AppendLine($"            {cancellation});");
                }
                else
                {
                    if (ShouldUseInsertWhenKeyIsNull(entity))
                    {
                        source.AppendLine($"        if ({firstParameter}.{entity.Key.PropertyName} is null)");
                        source.AppendLine("        {");
                        source.AppendLine("            return Inquiry.ExecuteAsync(");
                        AppendMutationCommand(source, "_sqlInsert", entity, firstParameter, indent: "                ");
                        source.AppendLine($"                {cancellation});");
                        source.AppendLine("        }");
                        source.AppendLine();
                    }

                    source.AppendLine("        return Inquiry.ExecuteAsync(");
                    AppendMutationCommand(source, "_sqlUpsert", entity, firstParameter, indent: "            ", includeKey: true);
                    source.AppendLine($"            {cancellation});");
                }
                source.AppendLine("    }");
                break;

            case StoreOperation.DeleteOneByKey:
                AppendHeader(source, symbol, parameters, isAsync: true);
                source.AppendLine("        return await Inquiry.ExecuteAsync(");
                source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(");
                source.AppendLine("                _sqlDeleteByKey,");
                AppendPositionalParameters(source, entity.Keys, symbol.Parameters, indent: "                ");
                source.AppendLine($"            {cancellation}).ConfigureAwait(false) > 0;");
                source.AppendLine("    }");
                break;

            case StoreOperation.StoredProcedure:
                EmitStoredProcedure(source, symbol, parameters, entityType, structMat, cancellation, method.ProcedureName!, entity);
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

        var parameterColumns = entity.Columns
            .Where(c => includeKey ? c.IsKey || !c.IsGenerated : !c.IsGenerated && !c.UseDatabaseDefault)
            .ToArray();

        if (parameterColumns.Length == 0)
        {
            source.AppendLine($"{indent}    global::System.Array.Empty<global::Inquiry.Parameters.InquiryParameter>()),");
            return;
        }

        source.AppendLine($"{indent}    new global::Inquiry.Parameters.InquiryParameter[]");
        source.AppendLine($"{indent}    {{");

        foreach (var column in parameterColumns)
        {
            // '@'-prefixed names let InquiryParameterBinder.NormalizeName take its no-allocation
            // fast path. Hand-written callers can still pass bare names.
            source.AppendLine($"{indent}        new global::Inquiry.Parameters.InquiryParameter(\"@{GeneratorHelpers.Escape(column.PropertyName)}\", {entityParameter}.{column.PropertyName}),");
        }

        source.AppendLine($"{indent}    }}),");
    }

    private static void EmitStoredProcedure(StringBuilder source, IMethodSymbol symbol, string parameters, string entityType, string structMat, string cancellation, string procedureName, EntityModel entity)
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
                source.AppendLine($"                new global::Inquiry.Parameters.InquiryParameter(\"@{GeneratorHelpers.Escape(p.Name)}\", (object?){p.Name} ?? global::System.DBNull.Value),");
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
            source.AppendLine($"        return Inquiry.QueryAsync<{entityType}, {structMat}>(_cmd, default, {cancellation});");
        }
        else if (isTask && symbol.ReturnType is INamedTypeSymbol taskType)
        {
            var inner = taskType.TypeArguments[0];
            if (SymbolEqualityComparer.Default.Equals(inner, entity.Symbol))
            {
                source.AppendLine($"        return await Inquiry.QuerySingleOrDefaultAsync<{entityType}, {structMat}>(_cmd, default, {cancellation}).ConfigureAwait(false);");
            }
            else
            {
                // Task<int>
                source.AppendLine($"        return await Inquiry.ExecuteAsync(_cmd, {cancellation}).ConfigureAwait(false);");
            }
        }

        source.AppendLine("    }");
    }

    private static void EmitSelectOneByKeyEager(StringBuilder source, IMethodSymbol symbol, string parameters, string entityType, string cancellation, EntityModel entity, Dictionary<string, EntityModel> relationChildEntities)
    {
        // Eager-on-composite is rejected in Validate, so entity.Keys.Count == 1 here.
        var keyParamName = symbol.Parameters[0].Name;
        var parentStructMat = GeneratorHelpers.GetStructMaterializerFullName(entity.Symbol);
        AppendHeader(source, symbol, parameters, isAsync: true);
        source.AppendLine($"        var _entity = await Inquiry.QuerySingleOrDefaultAsync<{entityType}, {parentStructMat}>(");
        source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(");
        source.AppendLine("                _sqlSelectByKey,");
        source.AppendLine("                new global::Inquiry.Parameters.InquiryParameter[]");
        source.AppendLine("                {");
        source.AppendLine($"                    new global::Inquiry.Parameters.InquiryParameter(\"@{GeneratorHelpers.Escape(entity.Key.PropertyName)}\", {keyParamName}),");
        source.AppendLine("                }),");
        source.AppendLine("            default,");
        source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
        source.AppendLine("        if (_entity is not null)");
        source.AppendLine("        {");
        foreach (var relation in entity.Relations)
        {
            var childType = relation.ChildEntitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var childStructMat = GeneratorHelpers.GetStructMaterializerFullName(relation.ChildEntitySymbol);
            var fieldName = $"_sql_{relation.PropertyName}";
            if (relation.IsCollection)
            {
                // One-to-many: load children filtered by their FK column. The SQL parameter
                // name comes from the child's FK property name (which is relation.ForeignKeyProperty).
                source.AppendLine($"            var _{relation.PropertyName}_list = new global::System.Collections.Generic.List<{childType}>();");
                source.AppendLine($"            await foreach (var _child in Inquiry.QueryAsync<{childType}, {childStructMat}>(");
                source.AppendLine("                new global::Inquiry.Commands.InquiryCommand(");
                source.AppendLine($"                    {fieldName},");
                source.AppendLine("                    new global::Inquiry.Parameters.InquiryParameter[]");
                source.AppendLine("                    {");
                source.AppendLine($"                        new global::Inquiry.Parameters.InquiryParameter(\"@{GeneratorHelpers.Escape(relation.ForeignKeyProperty)}\", _entity.{entity.Key.PropertyName}),");
                source.AppendLine("                    }),");
                source.AppendLine("                default,");
                source.AppendLine($"                {cancellation}).ConfigureAwait(false))");
                source.AppendLine($"                _{relation.PropertyName}_list.Add(_child);");
                source.AppendLine($"            _entity.{relation.PropertyName} = _{relation.PropertyName}_list;");
            }
            else
            {
                // Many-to-one: load single parent filtered by the parent's key column. The SQL
                // parameter name comes from the parent (child entity in the relation's terms)
                // KEY property name.
                var parentKeyPropertyName = relationChildEntities[relation.PropertyName].Key.PropertyName;
                source.AppendLine($"            _entity.{relation.PropertyName} = await Inquiry.QuerySingleOrDefaultAsync<{childType}, {childStructMat}>(");
                source.AppendLine("                new global::Inquiry.Commands.InquiryCommand(");
                source.AppendLine($"                    {fieldName},");
                source.AppendLine("                    new global::Inquiry.Parameters.InquiryParameter[]");
                source.AppendLine("                    {");
                source.AppendLine($"                        new global::Inquiry.Parameters.InquiryParameter(\"@{GeneratorHelpers.Escape(parentKeyPropertyName)}\", _entity.{relation.ForeignKeyProperty}),");
                source.AppendLine("                    }),");
                source.AppendLine("                default,");
                source.AppendLine($"                {cancellation}).ConfigureAwait(false);");
            }
        }
        source.AppendLine("        }");
        source.AppendLine("        return _entity;");
        source.AppendLine("    }");
    }

    /// <summary>
    /// Emits a parameter list that pairs each of the supplied <paramref name="columns"/> with the
    /// matching positional method parameter (by index). Used by every operation whose SQL has
    /// N <c>WHERE col = @col</c> placeholders bound to N positional store-method arguments.
    /// </summary>
    private static void AppendPositionalParameters(
        StringBuilder source,
        IReadOnlyList<ColumnModel> columns,
        System.Collections.Immutable.ImmutableArray<IParameterSymbol> methodParameters,
        string indent)
    {
        source.AppendLine($"{indent}new global::Inquiry.Parameters.InquiryParameter[]");
        source.AppendLine($"{indent}{{");
        for (var i = 0; i < columns.Count; i++)
        {
            // '@'-prefix lets the binder skip its NormalizeName string concat.
            source.AppendLine($"{indent}    new global::Inquiry.Parameters.InquiryParameter(\"@{GeneratorHelpers.Escape(columns[i].PropertyName)}\", {methodParameters[i].Name}),");
        }
        source.AppendLine($"{indent}}}),");
    }

    /// <summary>
    /// Returns the suffix used in the generated SQL field name for a SelectByField operation
    /// (e.g. "CustomerID_EmployeeID" for a two-column filter). Single-column filters use the
    /// column's property name directly, matching the pre-multi-column naming.
    /// </summary>
    private static string BuildFieldSuffix(IReadOnlyList<ColumnModel> columns)
    {
        if (columns.Count == 1) return columns[0].PropertyName;
        return string.Join("_", columns.Select(c => c.PropertyName));
    }

    private static void EmitSelectAllEager(StringBuilder source, IMethodSymbol symbol, string parameters, string entityType, string cancellation, EntityModel entity, Dictionary<string, EntityModel> relationChildEntities)
    {
        var parametersWithAttr = GeneratorHelpers.GetParameterDeclaration(symbol, enumeratorCancellation: true);
        var parentStructMat = GeneratorHelpers.GetStructMaterializerFullName(entity.Symbol);
        AppendHeader(source, symbol, parametersWithAttr, isAsync: true);
        source.AppendLine($"        var _entities = new global::System.Collections.Generic.List<{entityType}>();");
        source.AppendLine($"        await foreach (var _e in Inquiry.QueryAsync<{entityType}, {parentStructMat}>(new global::Inquiry.Commands.InquiryCommand(_sqlSelectAll), default, {cancellation}).ConfigureAwait(false))");
        source.AppendLine("            _entities.Add(_e);");
        source.AppendLine("        if (_entities.Count == 0)");
        source.AppendLine("            yield break;");
        source.AppendLine();

        foreach (var relation in entity.Relations)
        {
            var childType = relation.ChildEntitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var fieldName = $"_sql_{relation.PropertyName}";

            var childStructMat = GeneratorHelpers.GetStructMaterializerFullName(relation.ChildEntitySymbol);
            if (relation.IsCollection)
            {
                // One-to-many: load all children, group by their FK value.
                // If the child FK is nullable, skip rows where the FK is null — those
                // children logically belong to no parent and must not bucket together.
                var childFkColumn = relationChildEntities[relation.PropertyName].Columns.FirstOrDefault(c => c.PropertyName == relation.ForeignKeyProperty);
                var childFkNullable = childFkColumn?.Type.IsNullable ?? false;

                source.AppendLine($"        var _allChildren_{relation.PropertyName} = new global::System.Collections.Generic.List<{childType}>();");
                source.AppendLine($"        await foreach (var _c in Inquiry.QueryAsync<{childType}, {childStructMat}>(new global::Inquiry.Commands.InquiryCommand({fieldName}_All), default, {cancellation}).ConfigureAwait(false))");
                source.AppendLine($"            _allChildren_{relation.PropertyName}.Add(_c);");
                source.AppendLine($"        var _grouped_{relation.PropertyName} = new global::System.Collections.Generic.Dictionary<object, global::System.Collections.Generic.List<{childType}>>();");
                source.AppendLine($"        foreach (var _c in _allChildren_{relation.PropertyName})");
                source.AppendLine("        {");
                if (childFkNullable)
                {
                    source.AppendLine($"            if (_c.{relation.ForeignKeyProperty} is null) continue;");
                    source.AppendLine($"            var _fkVal = (object)_c.{relation.ForeignKeyProperty}!;");
                }
                else
                {
                    source.AppendLine($"            var _fkVal = (object)_c.{relation.ForeignKeyProperty};");
                }
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
                // If the parent's key column is nullable (e.g. an IDENTITY surfaced as int?),
                // skip rows with a null key — they can never satisfy any FK reference.
                var childEntity = relationChildEntities[relation.PropertyName];
                var relatedKeyProperty = childEntity.Key.PropertyName;
                var childKeyNullable = childEntity.Key.Type.IsNullable;

                source.AppendLine($"        var _parents_{relation.PropertyName} = new global::System.Collections.Generic.Dictionary<object, {childType}>();");
                if (childKeyNullable)
                {
                    source.AppendLine($"        await foreach (var _p in Inquiry.QueryAsync<{childType}, {childStructMat}>(new global::Inquiry.Commands.InquiryCommand({fieldName}_All), default, {cancellation}).ConfigureAwait(false))");
                    source.AppendLine("        {");
                    source.AppendLine($"            if (_p.{relatedKeyProperty} is null) continue;");
                    source.AppendLine($"            _parents_{relation.PropertyName}[(object)_p.{relatedKeyProperty}!] = _p;");
                    source.AppendLine("        }");
                }
                else
                {
                    source.AppendLine($"        await foreach (var _p in Inquiry.QueryAsync<{childType}, {childStructMat}>(new global::Inquiry.Commands.InquiryCommand({fieldName}_All), default, {cancellation}).ConfigureAwait(false))");
                    source.AppendLine($"            _parents_{relation.PropertyName}[(object)_p.{relatedKeyProperty}] = _p;");
                }
            }
        }

        source.AppendLine("        foreach (var _entity in _entities)");
        source.AppendLine("        {");
        foreach (var relation in entity.Relations)
        {
            var childType = relation.ChildEntitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (relation.IsCollection)
            {
                // Short-circuit when the parent's own key is null — its children list
                // is empty by definition (no FK row could point at a null parent key).
                if (entity.Key.Type.IsNullable)
                {
                    source.AppendLine($"            _entity.{relation.PropertyName} = _entity.{entity.Key.PropertyName} is null");
                    source.AppendLine($"                ? new global::System.Collections.Generic.List<{childType}>()");
                    source.AppendLine($"                : (_grouped_{relation.PropertyName}.TryGetValue((object)_entity.{entity.Key.PropertyName}!, out var _rel_{relation.PropertyName}) ? _rel_{relation.PropertyName} : new global::System.Collections.Generic.List<{childType}>());");
                }
                else
                {
                    source.AppendLine($"            _entity.{relation.PropertyName} = _grouped_{relation.PropertyName}.TryGetValue((object)_entity.{entity.Key.PropertyName}, out var _rel_{relation.PropertyName}) ? _rel_{relation.PropertyName} : new global::System.Collections.Generic.List<{childType}>();");
                }
            }
            else
            {
                // Short-circuit when this entity's FK is null — orphan, no parent.
                var parentFkColumn = entity.Columns.FirstOrDefault(c => c.PropertyName == relation.ForeignKeyProperty);
                var parentFkNullable = parentFkColumn?.Type.IsNullable ?? false;
                if (parentFkNullable)
                {
                    source.AppendLine($"            _entity.{relation.PropertyName} = _entity.{relation.ForeignKeyProperty} is null");
                    source.AppendLine($"                ? null");
                    source.AppendLine($"                : (_parents_{relation.PropertyName}.TryGetValue((object)_entity.{relation.ForeignKeyProperty}!, out var _rel_{relation.PropertyName}) ? _rel_{relation.PropertyName} : null);");
                }
                else
                {
                    source.AppendLine($"            _entity.{relation.PropertyName} = _parents_{relation.PropertyName}.TryGetValue((object)_entity.{relation.ForeignKeyProperty}, out var _rel_{relation.PropertyName}) ? _rel_{relation.PropertyName} : null;");
                }
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

    private static bool ShouldUseInsertWhenKeyIsNull(EntityModel entity)
        => entity.Key.Type.IsNullable && (entity.Key.IsGenerated || entity.Key.UseDatabaseDefault);

    private static bool HasSupportedReturnType(StoreOperation operation, ITypeSymbol returnType, EntityModel entity, bool returnsEntity)
    {
        return operation switch
        {
            // SelectAll / SelectAllByField accept both streaming and buffered shapes; the choice
            // is made by the method's declared return type.
            StoreOperation.SelectAll or StoreOperation.SelectAllByField =>
                GeneratorHelpers.IsGenericType(returnType, "System.Collections.Generic.IAsyncEnumerable<T>", entity.Symbol) ||
                IsTaskOfReadOnlyList(returnType, entity.Symbol),
            StoreOperation.SelectAllEager =>
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

    /// <summary>
    /// Returns true if <paramref name="returnType"/> is <c>Task&lt;IReadOnlyList&lt;TEntity&gt;&gt;</c>.
    /// </summary>
    private static bool IsTaskOfReadOnlyList(ITypeSymbol returnType, ITypeSymbol entitySymbol)
    {
        if (returnType is not INamedTypeSymbol task ||
            !task.IsGenericType ||
            task.TypeArguments.Length != 1 ||
            task.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::System.Threading.Tasks.Task<TResult>")
        {
            return false;
        }

        if (task.TypeArguments[0] is not INamedTypeSymbol inner ||
            !inner.IsGenericType ||
            inner.TypeArguments.Length != 1 ||
            inner.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::System.Collections.Generic.IReadOnlyList<T>")
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(inner.TypeArguments[0], entitySymbol);
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

    private static bool HasSupportedParameters(IMethodSymbol method, StoreOperation operation, EntityModel entity, IReadOnlyList<ColumnModel>? fieldColumns)
    {
        if (method.Parameters.Length == 0 || !GeneratorHelpers.IsCancellationToken(method.Parameters[method.Parameters.Length - 1].Type))
        {
            return false;
        }

        // Count of "real" parameters (everything except the trailing CancellationToken).
        var nonCancellationCount = method.Parameters.Length - 1;

        return operation switch
        {
            StoreOperation.SelectAll or StoreOperation.SelectAllEager =>
                method.Parameters.Length == 1,
            StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager or StoreOperation.DeleteOneByKey =>
                MatchesPositionalColumns(method, nonCancellationCount, entity.Keys),
            StoreOperation.SelectAllByField =>
                fieldColumns is not null &&
                fieldColumns.Count > 0 &&
                MatchesPositionalColumns(method, nonCancellationCount, fieldColumns),
            StoreOperation.Insert or StoreOperation.Update or StoreOperation.Upsert =>
                method.Parameters.Length == 2 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, entity.Symbol),
            StoreOperation.StoredProcedure =>
                true, // any parameters allowed
            _ => false,
        };
    }

    private static bool MatchesPositionalColumns(IMethodSymbol method, int nonCancellationCount, IReadOnlyList<ColumnModel> columns)
    {
        if (nonCancellationCount != columns.Count) return false;
        for (var i = 0; i < columns.Count; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(method.Parameters[i].Type, columns[i].Type.Symbol))
            {
                return false;
            }
        }
        return true;
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
