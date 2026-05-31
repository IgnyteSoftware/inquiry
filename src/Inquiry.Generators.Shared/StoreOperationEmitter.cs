using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Inquiry.Generators;

/// <summary>
/// Emits the body of one generated store method from its value-equatable <see cref="StoreMethodData"/>
/// and the owning <see cref="EntityData"/>. Pure code generation — no symbols — so it runs in the
/// cached output stage. Operation mapping and validation live in <see cref="StoreProcessor"/>.
/// </summary>
internal static class StoreOperationEmitter
{
    public static void Emit(
        StringBuilder source,
        StoreMethodData method,
        IReadOnlyList<ColumnData> fieldColumns,
        ResolvedPredicatePlan? predicatePlan,
        EntityData entity,
        Dictionary<string, EntityData> relationChildEntities)
    {
        var entityType = entity.FullyQualifiedName;
        var structMat = entity.StructMaterializerFullName;
        var cancellation = method.Parameters[method.Parameters.Count - 1].Name;
        var firstParameter = method.Parameters.Count > 1 ? method.Parameters[0].Name : "entity";
        var parameters = GetParameterDeclaration(method.Parameters);

        switch (method.Operation)
        {
            case StoreOperation.SelectAll:
                AppendHeader(source, method, parameters, isAsync: false);
                source.AppendLine(method.ReturnsList
                    ? $"        return Inquiry.QueryListAsync<{entityType}, {structMat}>("
                    : $"        return Inquiry.QueryAsync<{entityType}, {structMat}>(");
                source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(_sqlSelectAll),");
                source.AppendLine("            default,");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectAllEager:
                EmitSelectAllEager(source, method, entityType, cancellation, entity, relationChildEntities);
                break;

            case StoreOperation.SelectOneByKey:
                AppendHeader(source, method, parameters, isAsync: true);
                EmitFastQuerySingleByKeys(source, entityType, structMat, "_sqlSelectByKey", entity.Keys, method.Parameters, cancellation, indent: "        ");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectOneByKeyEager:
                EmitSelectOneByKeyEager(source, method, parameters, entityType, cancellation, entity, relationChildEntities);
                break;

            case StoreOperation.SelectAllByField:
                AppendHeader(source, method, parameters, isAsync: false);
                if (method.ReturnsList)
                {
                    // Buffered: allocation-free fast path (static binder, no InquiryParameter[]).
                    EmitFastQueryListByFields(source, method.Parameters, fieldColumns, entityType, structMat, cancellation, indent: "        ");
                }
                else
                {
                    // Streaming IAsyncEnumerable: no fast streaming overload, keep the InquiryParameter[] path.
                    source.AppendLine($"        return Inquiry.QueryAsync<{entityType}, {structMat}>(");
                    source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(");
                    source.AppendLine($"                _sqlSelectBy_{BuildFieldSuffix(fieldColumns)},");
                    AppendPositionalParameters(source, fieldColumns, method.Parameters, indent: "                ");
                    source.AppendLine("            default,");
                    source.AppendLine($"            {cancellation});");
                }
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectAllByPredicate:
                AppendHeader(source, method, parameters, isAsync: false);
                EmitSelectAllByPredicate(source, method, predicatePlan!, entityType, structMat, cancellation);
                source.AppendLine("    }");
                break;

            case StoreOperation.Insert:
                AppendHeader(source, method, parameters, isAsync: false);
                if (method.ReturnsEntity)
                {
                    EmitFastQuerySingleFromEntity(source, "_sqlInsertReturning", entity, firstParameter, entityType, structMat, cancellation, indent: "        ", includeKey: false, isAwait: false);
                }
                else
                {
                    EmitFastExecuteFromEntity(source, "_sqlInsert", entity, firstParameter, entityType, cancellation, indent: "        ", includeKey: false, returnRowsAffectedAsBool: false);
                }
                source.AppendLine("    }");
                break;

            case StoreOperation.Update:
                AppendHeader(source, method, parameters, isAsync: true);
                if (method.ReturnsEntity)
                {
                    EmitFastQuerySingleFromEntity(source, "_sqlUpdateReturning", entity, firstParameter, entityType, structMat, cancellation, indent: "        ", includeKey: true, isAwait: true);
                }
                else
                {
                    EmitFastExecuteFromEntity(source, "_sqlUpdate", entity, firstParameter, entityType, cancellation, indent: "        ", includeKey: true, returnRowsAffectedAsBool: true);
                }
                source.AppendLine("    }");
                break;

            case StoreOperation.Upsert:
                AppendHeader(source, method, parameters, isAsync: false);
                if (method.ReturnsEntity)
                {
                    if (ShouldUseInsertWhenKeyIsNull(entity))
                    {
                        source.AppendLine($"        if ({firstParameter}.{entity.Keys[0].PropertyName} is null)");
                        source.AppendLine("        {");
                        EmitFastQuerySingleFromEntity(source, "_sqlInsertReturning", entity, firstParameter, entityType, structMat, cancellation, indent: "            ", includeKey: false, isAwait: false);
                        source.AppendLine("        }");
                        source.AppendLine();
                    }

                    EmitFastQuerySingleFromEntity(source, "_sqlUpsertReturning", entity, firstParameter, entityType, structMat, cancellation, indent: "        ", includeKey: true, isAwait: false);
                }
                else
                {
                    if (ShouldUseInsertWhenKeyIsNull(entity))
                    {
                        source.AppendLine($"        if ({firstParameter}.{entity.Keys[0].PropertyName} is null)");
                        source.AppendLine("        {");
                        EmitFastExecuteFromEntity(source, "_sqlInsert", entity, firstParameter, entityType, cancellation, indent: "            ", includeKey: false, returnRowsAffectedAsBool: false);
                        source.AppendLine("        }");
                        source.AppendLine();
                    }

                    EmitFastExecuteFromEntity(source, "_sqlUpsert", entity, firstParameter, entityType, cancellation, indent: "        ", includeKey: true, returnRowsAffectedAsBool: false);
                }
                source.AppendLine("    }");
                break;

            case StoreOperation.DeleteOneByKey:
                AppendHeader(source, method, parameters, isAsync: true);
                EmitFastExecuteFromKeys(source, "_sqlDeleteByKey", entity.Keys, method.Parameters, cancellation, indent: "        ");
                source.AppendLine("    }");
                break;

            case StoreOperation.StoredProcedure:
                EmitStoredProcedure(source, method, parameters, entityType, structMat, cancellation);
                break;
        }
    }

    // ---- Private emit helpers ----

    private static void EmitFastExecuteFromEntity(
        StringBuilder source,
        string sqlField,
        EntityData entity,
        string entityParameter,
        string entityType,
        string cancellation,
        string indent,
        bool includeKey,
        bool returnRowsAffectedAsBool)
    {
        var columns = SelectMutationColumns(entity, includeKey);

        var awaitPrefix = returnRowsAffectedAsBool ? "await " : string.Empty;
        var returnSuffix = returnRowsAffectedAsBool ? ".ConfigureAwait(false) > 0" : string.Empty;

        source.AppendLine($"{indent}return {awaitPrefix}Inquiry.ExecuteAsync(");
        source.AppendLine($"{indent}    {sqlField},");
        source.AppendLine($"{indent}    {entityParameter},");
        AppendBinderLambda(source, "_e", columns, i => $"_e.{columns[i].PropertyName}", indent + "    ");
        source.AppendLine($"{indent}    {cancellation}){returnSuffix};");
    }

    /// <summary>
    /// Builds the C# expression assigned to <c>DbParameter.Value</c>. Non-enum columns use a simple
    /// null-coalesce; enum columns coerce to the underlying integer type so providers that reject
    /// unmapped enums (e.g. Npgsql) see the same primitive value the reflection binder would.
    /// </summary>
    private static string BuildParameterValueExpression(ColumnData column, string accessor)
    {
        if (!column.Type.IsEnum)
        {
            return $"(object?){accessor} ?? global::System.DBNull.Value";
        }

        var underlying = column.Type.EnumUnderlyingSpecialType switch
        {
            SpecialType.System_Byte => "byte",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Int16 => "short",
            SpecialType.System_UInt16 => "ushort",
            SpecialType.System_Int32 => "int",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_Int64 => "long",
            SpecialType.System_UInt64 => "ulong",
            _ => "int",
        };

        return column.Type.IsNullable
            ? $"{accessor}.HasValue ? (object)({underlying}){accessor}.Value : global::System.DBNull.Value"
            : $"(object)({underlying}){accessor}";
    }

    private static void EmitFastExecuteFromKeys(
        StringBuilder source,
        string sqlField,
        EquatableArray<ColumnData> keyColumns,
        EquatableArray<ParameterData> methodParameters,
        string cancellation,
        string indent)
    {
        if (keyColumns.Count == 1)
        {
            var keyParamName = methodParameters[0].Name;
            source.AppendLine($"{indent}return await Inquiry.ExecuteAsync(");
            source.AppendLine($"{indent}    {sqlField},");
            source.AppendLine($"{indent}    {keyParamName},");
            AppendBinderLambda(source, "_key", keyColumns.AsImmutableArray(), _ => "_key", indent + "    ");
            source.AppendLine($"{indent}    {cancellation}).ConfigureAwait(false) > 0;");
            return;
        }

        var tupleArgs = string.Join(", ", Take(methodParameters, keyColumns.Count).Select(p => p.Name));
        source.AppendLine($"{indent}return await Inquiry.ExecuteAsync(");
        source.AppendLine($"{indent}    {sqlField},");
        source.AppendLine($"{indent}    ({tupleArgs}),");
        AppendBinderLambda(source, "_keys", keyColumns.AsImmutableArray(), i => $"_keys.Item{i + 1}", indent + "    ");
        source.AppendLine($"{indent}    {cancellation}).ConfigureAwait(false) > 0;");
    }

    /// <summary>
    /// Emits a <c>static (_cmd, &lt;lambdaParam&gt;) =&gt; { … }</c> binder that writes one
    /// <c>DbParameter</c> per column straight into the <c>DbCommand</c>. <paramref name="accessor"/>
    /// yields the value expression for column <c>i</c>.
    /// </summary>
    private static void AppendBinderLambda(
        StringBuilder source,
        string lambdaParam,
        IReadOnlyList<ColumnData> columns,
        Func<int, string> accessor,
        string indent)
    {
        source.AppendLine($"{indent}static (_cmd, {lambdaParam}) =>");
        source.AppendLine($"{indent}{{");
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            source.AppendLine($"{indent}    var _p{i} = _cmd.CreateParameter();");
            source.AppendLine($"{indent}    _p{i}.ParameterName = \"@{GeneratorHelpers.Escape(column.PropertyName)}\";");
            source.AppendLine($"{indent}    _p{i}.Value = {BuildParameterValueExpression(column, accessor(i))};");
            source.AppendLine($"{indent}    _cmd.Parameters.Add(_p{i});");
        }
        source.AppendLine($"{indent}}},");
    }

    private static void EmitFastQuerySingleByKeys(
        StringBuilder source,
        string entityType,
        string structMat,
        string sqlField,
        EquatableArray<ColumnData> keyColumns,
        EquatableArray<ParameterData> methodParameters,
        string cancellation,
        string indent)
    {
        if (keyColumns.Count == 1)
        {
            var keyParam = methodParameters[0];
            source.AppendLine($"{indent}return await Inquiry.QuerySingleOrDefaultAsync<{entityType}, {keyParam.TypeDisplay}, {structMat}>(");
            source.AppendLine($"{indent}    {sqlField},");
            source.AppendLine($"{indent}    {keyParam.Name},");
            AppendBinderLambda(source, "_key", keyColumns.AsImmutableArray(), _ => "_key", indent + "    ");
            source.AppendLine($"{indent}    default,");
            source.AppendLine($"{indent}    {cancellation}).ConfigureAwait(false);");
            return;
        }

        var tupleArgs = string.Join(", ", Take(methodParameters, keyColumns.Count).Select(p => p.Name));
        var tupleType = "(" + string.Join(", ", Take(methodParameters, keyColumns.Count).Select(p => p.TypeDisplay)) + ")";
        source.AppendLine($"{indent}return await Inquiry.QuerySingleOrDefaultAsync<{entityType}, {tupleType}, {structMat}>(");
        source.AppendLine($"{indent}    {sqlField},");
        source.AppendLine($"{indent}    ({tupleArgs}),");
        AppendBinderLambda(source, "_keys", keyColumns.AsImmutableArray(), i => $"_keys.Item{i + 1}", indent + "    ");
        source.AppendLine($"{indent}    default,");
        source.AppendLine($"{indent}    {cancellation}).ConfigureAwait(false);");
    }

    private static void EmitFastQueryListByFields(
        StringBuilder source,
        EquatableArray<ParameterData> methodParameters,
        IReadOnlyList<ColumnData> fieldColumns,
        string entityType,
        string structMat,
        string cancellation,
        string indent)
    {
        var sqlField = "_sqlSelectBy_" + BuildFieldSuffix(fieldColumns);
        if (fieldColumns.Count == 1)
        {
            var fieldParam = methodParameters[0];
            source.AppendLine($"{indent}return Inquiry.QueryListAsync<{entityType}, {fieldParam.TypeDisplay}, {structMat}>(");
            source.AppendLine($"{indent}    {sqlField},");
            source.AppendLine($"{indent}    {fieldParam.Name},");
            AppendBinderLambda(source, "_arg", fieldColumns, _ => "_arg", indent + "    ");
            source.AppendLine($"{indent}    default,");
            source.AppendLine($"{indent}    {cancellation});");
            return;
        }

        var tupleArgs = string.Join(", ", Take(methodParameters, fieldColumns.Count).Select(p => p.Name));
        var tupleType = "(" + string.Join(", ", Take(methodParameters, fieldColumns.Count).Select(p => p.TypeDisplay)) + ")";
        source.AppendLine($"{indent}return Inquiry.QueryListAsync<{entityType}, {tupleType}, {structMat}>(");
        source.AppendLine($"{indent}    {sqlField},");
        source.AppendLine($"{indent}    ({tupleArgs}),");
        AppendBinderLambda(source, "_args", fieldColumns, i => $"_args.Item{i + 1}", indent + "    ");
        source.AppendLine($"{indent}    default,");
        source.AppendLine($"{indent}    {cancellation});");
    }

    /// <summary>
    /// Emits a <c>SelectAllByPredicate</c> body. Predicate methods route through an
    /// <see cref="global::Inquiry.Commands.InquiryCommand"/> with a <c>DbCommandBinder</c> closure so a
    /// single path covers both scalar binding and the IN command-text rewrite (the binder runs after the
    /// pipeline assigns the command text, which is what lets <see cref="global::Inquiry.Parameters.InquiryInExpansion"/>
    /// expand the sentinel). Buffered methods use the list overload; streaming ones use QueryAsync.
    /// </summary>
    private static void EmitSelectAllByPredicate(
        StringBuilder source,
        StoreMethodData method,
        ResolvedPredicatePlan plan,
        string entityType,
        string structMat,
        string cancellation)
    {
        source.AppendLine("        var _cmd = new global::Inquiry.Commands.InquiryCommand(");
        source.AppendLine($"            _sqlPredicate_{method.Name},");
        source.AppendLine("            (global::System.Data.Common.DbCommand _c) =>");
        source.AppendLine("            {");
        for (var i = 0; i < plan.Bindings.Count; i++)
        {
            var binding = plan.Bindings[i];
            var arg = method.Parameters[binding.MethodParameterIndex].Name;
            if (binding.IsCollection)
            {
                source.AppendLine($"                global::Inquiry.Parameters.InquiryInExpansion.Expand(_c, \"{GeneratorHelpers.Escape(binding.SqlParameterName)}\", {arg});");
            }
            else
            {
                source.AppendLine($"                var _p{i} = _c.CreateParameter();");
                source.AppendLine($"                _p{i}.ParameterName = \"{GeneratorHelpers.Escape(binding.SqlParameterName)}\";");
                source.AppendLine($"                _p{i}.Value = {BuildParameterValueExpression(binding.Column, arg)};");
                source.AppendLine($"                _c.Parameters.Add(_p{i});");
            }
        }
        source.AppendLine("            });");

        if (method.ReturnsList)
        {
            source.AppendLine($"        return Inquiry.QueryListAsync<{entityType}, {structMat}>(_cmd, default, {cancellation});");
        }
        else
        {
            source.AppendLine($"        return Inquiry.QueryAsync<{entityType}, {structMat}>(_cmd, default, {cancellation});");
        }
    }

    private static void EmitFastQuerySingleFromEntity(
        StringBuilder source,
        string sqlField,
        EntityData entity,
        string entityParameter,
        string entityType,
        string structMat,
        string cancellation,
        string indent,
        bool includeKey,
        bool isAwait)
    {
        var columns = SelectMutationColumns(entity, includeKey);

        var awaitPrefix = isAwait ? "await " : string.Empty;
        var returnSuffix = isAwait ? ".ConfigureAwait(false)" : string.Empty;

        source.AppendLine($"{indent}return {awaitPrefix}Inquiry.QuerySingleOrDefaultAsync<{entityType}, {entityType}, {structMat}>(");
        source.AppendLine($"{indent}    {sqlField},");
        source.AppendLine($"{indent}    {entityParameter},");
        AppendBinderLambda(source, "_e", columns, i => $"_e.{columns[i].PropertyName}", indent + "    ");
        source.AppendLine($"{indent}    default,");
        source.AppendLine($"{indent}    {cancellation}){returnSuffix};");
    }

    private static void EmitStoredProcedure(StringBuilder source, StoreMethodData method, string parameters, string entityType, string structMat, string cancellation)
    {
        var procParams = Take(method.Parameters, method.Parameters.Count - 1).ToArray();
        var isAsyncEnum = method.ProcedureReturn == ProcedureReturnKind.AsyncEnumerableOfEntity;
        var isAsync = !isAsyncEnum;

        AppendHeader(source, method, parameters, isAsync: isAsync);

        source.AppendLine("        var _cmd = new global::Inquiry.Commands.InquiryCommand(");
        source.AppendLine($"            \"{GeneratorHelpers.Escape(method.ProcedureName!)}\",");

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

        switch (method.ProcedureReturn)
        {
            case ProcedureReturnKind.AsyncEnumerableOfEntity:
                source.AppendLine($"        return Inquiry.QueryAsync<{entityType}, {structMat}>(_cmd, default, {cancellation});");
                break;
            case ProcedureReturnKind.TaskOfEntity:
                source.AppendLine($"        return await Inquiry.QuerySingleOrDefaultAsync<{entityType}, {structMat}>(_cmd, default, {cancellation}).ConfigureAwait(false);");
                break;
            case ProcedureReturnKind.TaskOfInt:
                source.AppendLine($"        return await Inquiry.ExecuteAsync(_cmd, {cancellation}).ConfigureAwait(false);");
                break;
        }

        source.AppendLine("    }");
    }

    private static void AppendPositionalParameters(
        StringBuilder source,
        IReadOnlyList<ColumnData> columns,
        EquatableArray<ParameterData> methodParameters,
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
    /// column's property name directly.
    /// </summary>
    public static string BuildFieldSuffix(IReadOnlyList<ColumnData> columns)
    {
        if (columns.Count == 1) return columns[0].PropertyName;
        return string.Join("_", columns.Select(c => c.PropertyName));
    }

    private static string NonNullableValueExpression(TypeData type, string accessor)
    {
        if (!type.IsNullable) return accessor;
        return type.IsValueType ? $"{accessor}.Value" : $"{accessor}!";
    }

    private static void EmitSelectOneByKeyEager(StringBuilder source, StoreMethodData method, string parameters, string entityType, string cancellation, EntityData entity, Dictionary<string, EntityData> relationChildEntities)
    {
        // Eager-on-composite is rejected in validation, so entity.Keys.Count == 1 here.
        var keyParamName = method.Parameters[0].Name;
        var parentStructMat = entity.StructMaterializerFullName;
        AppendHeader(source, method, parameters, isAsync: true);
        source.AppendLine($"        var _entity = await Inquiry.QuerySingleOrDefaultAsync<{entityType}, {parentStructMat}>(");
        source.AppendLine("            new global::Inquiry.Commands.InquiryCommand(");
        source.AppendLine("                _sqlSelectByKey,");
        source.AppendLine("                new global::Inquiry.Parameters.InquiryParameter[]");
        source.AppendLine("                {");
        source.AppendLine($"                    new global::Inquiry.Parameters.InquiryParameter(\"@{GeneratorHelpers.Escape(entity.Keys[0].PropertyName)}\", {keyParamName}),");
        source.AppendLine("                }),");
        source.AppendLine("            default,");
        source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
        source.AppendLine("        if (_entity is not null)");
        source.AppendLine("        {");
        foreach (var relation in entity.Relations)
        {
            if (!relationChildEntities.TryGetValue(relation.PropertyName, out var childEntity)) continue;
            var childType = childEntity.FullyQualifiedName;
            var childStructMat = childEntity.StructMaterializerFullName;
            var fieldName = $"_sql_{relation.PropertyName}";
            if (relation.IsCollection)
            {
                source.AppendLine($"            var _{relation.PropertyName}_list = new global::System.Collections.Generic.List<{childType}>();");
                source.AppendLine($"            await foreach (var _child in Inquiry.QueryAsync<{childType}, {childStructMat}>(");
                source.AppendLine("                new global::Inquiry.Commands.InquiryCommand(");
                source.AppendLine($"                    {fieldName},");
                source.AppendLine("                    new global::Inquiry.Parameters.InquiryParameter[]");
                source.AppendLine("                    {");
                source.AppendLine($"                        new global::Inquiry.Parameters.InquiryParameter(\"@{GeneratorHelpers.Escape(relation.ForeignKeyProperty)}\", _entity.{entity.Keys[0].PropertyName}),");
                source.AppendLine("                    }),");
                source.AppendLine("                default,");
                source.AppendLine($"                {cancellation}).ConfigureAwait(false))");
                source.AppendLine($"                _{relation.PropertyName}_list.Add(_child);");
                source.AppendLine($"            _entity.{relation.PropertyName} = _{relation.PropertyName}_list;");
            }
            else
            {
                var parentKeyPropertyName = childEntity.Keys[0].PropertyName;
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

    private static void EmitSelectAllEager(StringBuilder source, StoreMethodData method, string entityType, string cancellation, EntityData entity, Dictionary<string, EntityData> relationChildEntities)
    {
        var parametersWithAttr = GetParameterDeclaration(method.Parameters, enumeratorCancellation: true);
        var parentStructMat = entity.StructMaterializerFullName;
        AppendHeader(source, method, parametersWithAttr, isAsync: true);
        source.AppendLine($"        var _entities = new global::System.Collections.Generic.List<{entityType}>();");
        source.AppendLine($"        await foreach (var _e in Inquiry.QueryAsync<{entityType}, {parentStructMat}>(new global::Inquiry.Commands.InquiryCommand(_sqlSelectAll), default, {cancellation}).ConfigureAwait(false))");
        source.AppendLine("            _entities.Add(_e);");
        source.AppendLine("        if (_entities.Count == 0)");
        source.AppendLine("            yield break;");
        source.AppendLine();

        foreach (var relation in entity.Relations)
        {
            if (!relationChildEntities.TryGetValue(relation.PropertyName, out var childEntity)) continue;
            var childType = childEntity.FullyQualifiedName;
            var fieldName = $"_sql_{relation.PropertyName}";
            var childStructMat = childEntity.StructMaterializerFullName;
            if (relation.IsCollection)
            {
                var childFkColumn = FindColumn(childEntity, relation.ForeignKeyProperty);
                var childFkNullable = childFkColumn?.Type.IsNullable ?? false;
                var fkKeyType = childFkColumn?.Type.NonNullableDisplayName ?? "object";

                source.AppendLine($"        var _allChildren_{relation.PropertyName} = new global::System.Collections.Generic.List<{childType}>();");
                source.AppendLine($"        await foreach (var _c in Inquiry.QueryAsync<{childType}, {childStructMat}>(new global::Inquiry.Commands.InquiryCommand({fieldName}_All), default, {cancellation}).ConfigureAwait(false))");
                source.AppendLine($"            _allChildren_{relation.PropertyName}.Add(_c);");
                source.AppendLine($"        var _grouped_{relation.PropertyName} = new global::System.Collections.Generic.Dictionary<{fkKeyType}, global::System.Collections.Generic.List<{childType}>>();");
                source.AppendLine($"        foreach (var _c in _allChildren_{relation.PropertyName})");
                source.AppendLine("        {");
                if (childFkNullable)
                {
                    source.AppendLine($"            if (_c.{relation.ForeignKeyProperty} is null) continue;");
                    source.AppendLine($"            var _fkVal = {NonNullableValueExpression(childFkColumn!.Type, $"_c.{relation.ForeignKeyProperty}")};");
                }
                else
                {
                    source.AppendLine($"            var _fkVal = _c.{relation.ForeignKeyProperty};");
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
                var relatedKeyProperty = childEntity.Keys[0].PropertyName;
                var childKeyNullable = childEntity.Keys[0].Type.IsNullable;
                var parentKeyType = childEntity.Keys[0].Type.NonNullableDisplayName;

                source.AppendLine($"        var _parents_{relation.PropertyName} = new global::System.Collections.Generic.Dictionary<{parentKeyType}, {childType}>();");
                if (childKeyNullable)
                {
                    source.AppendLine($"        await foreach (var _p in Inquiry.QueryAsync<{childType}, {childStructMat}>(new global::Inquiry.Commands.InquiryCommand({fieldName}_All), default, {cancellation}).ConfigureAwait(false))");
                    source.AppendLine("        {");
                    source.AppendLine($"            if (_p.{relatedKeyProperty} is null) continue;");
                    source.AppendLine($"            _parents_{relation.PropertyName}[{NonNullableValueExpression(childEntity.Keys[0].Type, $"_p.{relatedKeyProperty}")}] = _p;");
                    source.AppendLine("        }");
                }
                else
                {
                    source.AppendLine($"        await foreach (var _p in Inquiry.QueryAsync<{childType}, {childStructMat}>(new global::Inquiry.Commands.InquiryCommand({fieldName}_All), default, {cancellation}).ConfigureAwait(false))");
                    source.AppendLine($"            _parents_{relation.PropertyName}[_p.{relatedKeyProperty}] = _p;");
                }
            }
        }

        source.AppendLine("        foreach (var _entity in _entities)");
        source.AppendLine("        {");
        foreach (var relation in entity.Relations)
        {
            if (!relationChildEntities.TryGetValue(relation.PropertyName, out var childEntity)) continue;
            var childType = childEntity.FullyQualifiedName;
            if (relation.IsCollection)
            {
                if (entity.Keys[0].Type.IsNullable)
                {
                    source.AppendLine($"            _entity.{relation.PropertyName} = _entity.{entity.Keys[0].PropertyName} is null");
                    source.AppendLine($"                ? new global::System.Collections.Generic.List<{childType}>()");
                    source.AppendLine($"                : (_grouped_{relation.PropertyName}.TryGetValue({NonNullableValueExpression(entity.Keys[0].Type, $"_entity.{entity.Keys[0].PropertyName}")}, out var _rel_{relation.PropertyName}) ? _rel_{relation.PropertyName} : new global::System.Collections.Generic.List<{childType}>());");
                }
                else
                {
                    source.AppendLine($"            _entity.{relation.PropertyName} = _grouped_{relation.PropertyName}.TryGetValue(_entity.{entity.Keys[0].PropertyName}, out var _rel_{relation.PropertyName}) ? _rel_{relation.PropertyName} : new global::System.Collections.Generic.List<{childType}>();");
                }
            }
            else
            {
                var parentFkColumn = FindColumn(entity, relation.ForeignKeyProperty);
                var parentFkNullable = parentFkColumn?.Type.IsNullable ?? false;
                if (parentFkNullable)
                {
                    source.AppendLine($"            _entity.{relation.PropertyName} = _entity.{relation.ForeignKeyProperty} is null");
                    source.AppendLine($"                ? null");
                    source.AppendLine($"                : (_parents_{relation.PropertyName}.TryGetValue({NonNullableValueExpression(parentFkColumn!.Type, $"_entity.{relation.ForeignKeyProperty}")}, out var _rel_{relation.PropertyName}) ? _rel_{relation.PropertyName} : null);");
                }
                else
                {
                    source.AppendLine($"            _entity.{relation.PropertyName} = _parents_{relation.PropertyName}.TryGetValue(_entity.{relation.ForeignKeyProperty}, out var _rel_{relation.PropertyName}) ? _rel_{relation.PropertyName} : null;");
                }
            }
        }
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        foreach (var _entity in _entities)");
        source.AppendLine("            yield return _entity;");
        source.AppendLine("    }");
    }

    private static void AppendHeader(StringBuilder source, StoreMethodData method, string parameters, bool isAsync)
    {
        var asyncModifier = isAsync ? "async " : string.Empty;
        source.AppendLine($"    public {asyncModifier}partial {method.ReturnTypeDisplay} {method.Name}({parameters})");
        source.AppendLine("    {");
    }

    private static bool ShouldUseInsertWhenKeyIsNull(EntityData entity)
        => entity.Keys[0].Type.IsNullable && (entity.Keys[0].IsGenerated || entity.Keys[0].UseDatabaseDefault);

    private static string GetParameterDeclaration(EquatableArray<ParameterData> parameters, bool enumeratorCancellation = false)
    {
        // Defaults live on the user's partial declaration; the generator's implementation half
        // must not repeat them or CS1066 fires.
        var parts = new List<string>(parameters.Count);
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var isCt = i == parameters.Count - 1 && parameter.IsCancellationToken;
            var prefix = enumeratorCancellation && isCt
                ? "[global::System.Runtime.CompilerServices.EnumeratorCancellation] "
                : string.Empty;
            parts.Add($"{prefix}{parameter.TypeDisplay} {parameter.Name}");
        }

        return string.Join(", ", parts);
    }

    private static ColumnData[] SelectMutationColumns(EntityData entity, bool includeKey)
        => entity.Columns.AsImmutableArray()
            .Where(c => includeKey ? c.IsKey || !c.IsGenerated : !c.IsGenerated && !c.UseDatabaseDefault)
            .ToArray();

    private static ColumnData? FindColumn(EntityData entity, string propertyName)
    {
        foreach (var column in entity.Columns.AsImmutableArray())
        {
            if (column.PropertyName == propertyName)
            {
                return column;
            }
        }

        return null;
    }

    private static IEnumerable<ParameterData> Take(EquatableArray<ParameterData> parameters, int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return parameters[i];
        }
    }
}
