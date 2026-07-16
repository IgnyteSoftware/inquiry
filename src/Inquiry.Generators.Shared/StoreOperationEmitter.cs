using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
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
        ResolvedSelectPlan? selectPlan,
        EntityData entity,
        Dictionary<string, EntityData> relationChildEntities,
        Dictionary<string, EntityData> relationJunctionEntities,
        SqlBuilder sqlBuilder,
        CollectionParameterResolution? deleteAllCollectionResolution = null,
        string? baseSelectField = null,
        string? resultTypeOverride = null,
        string? structMatOverride = null,
        IReadOnlyDictionary<string, EntityData>? entities = null)
    {
        // a projection-returning select overrides the materialized result type and its struct
        // materializer; all other operations use the store's entity.
        var entityType = resultTypeOverride ?? entity.FullyQualifiedName;
        var structMat = structMatOverride ?? entity.StructMaterializerFullName;
        var cancellation = method.Parameters[method.Parameters.Count - 1].Name;
        var firstParameter = method.Parameters.Count > 1 ? method.Parameters[0].Name : "entity";
        var parameters = GetParameterDeclaration(method.Parameters);

        // SelectAllByField with a plan (ordered and/or offset-paged) and SelectAll that is offset-paged
        // route through a dedicated emitter (own SQL const + filter/offset/limit binder). Ordered-only
        // SelectAll has no parameters to bind, so it falls through to the shared SelectAll case below
        // (which references selectPlan.SqlFieldName).
        if (selectPlan is not null &&
            (selectPlan.Pagination == Pagination.Offset || method.Operation == StoreOperation.SelectAllByField))
        {
            AppendHeader(source, method, parameters, isAsync: method.ReturnsPagedResult);
            EmitOffsetPaged(source, sqlBuilder, method, fieldColumns, selectPlan, entityType, structMat, cancellation);
            source.AppendLine("    }");
            return;
        }

        switch (method.Operation)
        {
            case StoreOperation.KeysetPage:
                AppendHeader(source, method, parameters, isAsync: true);
                EmitKeysetPage(source, sqlBuilder, method, selectPlan!, entityType, structMat, cancellation);
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectAll:
                AppendHeader(source, method, parameters, isAsync: false);
                source.AppendLine(method.ReturnsList
                    ? $"        return Inquiry.QueryListAsync<{entityType}, byte, {structMat}>("
                    : $"        return Inquiry.QueryAsync<{entityType}, byte, {structMat}>(");
                source.AppendLine($"            {EmptyGeneratedCommand(selectPlan is not null ? selectPlan.SqlFieldName : baseSelectField ?? "_sqlSelectAll")},");
                source.AppendLine("            default,");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectAllEager:
                EmitSelectAllEager(source, sqlBuilder, method, entityType, cancellation, entity, relationChildEntities, relationJunctionEntities, baseSelectField ?? "_sqlSelectAll");
                break;

            case StoreOperation.SelectOneByKey:
                AppendHeader(source, method, parameters, isAsync: true);
                EmitFastQuerySingleByKeys(source, sqlBuilder, entityType, structMat, baseSelectField ?? "_sqlSelectByKey", entity.Keys, method.Parameters, cancellation, indent: "        ");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectOneByKeyEager:
                EmitSelectOneByKeyEager(source, sqlBuilder, method, parameters, entityType, cancellation, entity, relationChildEntities, baseSelectField ?? "_sqlSelectByKey");
                break;

            case StoreOperation.SelectAllByField:
                AppendHeader(source, method, parameters, isAsync: false);
                // Buffered and streaming both use the immutable generated-command path.
                EmitFastQueryByFields(source, sqlBuilder, method.Parameters, fieldColumns, entityType, structMat, cancellation, indent: "        ", sqlField: baseSelectField, returnsList: method.ReturnsList);
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectAllByPredicate:
                AppendHeader(source, method, parameters, isAsync: false);
                EmitSelectAllByPredicate(source, sqlBuilder, method, predicatePlan!, entityType, structMat, cancellation, entity.Schema);
                source.AppendLine("    }");
                break;

            case StoreOperation.Insert:
                AppendHeader(source, method, parameters, isAsync: false);
                EmitSequentialGuidAssignment(source, entity, firstParameter, indent: "        ", sqlBuilder);
                EmitAuditAssignments(source, entity, firstParameter, isInsert: true, indent: "        ");
                if (method.ReturnsEntity)
                {
                    EmitFastQuerySingleFromEntity(source, sqlBuilder, "_sqlInsertReturning", entity, firstParameter, entityType, structMat, cancellation, indent: "        ", includeKey: false, isAwait: false);
                }
                else
                {
                    EmitFastExecuteFromEntity(source, sqlBuilder, "_sqlInsert", entity, firstParameter, entityType, cancellation, indent: "        ", includeKey: false, returnRowsAffectedAsBool: false);
                }
                source.AppendLine("    }");
                break;

            case StoreOperation.Update:
                AppendHeader(source, method, parameters, isAsync: true);
                EmitModifiedAuditAssignment(source, entity, firstParameter, indent: "        ");
                if (method.ReturnsEntity)
                {
                    EmitFastQuerySingleFromEntity(source, sqlBuilder, "_sqlUpdateReturning", entity, firstParameter, entityType, structMat, cancellation, indent: "        ", includeKey: true, isAwait: true, emitConcurrencyGuard: true, forUpdate: true);
                }
                else
                {
                    EmitFastExecuteFromEntity(source, sqlBuilder, "_sqlUpdate", entity, firstParameter, entityType, cancellation, indent: "        ", includeKey: true, returnRowsAffectedAsBool: true, forUpdate: true);
                }
                source.AppendLine("    }");
                break;

            case StoreOperation.Upsert:
                AppendHeader(source, method, parameters, isAsync: false);
                // An unset SequentialGuid key gets a fresh sequential GUID before the upsert,
                // making it an insert of a new row — same "default key generation" semantics as Insert.
                EmitSequentialGuidAssignment(source, entity, firstParameter, indent: "        ", sqlBuilder);
                // CreatedAt only lands via the insert branch (the conflict-branch SET excludes it),
                // so the unset-stamp is correct for both outcomes; ModifiedAt is set on both.
                EmitAuditAssignments(source, entity, firstParameter, isInsert: true, indent: "        ");
                if (method.ReturnsEntity)
                {
                    if (ShouldUseInsertWhenKeyIsNull(entity))
                    {
                        source.AppendLine($"        if ({firstParameter}.{entity.Keys[0].PropertyName} is null)");
                        source.AppendLine("        {");
                        EmitFastQuerySingleFromEntity(source, sqlBuilder, "_sqlInsertReturning", entity, firstParameter, entityType, structMat, cancellation, indent: "            ", includeKey: false, isAwait: false);
                        source.AppendLine("        }");
                        source.AppendLine();
                    }

                    EmitFastQuerySingleFromEntity(source, sqlBuilder, "_sqlUpsertReturning", entity, firstParameter, entityType, structMat, cancellation, indent: "        ", includeKey: true, isAwait: false);
                }
                else
                {
                    if (ShouldUseInsertWhenKeyIsNull(entity))
                    {
                        source.AppendLine($"        if ({firstParameter}.{entity.Keys[0].PropertyName} is null)");
                        source.AppendLine("        {");
                        EmitFastExecuteFromEntity(source, sqlBuilder, "_sqlInsert", entity, firstParameter, entityType, cancellation, indent: "            ", includeKey: false, returnRowsAffectedAsBool: false);
                        source.AppendLine("        }");
                        source.AppendLine();
                    }

                    EmitFastExecuteFromEntity(source, sqlBuilder, "_sqlUpsert", entity, firstParameter, entityType, cancellation, indent: "        ", includeKey: true, returnRowsAffectedAsBool: false);
                }
                source.AppendLine("    }");
                break;

            case StoreOperation.DeleteOneByKey:
            {
                if (method.ReturnsEntity)
                {
                    var returningField = entity.SoftDeleteColumn is not null && !method.HardDelete
                        ? "_sqlSoftDeleteReturning"
                        : "_sqlDeleteReturning";
                    AppendHeader(source, method, parameters, isAsync: true);
                    if (entity.ConcurrencyToken is not null)
                    {
                        var deleteColumns = new List<ColumnData>(entity.Keys.AsImmutableArray()) { entity.ConcurrencyToken };
                        source.AppendLine($"        var _result = await Inquiry.QueryGeneratedSingleOrDefaultAsync<{entityType}, {entityType}, {structMat}>(");
                        source.AppendLine($"            new global::Inquiry.Commands.InquiryGeneratedCommand<{entityType}>(");
                        source.AppendLine($"                {returningField},");
                        source.AppendLine($"                {firstParameter},");
                        AppendBinderLambda(source, sqlBuilder, "_e", deleteColumns, i => $"_e.{deleteColumns[i].PropertyName}", "                ", emitSizePrecision: true, trailingComma: false);
                        source.AppendLine("            ),");
                        source.AppendLine("            default,");
                        source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
                        source.AppendLine("        if (_result is null && Inquiry.ThrowOnConcurrencyConflict) throw new global::Inquiry.InquiryConcurrencyException();");
                        source.AppendLine("        return _result;");
                    }
                    else
                    {
                        EmitFastQuerySingleByKeys(source, sqlBuilder, entityType, structMat, returningField, entity.Keys, method.Parameters, cancellation, indent: "        ");
                    }
                    source.AppendLine("    }");
                    break;
                }

                // the shared _sqlDeleteByKey is the soft UPDATE for a soft-delete entity (or the literal
                // DELETE otherwise). A HardDelete method on a soft-delete entity uses the separate literal
                // const. Either way it is a rows-affected ExecuteAsync, so binder/return are unchanged.
                var deleteField = method.HardDelete && entity.SoftDeleteColumn is not null
                    ? "_sqlHardDeleteByKey"
                    : "_sqlDeleteByKey";
                AppendHeader(source, method, parameters, isAsync: true);
                if (entity.ConcurrencyToken is not null)
                {
                    // a concurrency-checked DELETE takes the entity and binds the key + token (the
                    // DELETE WHERE references both); a 0-row result is a conflict and throws when the
                    // runtime option is set.
                    var deleteColumns = new List<ColumnData>(entity.Keys.AsImmutableArray()) { entity.ConcurrencyToken };
                    source.AppendLine("        var _rows = await Inquiry.ExecuteAsync(");
                    source.AppendLine($"            new global::Inquiry.Commands.InquiryGeneratedCommand<{entityType}>(");
                    source.AppendLine($"                {deleteField},");
                    source.AppendLine($"                {firstParameter},");
                    AppendBinderLambda(source, sqlBuilder, "_e", deleteColumns, i => $"_e.{deleteColumns[i].PropertyName}", "                ", emitSizePrecision: true, trailingComma: false);
                    source.AppendLine("            ),");
                    source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
                    AppendConcurrencyConflictGuard(source, "        ");
                    source.AppendLine("        return _rows > 0;");
                }
                else
                {
                    EmitFastExecuteFromKeys(source, sqlBuilder, deleteField, entity.Keys, method.Parameters, cancellation, indent: "        ");
                }
                source.AppendLine("    }");
                break;
            }

            case StoreOperation.RestoreOneByKey:
                AppendHeader(source, method, parameters, isAsync: true);
                EmitFastExecuteFromKeys(source, sqlBuilder, "_sqlRestoreByKey", entity.Keys, method.Parameters, cancellation, indent: "        ");
                source.AppendLine("    }");
                break;

            case StoreOperation.DeleteAll:
            {
                // batch delete over a key collection. On array dialects the (keys) placeholder binds
                // the whole collection as one native array parameter (constant SQL); elsewhere the
                // sentinel is expanded at runtime into one placeholder per key by InquiryInExpansion.
                // The bind/Expand name takes the dialect sigil (':keys' on Oracle, '@keys' elsewhere)
                // to match the baked SQL. Returns rows affected; for a soft-delete entity
                // _sqlDeleteAll is the soft UPDATE form.
                var keysExpression = NonNullBatchItemsExpression(method.Parameters[0]);
                var batchDescriptor = BuildBatchDescriptorFieldName(method);
                AppendHeader(source, method, parameters, isAsync: false);
                source.AppendLine($"        return Inquiry.ExecuteBatchAsync({batchDescriptor}, {keysExpression}, {cancellation});");
                source.AppendLine("    }");
                break;
            }

            case StoreOperation.Count:
                // COUNT(*) returns a scalar long via the runtime scalar path. No parameters to bind,
                // so the Task is returned directly (no async state machine).
                AppendHeader(source, method, parameters, isAsync: false);
                source.AppendLine($"        return Inquiry.ExecuteScalarAsync<long, byte>({EmptyGeneratedCommand("_sqlCount")}, {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.Exists:
                // EXISTS returns a 1/0 scalar the runtime coerces to bool; criteria (if any) bind through
                // the generated command's static binder.
                AppendHeader(source, method, parameters, isAsync: false);
                EmitExists(source, sqlBuilder, method, predicatePlan!, cancellation, entity.Schema);
                source.AppendLine("    }");
                break;

            case StoreOperation.Aggregate:
                // SUM/AVG/MIN/MAX returns the method's declared scalar type via the scalar path.
                AppendHeader(source, method, parameters, isAsync: false);
                source.AppendLine($"        return Inquiry.ExecuteScalarAsync<{method.ScalarResultType}, byte>({EmptyGeneratedCommand("_sqlAgg_" + method.Name)}, {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectTopByOrder:
                // Top-1-by-order: parameterless single-row select with ORDER BY + LIMIT 1.
                AppendHeader(source, method, parameters, isAsync: false);
                source.AppendLine($"        return Inquiry.QueryGeneratedSingleOrDefaultAsync<{entityType}, byte, {structMat}>(");
                source.AppendLine($"            {EmptyGeneratedCommand("_sqlTop_" + method.Name)},");
                source.AppendLine("            default,");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.GroupCount:
            {
                var groupColumn = entity.Columns.AsImmutableArray().First(column =>
                    string.Equals(column.PropertyName, method.GroupCountColumn, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(column.ColumnName, method.GroupCountColumn, StringComparison.OrdinalIgnoreCase));
                var keyType = method.GroupCountKeyTypeFqn!;
                var gcType = $"global::Inquiry.GroupCount<{keyType}>";
                var matName = "_GroupCountMat_" + method.Name;
                AppendHeader(source, method, parameters, isAsync: false);
                source.AppendLine($"        return Inquiry.QueryListAsync<{gcType}, byte, {matName}>(");
                source.AppendLine($"            {EmptyGeneratedCommand("_sqlGroupCount_" + method.Name)},");
                source.AppendLine("            default,");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                // Emit the inline struct materializer for this GroupCount method.
                source.AppendLine();
                source.AppendLine($"    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
                source.AppendLine($"    internal readonly struct {matName} : global::Inquiry.Materialization.IInquiryEntityMaterializer<{gcType}>");
                source.AppendLine("    {");
                source.AppendLine($"        public {gcType} Materialize(global::System.Data.Common.DbDataReader reader)");
                var keyRead = MaterializerEmitter.ReadExpression(groupColumn.Type, 0, sqlBuilder, groupColumn.EnumAsString, groupColumn.Converter);
                var countType = new TypeData(
                    "global::System.Int64", "global::System.Int64", SpecialType.System_Int64,
                    SpecialType.None, false, true, false, false);
                var countRead = MaterializerEmitter.ReadExpression(countType, 1, sqlBuilder, role: ReaderResultRole.Count);
                source.AppendLine($"            => new {gcType}({keyRead}, {countRead});");
                source.AppendLine("    }");
                break;
            }

            case StoreOperation.FullTextSearch:
            {
                // one string search-term parameter bound to @searchTerm; the SQL is the dialect's
                // full-text predicate over the searched columns.
                var searchArg = method.Parameters[0].Name;
                AppendHeader(source, method, parameters, isAsync: false);
                // Buffered and streaming both use the static-binder overload, which the built-in
                // pipeline lowers to an immutable generated command without captured state.
                var ftsQueryMethod = method.ReturnsList ? "QueryListAsync" : "QueryAsync";
                source.AppendLine($"        return Inquiry.{ftsQueryMethod}<{entityType}, string, {structMat}>(");
                source.AppendLine($"            _sqlFts_{method.Name},");
                source.AppendLine($"            {searchArg},");
                source.AppendLine("            static (_cmd, _arg) =>");
                source.AppendLine("            {");
                source.AppendLine("                var _p = _cmd.CreateParameter();");
                source.AppendLine($"                _p.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName("searchTerm"))}\";");
                source.AppendLine("                _p.DbType = global::System.Data.DbType.String;");
                source.AppendLine("                _p.Value = (object?)_arg ?? global::System.DBNull.Value;");
                source.AppendLine("                _cmd.Parameters.Add(_p);");
                source.AppendLine("            },");
                source.AppendLine("            default,");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                break;
            }

            case StoreOperation.BulkInsert when sqlBuilder.SupportsBulkCopy:
                EmitBulkInsert(source, method, parameters, entity, entityType, cancellation, sqlBuilder);
                break;

            case StoreOperation.BulkInsert:
                // No native bulk-copy API on this dialect — compile down to the batch-insert body.
                // The method's Task<long> return accepts the body's int expressions (implicit widening).
                goto case StoreOperation.InsertAll;

            case StoreOperation.InsertAll:
            {
                // The cached descriptor owns provider SQL and binding while the runtime streams bounded,
                // atomic chunks from the original enumerable.
                var itemsExpression = NonNullBatchItemsExpression(method.Parameters[0]);
                var batchDescriptor = BuildBatchDescriptorFieldName(method);
                if (method.Operation == StoreOperation.BulkInsert)
                {
                    AppendHeader(source, method, parameters, isAsync: true);
                    source.AppendLine($"        return await Inquiry.ExecuteBatchAsync({batchDescriptor}, {itemsExpression}, {cancellation}).ConfigureAwait(false);");
                }
                else
                {
                    AppendHeader(source, method, parameters, isAsync: false);
                    source.AppendLine($"        return Inquiry.ExecuteBatchAsync({batchDescriptor}, {itemsExpression}, {cancellation});");
                }
                source.AppendLine("    }");
                break;
            }

            case StoreOperation.UpdateAll:
            {
                // The runtime selects the descriptor's fixed-row, array-bound, or eligible set-based path.
                var itemsExpression = NonNullBatchItemsExpression(method.Parameters[0]);
                var batchDescriptor = BuildBatchDescriptorFieldName(method);
                AppendHeader(source, method, parameters, isAsync: false);
                source.AppendLine($"        return Inquiry.ExecuteBatchAsync({batchDescriptor}, {itemsExpression}, {cancellation});");
                source.AppendLine("    }");
                break;
            }

            case StoreOperation.UpdateByPredicate:
                AppendHeader(source, method, parameters, isAsync: false);
                EmitMutationByPredicate(source, sqlBuilder, method, fieldColumns, predicatePlan!, "_sqlUpdateWhere_" + method.Name, cancellation, entity.Schema);
                source.AppendLine("    }");
                break;

            case StoreOperation.DeleteByPredicate:
                AppendHeader(source, method, parameters, isAsync: false);
                EmitMutationByPredicate(source, sqlBuilder, method, Array.Empty<ColumnData>(), predicatePlan!, "_sqlDeleteWhere_" + method.Name, cancellation, entity.Schema);
                source.AppendLine("    }");
                break;

            case StoreOperation.StoredProcedure:
                EmitStoredProcedure(source, sqlBuilder, method, parameters, entityType, structMat, cancellation, entities);
                break;
        }
    }

    // ---- Private emit helpers ----

    private static void EmitFastExecuteFromEntity(
        StringBuilder source,
        SqlBuilder sqlBuilder,
        string sqlField,
        EntityData entity,
        string entityParameter,
        string entityType,
        string cancellation,
        string indent,
        bool includeKey,
        bool returnRowsAffectedAsBool,
        bool forUpdate = false)
    {
        var columns = SelectMutationColumns(entity, includeKey, forUpdate);

        // a bool-returning mutation on a token entity captures the row count so a 0-row conflict can
        // (when the runtime option is set) throw instead of silently returning false.
        if (returnRowsAffectedAsBool && entity.ConcurrencyToken is not null)
        {
            source.AppendLine($"{indent}var _rows = await Inquiry.ExecuteAsync(");
            source.AppendLine($"{indent}    new global::Inquiry.Commands.InquiryGeneratedCommand<{entityType}>(");
            source.AppendLine($"{indent}        {sqlField},");
            source.AppendLine($"{indent}        {entityParameter},");
            AppendBinderLambda(source, sqlBuilder, "_e", columns, i => $"_e.{columns[i].PropertyName}", indent + "        ", trailingComma: false);
            source.AppendLine($"{indent}    ),");
            source.AppendLine($"{indent}    {cancellation}).ConfigureAwait(false);");
            AppendConcurrencyConflictGuard(source, indent);
            source.AppendLine($"{indent}return _rows > 0;");
            return;
        }

        var awaitPrefix = returnRowsAffectedAsBool ? "await " : string.Empty;
        var returnSuffix = returnRowsAffectedAsBool ? ".ConfigureAwait(false) > 0" : string.Empty;

        source.AppendLine($"{indent}return {awaitPrefix}Inquiry.ExecuteAsync(");
        source.AppendLine($"{indent}    new global::Inquiry.Commands.InquiryGeneratedCommand<{entityType}>(");
        source.AppendLine($"{indent}        {sqlField},");
        source.AppendLine($"{indent}        {entityParameter},");
        AppendBinderLambda(source, sqlBuilder, "_e", columns, i => $"_e.{columns[i].PropertyName}", indent + "        ", trailingComma: false);
        source.AppendLine($"{indent}    ),");
        source.AppendLine($"{indent}    {cancellation}){returnSuffix};");
    }

    /// <summary>
    /// Emits the optimistic-concurrency conflict guard for a captured <c>_rows</c> count — a 0-row
    /// mutation on a token entity throws <see cref="global::Inquiry.InquiryConcurrencyException"/> only
    /// when the runtime option is enabled (gated at the call site so non-token entities are unaffected).
    /// </summary>
    private static void AppendConcurrencyConflictGuard(StringBuilder source, string indent)
        => source.AppendLine($"{indent}if (_rows == 0 && Inquiry.ThrowOnConcurrencyConflict) throw new global::Inquiry.InquiryConcurrencyException();");

    /// <summary>
    /// Builds the C# expression assigned to <c>DbParameter.Value</c>. Non-enum columns use a simple
    /// null-coalesce; enum columns coerce to the underlying integer type so providers that reject
    /// unmapped enums (e.g. Npgsql) see the same primitive value the reflection binder would.
    /// </summary>
    /// <summary>
    /// The DbType expression to assign on a bound parameter, or null when none applies: a converter
    /// column uses its provider type, an enum-as-string column binds a string, otherwise
    /// the column's own mapping.
    /// </summary>
    private static string? ResolveDbType(ColumnData column, SqlBuilder sqlBuilder)
    {
        if (column.Converter is { } converter)
        {
            return converter.ProviderType is { } providerType
                ? sqlBuilder.MapDbTypeExpression(providerType, column.IsUnicode)
                : sqlBuilder.MapDbTypeExpressionForSpecialType(converter.ProviderSpecialType, column.IsUnicode);
        }

        if (column.EnumAsString)
        {
            return column.IsUnicode
                ? "global::System.Data.DbType.String"
                : "global::System.Data.DbType.AnsiString";
        }

        return sqlBuilder.MapDbTypeExpression(column.Type, column.IsUnicode);
    }

    /// <summary>
    /// Emits <c>Size</c> (variable-length string) or <c>Precision</c>+<c>Scale</c> (decimal) on a
    /// <b>predicate</b> parameter (a WHERE/comparison value), but only on dialects whose plan cache keys on
    /// parameter metadata (<see cref="SqlBuilder.EmitsParameterSizePrecision"/>) and only when the column
    /// declares the value via <c>[InquiryColumn(Length/Precision/Scale)]</c>. A declared size keeps the
    /// emitted <c>sp_executesql</c> signature stable across value lengths so high-cardinality string/decimal
    /// predicates don't flood the plan cache.
    /// <para>
    /// Deliberately NOT called on value-write parameters (INSERT/UPDATE binders): <c>Size</c> on a write
    /// parameter makes SqlClient silently truncate an over-length value client-side, turning a loud server
    /// truncation error into silent data loss. Undeclared-length columns keep provider inference (no invented
    /// default). IN/NOT IN list elements are not covered here — they would need the size threaded through the
    /// <c>InquiryInExpansion</c> runtime helper.
    /// </para>
    /// </summary>
    private static void AppendSizePrecision(StringBuilder source, ColumnData column, SqlBuilder sqlBuilder, string paramVar, string indent)
    {
        if (!sqlBuilder.EmitsParameterSizePrecision)
        {
            return;
        }

        if (IsStringParameter(column))
        {
            // nvarchar tops out at 4000 chars, varchar at 8000; a declared length beyond that maps to a MAX
            // column whose width isn't fixed, so leave it to inference rather than pinning a wrong Size.
            // Length is never validated to a range upstream, so this guard also keeps Size sane.
            var maxSize = column.IsUnicode ? 4000 : 8000;
            if (column.Length > 0 && column.Length <= maxSize)
            {
                source.AppendLine($"{indent}{paramVar}.Size = {column.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)};");
            }
        }
        else if (IsDecimalParameter(column)
            && column.Precision is > 0 and <= 38
            && column.Scale >= 0 && column.Scale <= column.Precision)
        {
            // DbParameter.Precision/Scale are byte; Precision/Scale are not range-validated upstream, so the
            // guard above (SQL Server's max decimal precision is 38) keeps the emitted literals in byte range
            // and the scale within the precision.
            source.AppendLine($"{indent}{paramVar}.Precision = {column.Precision.ToString(System.Globalization.CultureInfo.InvariantCulture)};");
            source.AppendLine($"{indent}{paramVar}.Scale = {column.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture)};");
        }
    }

    /// <summary>Emits provider type metadata shared by every column-backed parameter.</summary>
    private static void AppendColumnParameterMetadata(
        StringBuilder source,
        ColumnData column,
        SqlBuilder sqlBuilder,
        string paramVar,
        string indent,
        bool predicate)
    {
        var dbType = ResolveDbType(column, sqlBuilder);
        if (dbType is not null)
        {
            source.AppendLine($"{indent}{paramVar}.DbType = {dbType};");
        }

        if (predicate)
        {
            AppendSizePrecision(source, column, sqlBuilder, paramVar, indent);
        }
    }

    /// <summary>
    /// Returns the <see cref="Inquiry.Parameters.InquiryParameter"/> constructor-argument suffix
    /// (e.g. <c>, size: 64</c> or <c>, precision: 18, scale: 2</c>) carrying a <b>predicate</b> key
    /// parameter's declared <c>Size</c>/<c>Precision</c>/<c>Scale</c>. Used by the eager-load key binders,
    /// which build their parameters inline in an array initializer (no <c>_p</c> variable to set after
    /// construction, so <see cref="AppendSizePrecision"/>'s statement form does not apply). Gating is
    /// identical to <see cref="AppendSizePrecision"/>: SQL Server only, declared only, range-gated. Returns
    /// <see cref="string.Empty"/> when nothing should be emitted.
    /// </summary>
    private static string BuildSizePrecisionArgs(ColumnData column, SqlBuilder sqlBuilder)
    {
        if (!sqlBuilder.EmitsParameterSizePrecision)
        {
            return string.Empty;
        }

        if (IsStringParameter(column))
        {
            var maxSize = column.IsUnicode ? 4000 : 8000;
            if (column.Length > 0 && column.Length <= maxSize)
            {
                return $", size: {column.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            }
        }
        else if (IsDecimalParameter(column)
            && column.Precision is > 0 and <= 38
            && column.Scale >= 0 && column.Scale <= column.Precision)
        {
            var precision = column.Precision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var scale = column.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return $", precision: {precision}, scale: {scale}";
        }

        return string.Empty;
    }

    // The parameter's effective provider type is what the plan-cache signature is built from: a converter
    // binds its provider primitive, enum-as-string binds a string, and everything else binds its CLR type.
    private static bool IsStringParameter(ColumnData column)
        => column.Converter is { } converter
            ? converter.ProviderSpecialType == SpecialType.System_String
            : column.EnumAsString || column.Type.SpecialType == SpecialType.System_String;

    private static bool IsDecimalParameter(ColumnData column)
        => column.Converter is { } converter
            ? converter.ProviderSpecialType == SpecialType.System_Decimal
            : !column.Type.IsEnum && column.Type.SpecialType == SpecialType.System_Decimal;

    private static string BuildParameterValueExpression(ColumnData column, string accessor, SqlBuilder sqlBuilder)
    {
        // a converter column binds ToProvider(value); a null nullable model → NULL (converter not called).
        if (column.Converter is { } converter)
        {
            // converters are stateless; bind through the shared cached instance instead of allocating one per bind.
            var toProvider = ConverterInvocationEmitter.ToProvider(converter, NonNullableValueExpression(column.Type, accessor));
            // An unsigned/sbyte provider type is bound via its same-width storage partner (DbTypeMapper maps
            // the provider DbType to that signed/byte type): SqlClient rejects DbType.UInt*/SByte and would
            // overflow on a checked Convert past the signed max, so reinterpret the bit pattern with unchecked().
            var bridged = BridgeProviderValue(sqlBuilder, converter.ProviderType, converter.ProviderSpecialType, converter.ProviderTypeDisplay, toProvider);
            var providerValue = ReinterpretUnsignedProviderValue(converter.ProviderSpecialType, bridged);
            return column.Type.IsNullable
                ? $"{accessor} is null ? global::System.DBNull.Value : (object){providerValue}"
                : $"(object){providerValue}";
        }

        // enum-as-string binds the enum's member name (a string). A null nullable-enum → NULL.
        if (column.EnumAsString)
        {
            return column.Type.IsNullable
                ? $"{accessor}.HasValue ? (object){accessor}.Value.ToString() : global::System.DBNull.Value"
                : $"(object){accessor}.ToString()";
        }

        if (!column.Type.IsEnum)
        {
            // Unsigned/sbyte plain columns are reinterpreted to the same-width signed type.
            // DbType.SByte/UInt16/UInt32/UInt64 are rejected by SqlClient; storing via the signed
            // partner is lossless (same bit pattern) and the materializer reverses the cast on read.
            var reinterpretedValue = column.Type.SpecialType switch
            {
                SpecialType.System_SByte  => column.Type.IsNullable
                    ? $"{accessor}.HasValue ? (object)unchecked((byte){accessor}.Value) : global::System.DBNull.Value"
                    : $"(object)unchecked((byte){accessor})",
                SpecialType.System_UInt16 => column.Type.IsNullable
                    ? $"{accessor}.HasValue ? (object)unchecked((short){accessor}.Value) : global::System.DBNull.Value"
                    : $"(object)unchecked((short){accessor})",
                SpecialType.System_UInt32 => column.Type.IsNullable
                    ? $"{accessor}.HasValue ? (object)unchecked((int){accessor}.Value) : global::System.DBNull.Value"
                    : $"(object)unchecked((int){accessor})",
                SpecialType.System_UInt64 => column.Type.IsNullable
                    ? $"{accessor}.HasValue ? (object)unchecked((long){accessor}.Value) : global::System.DBNull.Value"
                    : $"(object)unchecked((long){accessor})",
                _ => null,
            };
            if (reinterpretedValue is not null) return reinterpretedValue;

            var nonNullable = NonNullableValueExpression(column.Type, accessor);
            var bridged = BridgeProviderValue(sqlBuilder, column.Type, column.Type.SpecialType, column.Type.NonNullableDisplayName, nonNullable);
            if (bridged == nonNullable) return $"(object?){accessor} ?? global::System.DBNull.Value";
            return column.Type.IsNullable
                ? $"{accessor}.HasValue ? (object){bridged} : global::System.DBNull.Value"
                : $"(object){bridged}";
        }

        // Enum columns: cast to the underlying integer type. Unsigned/sbyte underlyings are bound
        // via their signed same-width partner (matching DbTypeMapper) using unchecked() so out-of-range
        // values (e.g. SampleEnumUInt32.AboveIntMax) are preserved as bit patterns rather than throwing.
        var underlying = column.Type.EnumUnderlyingSpecialType switch
        {
            SpecialType.System_Byte   => ("byte",  false),
            SpecialType.System_SByte  => ("byte",  true),   // reinterpret: sbyte ↔ byte
            SpecialType.System_Int16  => ("short", false),
            SpecialType.System_UInt16 => ("short", true),   // reinterpret: ushort ↔ short
            SpecialType.System_Int32  => ("int",   false),
            SpecialType.System_UInt32 => ("int",   true),   // reinterpret: uint ↔ int
            SpecialType.System_Int64  => ("long",  false),
            SpecialType.System_UInt64 => ("long",  true),   // reinterpret: ulong ↔ long
            _                         => ("int",   false),
        };
        var (typeName, needsUnchecked) = underlying;
        var castExpr      = needsUnchecked ? $"unchecked(({typeName}){accessor})"       : $"({typeName}){accessor}";
        var castExprValue = needsUnchecked ? $"unchecked(({typeName}){accessor}.Value)" : $"({typeName}){accessor}.Value";
        return column.Type.IsNullable
            ? $"{accessor}.HasValue ? (object){castExprValue} : global::System.DBNull.Value"
            : $"(object){castExpr}";
    }

    private static string BridgeProviderValue(SqlBuilder sqlBuilder, TypeData? providerType, SpecialType specialType, string providerTypeName, string valueExpression)
        => sqlBuilder.BuildParameterValueExpression(new ParameterValueExpressionContext(
            valueExpression,
            providerTypeName,
            specialType,
            ProviderIsDateOnly: providerType?.IsDateOnly == true,
            ProviderIsTimeOnly: providerType?.IsTimeOnly == true,
            ProviderIsDateTimeOffset: providerType?.NonNullableDisplayName == "global::System.DateTimeOffset"));

    private static string BulkFieldTypeExpression(ColumnData column, SqlBuilder sqlBuilder)
    {
        TypeData? providerType;
        SpecialType providerSpecialType;
        string providerTypeName;

        if (column.Converter is { } converter)
        {
            providerType = converter.ProviderType;
            providerSpecialType = converter.ProviderSpecialType;
            providerTypeName = converter.ProviderType?.NonNullableDisplayName ?? converter.ProviderTypeDisplay;
        }
        else if (column.EnumAsString)
        {
            return "typeof(global::System.String)";
        }
        else if (column.Type.IsEnum)
        {
            providerType = null;
            providerSpecialType = column.Type.EnumUnderlyingSpecialType;
            providerTypeName = SpecialTypeName(providerSpecialType);
        }
        else
        {
            providerType = column.Type;
            providerSpecialType = column.Type.SpecialType;
            providerTypeName = column.Type.NonNullableDisplayName;
        }

        // BuildParameterValueExpression reinterprets these unsupported unsigned ADO primitives
        // after any provider bridge, so metadata must describe the signed storage partner too.
        var reinterpretedType = providerSpecialType switch
        {
            SpecialType.System_SByte => "global::System.Byte",
            SpecialType.System_UInt16 => "global::System.Int16",
            SpecialType.System_UInt32 => "global::System.Int32",
            SpecialType.System_UInt64 => "global::System.Int64",
            _ => null,
        };
        if (reinterpretedType is not null)
        {
            return $"typeof({reinterpretedType})";
        }

        var context = new ParameterValueExpressionContext(
            "_value",
            providerTypeName,
            providerSpecialType,
            ProviderIsDateOnly: providerType?.IsDateOnly == true,
            ProviderIsTimeOnly: providerType?.IsTimeOnly == true,
            ProviderIsDateTimeOffset: providerType?.NonNullableDisplayName == "global::System.DateTimeOffset");
        return $"typeof({sqlBuilder.BuildParameterValueTypeName(context)})";
    }

    private static string BuildInlineParameterValueExpression(ColumnData column, string accessor, SqlBuilder sqlBuilder)
    {
        var nonNullable = NonNullableValueExpression(column.Type, accessor);
        var bridged = BridgeProviderValue(sqlBuilder, column.Type, column.Type.SpecialType, column.Type.NonNullableDisplayName, nonNullable);
        return column.Converter is null && bridged == nonNullable
            ? accessor
            : BuildParameterValueExpression(column, accessor, sqlBuilder);
    }

    /// <summary>
    /// Reinterprets an unsigned/sbyte converter provider value to the same-width storage type the
    /// provider accepts (sbyte→byte, ushort→short, uint→int, ulong→long) via <c>unchecked()</c>,
    /// matching <see cref="Infrastructure.DbTypeMapper"/>. Returns the expression unchanged for
    /// signed and non-integer provider types.
    /// </summary>
    private static string ReinterpretUnsignedProviderValue(SpecialType providerSpecialType, string valueExpression)
        => providerSpecialType switch
        {
            SpecialType.System_SByte  => $"unchecked((byte)({valueExpression}))",
            SpecialType.System_UInt16 => $"unchecked((short)({valueExpression}))",
            SpecialType.System_UInt32 => $"unchecked((int)({valueExpression}))",
            SpecialType.System_UInt64 => $"unchecked((long)({valueExpression}))",
            _ => valueExpression,
        };

    private static void EmitFastExecuteFromKeys(
        StringBuilder source,
        SqlBuilder sqlBuilder,
        string sqlField,
        EquatableArray<ColumnData> keyColumns,
        EquatableArray<ParameterData> methodParameters,
        string cancellation,
        string indent,
        bool emitConcurrencyGuard = false)
    {
        // when the entity has a concurrency token, capture the row count and gate a conflict throw on
        // the runtime option; otherwise emit the original inline `… > 0` tail (byte-identical to before).
        var capture = emitConcurrencyGuard ? "var _rows = await Inquiry.ExecuteAsync(" : "return await Inquiry.ExecuteAsync(";
        var tail = emitConcurrencyGuard ? ").ConfigureAwait(false);" : ").ConfigureAwait(false) > 0;";

        if (keyColumns.Count == 1)
        {
            var keyParamName = methodParameters[0].Name;
            source.AppendLine($"{indent}{capture}");
            source.AppendLine($"{indent}    new global::Inquiry.Commands.InquiryGeneratedCommand<{methodParameters[0].TypeDisplay}>(");
            source.AppendLine($"{indent}        {sqlField},");
            source.AppendLine($"{indent}        {keyParamName},");
            AppendBinderLambda(source, sqlBuilder, "_key", keyColumns.AsImmutableArray(), _ => "_key", indent + "        ", emitSizePrecision: true, trailingComma: false);
            source.AppendLine($"{indent}    ),");
            source.AppendLine($"{indent}    {cancellation}{tail}");
        }
        else
        {
            var tupleArgs = string.Join(", ", Take(methodParameters, keyColumns.Count).Select(p => p.Name));
            var tupleType = "(" + string.Join(", ", Take(methodParameters, keyColumns.Count).Select(p => p.TypeDisplay)) + ")";
            source.AppendLine($"{indent}{capture}");
            source.AppendLine($"{indent}    new global::Inquiry.Commands.InquiryGeneratedCommand<{tupleType}>(");
            source.AppendLine($"{indent}        {sqlField},");
            source.AppendLine($"{indent}        ({tupleArgs}),");
            AppendBinderLambda(source, sqlBuilder, "_keys", keyColumns.AsImmutableArray(), i => $"_keys.Item{i + 1}", indent + "        ", emitSizePrecision: true, trailingComma: false);
            source.AppendLine($"{indent}    ),");
            source.AppendLine($"{indent}    {cancellation}{tail}");
        }

        if (emitConcurrencyGuard)
        {
            AppendConcurrencyConflictGuard(source, indent);
            source.AppendLine($"{indent}return _rows > 0;");
        }
    }

    /// <summary>
    /// Emits a <c>static (_cmd, &lt;lambdaParam&gt;) =&gt; { … }</c> binder that writes one
    /// <c>DbParameter</c> per column straight into the <c>DbCommand</c>. <paramref name="accessor"/>
    /// yields the value expression for column <c>i</c>. <paramref name="emitSizePrecision"/> is set only
    /// when the bound columns are <b>predicate</b> values (key/field lookups, deletes) — never write
    /// values (insert/update/upsert) — because <c>Size</c> on a write parameter silently truncates an
    /// over-length value (see <see cref="AppendSizePrecision"/>). It defaults to <see langword="false"/>
    /// so a new caller that forgets to classify itself fails safe (no truncation, just no optimization).
    /// </summary>
    private static void AppendBinderLambda(
        StringBuilder source,
        SqlBuilder sqlBuilder,
        string lambdaParam,
        IReadOnlyList<ColumnData> columns,
        Func<int, string> accessor,
        string indent,
        bool emitSizePrecision = false,
        bool trailingComma = true)
    {
        source.AppendLine($"{indent}static (_cmd, {lambdaParam}) =>");
        source.AppendLine($"{indent}{{");
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            source.AppendLine($"{indent}    var _p{i} = _cmd.CreateParameter();");
            source.AppendLine($"{indent}    _p{i}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(column.PropertyName))}\";");
            AppendColumnParameterMetadata(source, column, sqlBuilder, $"_p{i}", indent + "    ", emitSizePrecision);
            source.AppendLine($"{indent}    _p{i}.Value = {BuildParameterValueExpression(column, accessor(i), sqlBuilder)};");
            source.AppendLine($"{indent}    _cmd.Parameters.Add(_p{i});");
        }
        source.AppendLine($"{indent}}}{(trailingComma ? "," : string.Empty)}");
    }

    private static void AppendSingleParameterGeneratedCommand(
        StringBuilder source,
        SqlBuilder sqlBuilder,
        string sqlField,
        string stateType,
        string stateValue,
        string parameterName,
        ColumnData column,
        string indent)
    {
        source.AppendLine($"{indent}new global::Inquiry.Commands.InquiryGeneratedCommand<{stateType}>(");
        source.AppendLine($"{indent}    {sqlField},");
        source.AppendLine($"{indent}    {stateValue},");
        source.AppendLine($"{indent}    static (global::System.Data.Common.DbCommand _cmd, {stateType} _arg) =>");
        source.AppendLine($"{indent}    {{");
        source.AppendLine($"{indent}        var _p = _cmd.CreateParameter();");
        source.AppendLine($"{indent}        _p.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(parameterName))}\";");
        AppendColumnParameterMetadata(source, column, sqlBuilder, "_p", indent + "        ", predicate: true);
        source.AppendLine($"{indent}        _p.Value = {BuildParameterValueExpression(column, "_arg", sqlBuilder)};");
        source.AppendLine($"{indent}        _cmd.Parameters.Add(_p);");
        source.AppendLine($"{indent}    }}),");
    }

    private static void EmitFastQuerySingleByKeys(
        StringBuilder source,
        SqlBuilder sqlBuilder,
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
            source.AppendLine($"{indent}return await Inquiry.QueryGeneratedSingleOrDefaultAsync<{entityType}, {keyParam.TypeDisplay}, {structMat}>(");
            source.AppendLine($"{indent}    new global::Inquiry.Commands.InquiryGeneratedCommand<{keyParam.TypeDisplay}>(");
            source.AppendLine($"{indent}        {sqlField},");
            source.AppendLine($"{indent}        {keyParam.Name},");
            AppendBinderLambda(source, sqlBuilder, "_key", keyColumns.AsImmutableArray(), _ => "_key", indent + "        ", emitSizePrecision: true, trailingComma: false);
            source.AppendLine($"{indent}    ),");
            source.AppendLine($"{indent}    default,");
            source.AppendLine($"{indent}    {cancellation}).ConfigureAwait(false);");
            return;
        }

        var tupleArgs = string.Join(", ", Take(methodParameters, keyColumns.Count).Select(p => p.Name));
        var tupleType = "(" + string.Join(", ", Take(methodParameters, keyColumns.Count).Select(p => p.TypeDisplay)) + ")";
        source.AppendLine($"{indent}return await Inquiry.QueryGeneratedSingleOrDefaultAsync<{entityType}, {tupleType}, {structMat}>(");
        source.AppendLine($"{indent}    new global::Inquiry.Commands.InquiryGeneratedCommand<{tupleType}>(");
        source.AppendLine($"{indent}        {sqlField},");
        source.AppendLine($"{indent}        ({tupleArgs}),");
        AppendBinderLambda(source, sqlBuilder, "_keys", keyColumns.AsImmutableArray(), i => $"_keys.Item{i + 1}", indent + "        ", emitSizePrecision: true, trailingComma: false);
        source.AppendLine($"{indent}    ),");
        source.AppendLine($"{indent}    default,");
        source.AppendLine($"{indent}    {cancellation}).ConfigureAwait(false);");
    }

    private static void EmitFastQueryByFields(
        StringBuilder source,
        SqlBuilder sqlBuilder,
        EquatableArray<ParameterData> methodParameters,
        IReadOnlyList<ColumnData> fieldColumns,
        string entityType,
        string structMat,
        string cancellation,
        string indent,
        string? sqlField = null,
        bool returnsList = true)
    {
        sqlField ??= "_sqlSelectBy_" + BuildFieldSuffix(fieldColumns);
        var queryMethod = returnsList ? "QueryListAsync" : "QueryAsync";
        if (fieldColumns.Count == 1)
        {
            var fieldParam = methodParameters[0];
            source.AppendLine($"{indent}return Inquiry.{queryMethod}<{entityType}, {fieldParam.TypeDisplay}, {structMat}>(");
            source.AppendLine($"{indent}    new global::Inquiry.Commands.InquiryGeneratedCommand<{fieldParam.TypeDisplay}>(");
            source.AppendLine($"{indent}        {sqlField},");
            source.AppendLine($"{indent}        {fieldParam.Name},");
            AppendBinderLambda(source, sqlBuilder, "_arg", fieldColumns, _ => "_arg", indent + "        ", emitSizePrecision: true, trailingComma: false);
            source.AppendLine($"{indent}    ),");
            source.AppendLine($"{indent}    default,");
            source.AppendLine($"{indent}    {cancellation});");
            return;
        }

        var tupleArgs = string.Join(", ", Take(methodParameters, fieldColumns.Count).Select(p => p.Name));
        var tupleType = "(" + string.Join(", ", Take(methodParameters, fieldColumns.Count).Select(p => p.TypeDisplay)) + ")";
        source.AppendLine($"{indent}return Inquiry.{queryMethod}<{entityType}, {tupleType}, {structMat}>(");
        source.AppendLine($"{indent}    new global::Inquiry.Commands.InquiryGeneratedCommand<{tupleType}>(");
        source.AppendLine($"{indent}        {sqlField},");
        source.AppendLine($"{indent}        ({tupleArgs}),");
        AppendBinderLambda(source, sqlBuilder, "_args", fieldColumns, i => $"_args.Item{i + 1}", indent + "        ", emitSizePrecision: true, trailingComma: false);
        source.AppendLine($"{indent}    ),");
        source.AppendLine($"{indent}    default,");
        source.AppendLine($"{indent}    {cancellation});");
    }

    /// <summary>
    /// Emits a <c>SelectAllByPredicate</c> body. Predicate methods route through an immutable generated
    /// command with a static binder, covering both scalar binding and the IN command-text rewrite (the binder runs after the
    /// pipeline assigns the command text, which is what lets <see cref="global::Inquiry.Parameters.InquiryInExpansion"/>
    /// expand the sentinel). Buffered methods use the list overload; streaming ones use QueryAsync.
    /// </summary>
    private static void EmitSelectAllByPredicate(
        StringBuilder source,
        SqlBuilder sqlBuilder,
        StoreMethodData method,
        ResolvedPredicatePlan plan,
        string entityType,
        string structMat,
        string cancellation,
        string? owningSchema)
    {
        EmitPredicateBoundCommand(source, sqlBuilder, method, plan, "_sqlPredicate_" + method.Name, owningSchema);
        var state = new GeneratedCommandState(method.Parameters, includeMaxParameters: plan.Bindings.Any(binding => binding.IsCollection));

        if (method.ReturnsList)
        {
            source.AppendLine($"        return Inquiry.QueryListAsync<{entityType}, {state.Type}, {structMat}>(_cmd, default, {cancellation});");
        }
        else
        {
            source.AppendLine($"        return Inquiry.QueryAsync<{entityType}, {state.Type}, {structMat}>(_cmd, default, {cancellation});");
        }
    }

    /// <summary>
    /// Emits an existence test body ([InquiryExists]): the EXISTS scalar query, returned as a
    /// <c>Task&lt;bool&gt;</c> through the runtime scalar path (1/0 → bool). With no criteria there are no
    /// parameters to bind, so the command uses a no-op binder; otherwise the
    /// predicate binder runs after the pipeline assigns the command text (as for predicate selects).
    /// </summary>
    private static void EmitExists(StringBuilder source, SqlBuilder sqlBuilder, StoreMethodData method, ResolvedPredicatePlan plan, string cancellation, string? owningSchema)
    {
        var sqlField = "_sqlExists_" + method.Name;
        if (plan.Bindings.Count == 0)
        {
            source.AppendLine($"        return Inquiry.ExecuteScalarAsync<bool, byte>({EmptyGeneratedCommand(sqlField)}, {cancellation});");
            return;
        }

        EmitPredicateBoundCommand(source, sqlBuilder, method, plan, sqlField, owningSchema);
        source.AppendLine($"        return Inquiry.ExecuteScalarAsync<bool, {new GeneratedCommandState(method.Parameters, includeMaxParameters: plan.Bindings.Any(binding => binding.IsCollection)).Type}>(_cmd, {cancellation});");
    }

    /// <summary>
    /// Emits the immutable generated command shared by predicate selects and existence tests. Its static
    /// binder runs after the pipeline assigns the
    /// command text, so <c>InquiryInExpansion</c> can rewrite an IN/NOT IN sentinel.
    /// </summary>
    private static void EmitPredicateBoundCommand(StringBuilder source, SqlBuilder sqlBuilder, StoreMethodData method, ResolvedPredicatePlan plan, string sqlField, string? owningSchema)
    {
        var state = new GeneratedCommandState(method.Parameters, includeMaxParameters: plan.Bindings.Any(binding => binding.IsCollection));
        source.AppendLine($"        var _cmd = new global::Inquiry.Commands.InquiryGeneratedCommand<{state.Type}>(");
        source.AppendLine($"            {sqlField},");
        source.AppendLine($"            {state.Value},");
        source.AppendLine($"            static (global::System.Data.Common.DbCommand _c, {state.Type} _args) =>");
        source.AppendLine("            {");
        AppendGeneratedStateAliases(source, method.Parameters, state, "                ");
        for (var i = 0; i < plan.Bindings.Count; i++)
        {
            var binding = plan.Bindings[i];
            var arg = method.Parameters[binding.MethodParameterIndex].Name;
            if (binding.IsCollection)
            {
                source.AppendLine(CollectionBindingExpression(
                    sqlBuilder,
                    binding.Column,
                    owningSchema,
                    binding.SqlParameterName,
                    arg,
                    binding.IsNegatedCollection,
                    binding.ElementIsNullable,
                    binding.CollectionResolution,
                    state.MaxParametersReference));
            }
            else
            {
                source.AppendLine($"                var _p{i} = _c.CreateParameter();");
                source.AppendLine($"                _p{i}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterNameFromSql(binding.SqlParameterName))}\";");
                AppendColumnParameterMetadata(source, binding.Column, sqlBuilder, $"_p{i}", "                ", predicate: true);
                source.AppendLine($"                _p{i}.Value = {BuildParameterValueExpression(binding.Column, arg, sqlBuilder)};");
                source.AppendLine($"                _c.Parameters.Add(_p{i});");
            }
        }
        source.AppendLine("            });");
    }

    /// <summary>
    /// Emits a set-based predicate mutation body ([InquiryUpdateWhere]/[InquiryDeleteWhere]),
    /// following the <see cref="EmitSelectAllByPredicate"/> immutable generated-command pattern
    /// pattern (the binder runs after the pipeline assigns the command text, which lets
    /// <c>InquiryInExpansion</c> rewrite an IN sentinel). Binds the SET parameters first — with
    /// DbType stamping and converter/enum-aware value expressions, matching the single-row update
    /// binder — then the predicate bindings, and returns the rows-affected count via ExecuteAsync.
    /// For a delete, <paramref name="setColumns"/> is empty.
    /// </summary>
    private static void EmitMutationByPredicate(
        StringBuilder source,
        SqlBuilder sqlBuilder,
        StoreMethodData method,
        IReadOnlyList<ColumnData> setColumns,
        ResolvedPredicatePlan plan,
        string sqlField,
        string cancellation,
        string? owningSchema)
    {
        var state = new GeneratedCommandState(method.Parameters, includeMaxParameters: plan.Bindings.Any(binding => binding.IsCollection));
        source.AppendLine($"        var _cmd = new global::Inquiry.Commands.InquiryGeneratedCommand<{state.Type}>(");
        source.AppendLine($"            {sqlField},");
        source.AppendLine($"            {state.Value},");
        source.AppendLine($"            static (global::System.Data.Common.DbCommand _c, {state.Type} _args) =>");
        source.AppendLine("            {");
        AppendGeneratedStateAliases(source, method.Parameters, state, "                ");

        var pi = 0;
        for (var i = 0; i < setColumns.Count; i++)
        {
            var column = setColumns[i];
            var arg = method.Parameters[i].Name;
            source.AppendLine($"                var _p{pi} = _c.CreateParameter();");
            source.AppendLine($"                _p{pi}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(column.PropertyName))}\";");
            AppendColumnParameterMetadata(source, column, sqlBuilder, $"_p{pi}", "                ", predicate: false);
            source.AppendLine($"                _p{pi}.Value = {BuildParameterValueExpression(column, arg, sqlBuilder)};");
            source.AppendLine($"                _c.Parameters.Add(_p{pi});");
            pi++;
        }

        // Predicate bindings carry absolute method-parameter indexes (offset past the SET values for
        // an update), so this loop is identical to the predicate-select binder.
        foreach (var binding in plan.Bindings)
        {
            var arg = method.Parameters[binding.MethodParameterIndex].Name;
            if (binding.IsCollection)
            {
                source.AppendLine(CollectionBindingExpression(
                    sqlBuilder,
                    binding.Column,
                    owningSchema,
                    binding.SqlParameterName,
                    arg,
                    binding.IsNegatedCollection,
                    binding.ElementIsNullable,
                    binding.CollectionResolution,
                    state.MaxParametersReference));
            }
            else
            {
                source.AppendLine($"                var _p{pi} = _c.CreateParameter();");
                source.AppendLine($"                _p{pi}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterNameFromSql(binding.SqlParameterName))}\";");
                AppendColumnParameterMetadata(source, binding.Column, sqlBuilder, $"_p{pi}", "                ", predicate: true);
                source.AppendLine($"                _p{pi}.Value = {BuildParameterValueExpression(binding.Column, arg, sqlBuilder)};");
                source.AppendLine($"                _c.Parameters.Add(_p{pi});");
                pi++;
            }
        }

        source.AppendLine("            });");
        source.AppendLine($"        return Inquiry.ExecuteAsync(_cmd, {cancellation});");
    }

    /// <summary>
    /// Emits an ordered and/or offset-paged buffered select. Binds the field/filter parameters (for
    /// SelectAllByField) and, when offset-paged, the synthetic <c>@__offset</c>/<c>@__limit</c> int
    /// parameters through an immutable generated command and static binder.
    /// </summary>
    private static void EmitOffsetPaged(
        StringBuilder source,
        SqlBuilder sqlBuilder,
        StoreMethodData method,
        IReadOnlyList<ColumnData> fieldColumns,
        ResolvedSelectPlan plan,
        string entityType,
        string structMat,
        string cancellation)
    {
        var paged = plan.Pagination == Pagination.Offset;
        var state = new GeneratedCommandState(method.Parameters);

        // Audit P2 #12: guard offset/limit at the call site before they reach the SQL. Negative offsets
        // / non-positive limits previously fell through to the provider (provider-specific error or
        // silent empty result). nameof() in the throw preserves the caller-visible argument name.
        if (paged)
        {
            var offsetArg = method.Parameters[fieldColumns.Count].Name;
            var limitArg = method.Parameters[fieldColumns.Count + 1].Name;
            source.AppendLine($"        if ({offsetArg} < 0) throw new global::System.ArgumentOutOfRangeException(nameof({offsetArg}), {offsetArg}, \"Pagination offset must be >= 0.\");");
            source.AppendLine($"        if ({limitArg} <= 0) throw new global::System.ArgumentOutOfRangeException(nameof({limitArg}), {limitArg}, \"Pagination limit must be > 0.\");");
        }

        source.AppendLine($"        var _cmd = new global::Inquiry.Commands.InquiryGeneratedCommand<{state.Type}>(");
        source.AppendLine($"            {plan.SqlFieldName},");
        source.AppendLine($"            {state.Value},");
        source.AppendLine($"            static (global::System.Data.Common.DbCommand _c, {state.Type} _args) =>");
        source.AppendLine("            {");

        AppendGeneratedStateAliases(source, method.Parameters, state, "                ");

        var pi = 0;
        for (var i = 0; i < fieldColumns.Count; i++)
        {
            var arg = method.Parameters[i].Name;
            source.AppendLine($"                var _p{pi} = _c.CreateParameter();");
            source.AppendLine($"                _p{pi}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(fieldColumns[i].PropertyName))}\";");
            AppendColumnParameterMetadata(source, fieldColumns[i], sqlBuilder, $"_p{pi}", "                ", predicate: true);
            source.AppendLine($"                _p{pi}.Value = {BuildParameterValueExpression(fieldColumns[i], arg, sqlBuilder)};");
            source.AppendLine($"                _c.Parameters.Add(_p{pi});");
            pi++;
        }

        var pagedCapacityArg = string.Empty;
        if (paged)
        {
            var offsetArg = method.Parameters[fieldColumns.Count].Name;
            var limitArg = method.Parameters[fieldColumns.Count + 1].Name;
            AppendScalarIntParameter(source, sqlBuilder, ref pi, "__offset", offsetArg);
            AppendScalarIntParameter(source, sqlBuilder, ref pi, "__limit", limitArg);
            // The limit is the exact maximum row count, so pre-size the result list (#61).
            pagedCapacityArg = $", capacityHint: {method.Parameters[fieldColumns.Count + 1].Name}";
        }

        source.AppendLine("            });");

        if (method.ReturnsPagedResult)
        {
            source.AppendLine($"        var _items = await Inquiry.QueryListAsync<{entityType}, {state.Type}, {structMat}>(_cmd, default, {cancellation}{pagedCapacityArg}).ConfigureAwait(false);");

            if (fieldColumns.Count == 0)
            {
                source.AppendLine($"        var _total = await Inquiry.ExecuteScalarAsync<long, byte>({EmptyGeneratedCommand("_sqlCount_" + method.Name)}, {cancellation}).ConfigureAwait(false);");
            }
            else
            {
                source.AppendLine($"        var _countCmd = new global::Inquiry.Commands.InquiryGeneratedCommand<{state.Type}>(");
                source.AppendLine($"            _sqlCount_{method.Name},");
                source.AppendLine($"            {state.Value},");
                source.AppendLine($"            static (global::System.Data.Common.DbCommand _c, {state.Type} _args) =>");
                source.AppendLine("            {");
                AppendGeneratedStateAliases(source, method.Parameters, state, "                ");
                var cpi = 0;
                for (var i = 0; i < fieldColumns.Count; i++)
                {
                    var arg = method.Parameters[i].Name;
                    source.AppendLine($"                var _p{cpi} = _c.CreateParameter();");
                    source.AppendLine($"                _p{cpi}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(fieldColumns[i].PropertyName))}\";");
                    AppendColumnParameterMetadata(source, fieldColumns[i], sqlBuilder, $"_p{cpi}", "                ", predicate: true);
                    source.AppendLine($"                _p{cpi}.Value = {BuildParameterValueExpression(fieldColumns[i], arg, sqlBuilder)};");
                    source.AppendLine($"                _c.Parameters.Add(_p{cpi});");
                    cpi++;
                }
                source.AppendLine("            });");
                source.AppendLine($"        var _total = await Inquiry.ExecuteScalarAsync<long, {state.Type}>(_countCmd, {cancellation}).ConfigureAwait(false);");
            }

            source.AppendLine($"        return new global::Inquiry.Paging.InquiryPagedResult<{entityType}>(_items, _total);");
        }
        else
        {
            source.AppendLine($"        return Inquiry.QueryListAsync<{entityType}, {state.Type}, {structMat}>(_cmd, default, {cancellation}{pagedCapacityArg});");
        }
    }

    /// <summary>
    /// Emits a keyset-paged select returning <c>InquiryPage&lt;TEntity, TCursor&gt;</c>. Binds the cursor
    /// (or its tuple elements) and <c>pageSize + 1</c> to the per-method SQL const, materializes the
    /// over-fetched list, trims to the page size, derives <c>NextCursor</c> from the last item's key, and
    /// reports <c>HasMore</c>.
    /// </summary>
    private static void EmitKeysetPage(
        StringBuilder source,
        SqlBuilder sqlBuilder,
        StoreMethodData method,
        ResolvedSelectPlan plan,
        string entityType,
        string structMat,
        string cancellation)
    {
        var cursorParam = method.Parameters[0].Name;
        var pageSizeParam = method.Parameters[1].Name;
        var cursorType = method.Parameters[0].TypeDisplay;
        var single = plan.KeysetColumns.Count == 1;
        var state = new GeneratedCommandState(method.Parameters);

        // Audit P2 #12: guard pageSize. Non-positive pageSize wastes a round trip (or returns an
        // empty page after fetching one extra row). pageSize == int.MaxValue overflows the
        // `pageSize + 1` over-fetch arithmetic below.
        source.AppendLine($"        if ({pageSizeParam} <= 0) throw new global::System.ArgumentOutOfRangeException(nameof({pageSizeParam}), {pageSizeParam}, \"Page size must be > 0.\");");
        source.AppendLine($"        if ({pageSizeParam} == int.MaxValue) throw new global::System.ArgumentOutOfRangeException(nameof({pageSizeParam}), {pageSizeParam}, \"Page size must be less than int.MaxValue.\");");

        // A null cursor runs the predicate-free first-page query; a non-null cursor runs the seek query
        // (plain sargable `key > @cursor` -> index seek) and binds the cursor. Splitting the two -- rather
        // than one non-sargable (@cursor IS NULL OR ...) form -- is what keeps keyset paging O(pageSize):
        // the disjunction defeats the index seek and forces a full table scan (O(table size)).
        source.AppendLine($"        var _first = {cursorParam} is null;");
        source.AppendLine($"        var _cmd = new global::Inquiry.Commands.InquiryGeneratedCommand<{state.Type}>(");
        source.AppendLine($"            _first ? {plan.SqlFieldName}_first : {plan.SqlFieldName},");
        source.AppendLine($"            {state.Value},");
        source.AppendLine($"            static (global::System.Data.Common.DbCommand _c, {state.Type} _args) =>");
        source.AppendLine("            {");
        AppendGeneratedStateAliases(source, method.Parameters, state, "                ");
        source.AppendLine($"                var _first = {cursorParam} is null;");

        var pi = 0;
        if (plan.KeysetColumns.Count > 0)
        {
            // The @__cursor parameters appear only in the seek query, so bind them only on that path
            // (binding a parameter the first-page SQL never references errors on strict providers).
            source.AppendLine("                if (!_first)");
            source.AppendLine("                {");
            for (var i = 0; i < plan.KeysetColumns.Count; i++)
            {
                // On the seek path the cursor is non-null; for a multi-column cursor read its tuple element.
                var singleReference = single && !plan.KeysetColumns[i].Type.IsValueType;
                var rawCursorValue = singleReference
                    ? $"{cursorParam}!"
                    : single ? $"{cursorParam}.Value" : $"{cursorParam}.Value.Item{i + 1}";
                var bridgedCursorValue = BridgeProviderValue(
                    sqlBuilder,
                    plan.KeysetColumns[i].Type,
                    plan.KeysetColumns[i].Type.SpecialType,
                    plan.KeysetColumns[i].Type.NonNullableDisplayName,
                    rawCursorValue);
                var needsProviderTransform = plan.KeysetColumns[i].Converter is not null || bridgedCursorValue != rawCursorValue;
                var valueExpr = needsProviderTransform
                    ? (singleReference
                        ? $"{cursorParam} is not null ? {BuildParameterValueExpression(plan.KeysetColumns[i], rawCursorValue, sqlBuilder)} : global::System.DBNull.Value"
                        : $"{cursorParam}.HasValue ? {BuildParameterValueExpression(plan.KeysetColumns[i], rawCursorValue, sqlBuilder)} : global::System.DBNull.Value")
                    : single
                        ? $"(object?){cursorParam} ?? global::System.DBNull.Value"
                        : $"{cursorParam}.HasValue ? (object){cursorParam}.Value.Item{i + 1} : global::System.DBNull.Value";
                source.AppendLine($"                    var _p{pi} = _c.CreateParameter();");
                source.AppendLine($"                    _p{pi}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName("__cursor" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)))}\";");
                AppendColumnParameterMetadata(source, plan.KeysetColumns[i], sqlBuilder, $"_p{pi}", "                    ", predicate: true);
                source.AppendLine($"                    _p{pi}.Value = {valueExpr};");
                source.AppendLine($"                    _c.Parameters.Add(_p{pi});");
                pi++;
            }
            source.AppendLine("                }");
        }

        AppendScalarIntParameter(source, sqlBuilder, ref pi, "__pageSize", pageSizeParam + " + 1");

        source.AppendLine("            });");
        // Over-fetch pageSize + 1 to detect a next page; pre-size the list to that exact count (#61).
        source.AppendLine($"        var _rows = await Inquiry.QueryListAsync<{entityType}, {state.Type}, {structMat}>(_cmd, default, {cancellation}, capacityHint: {pageSizeParam} + 1).ConfigureAwait(false);");
        source.AppendLine($"        var _hasMore = _rows.Count > {pageSizeParam};");
        // Trim the sentinel over-fetch row in place (no second list, no per-item copy).
        source.AppendLine($"        if (_hasMore) ((global::System.Collections.Generic.List<{entityType}>)_rows).RemoveAt(_rows.Count - 1);");
        source.AppendLine("        var _items = _rows;");

        // NextCursor from the last returned item's key column(s); null on an empty page.
        var nonNullableCursor = StripNullable(cursorType);
        if (single)
        {
            var keyAccess = "_items[_items.Count - 1]." + plan.KeysetColumns[0].PropertyName;
            source.AppendLine($"        {cursorType} _next = _items.Count > 0 ? ({cursorType})({keyAccess}) : default;");
        }
        else
        {
            var tupleElems = string.Join(", ", plan.KeysetColumns.Select(c =>
            {
                var access = "_last." + c.PropertyName;
                if (!c.Type.IsNullable)
                {
                    return access;
                }

                // A returned row's key is never null in practice, but the compiler can't prove it; coerce
                // value types with GetValueOrDefault and reference types with the null-forgiving operator.
                return c.Type.IsValueType ? access + ".GetValueOrDefault()" : access + "!";
            }));
            source.AppendLine("        var _last = _items.Count > 0 ? _items[_items.Count - 1] : null;");
            source.AppendLine($"        {cursorType} _next = _last is not null ? ({nonNullableCursor})({tupleElems}) : default;");
        }

        source.AppendLine($"        return new global::Inquiry.Paging.InquiryPage<{entityType}, {nonNullableCursor}>(_items, _next, _hasMore);");
    }

    private static void AppendScalarIntParameter(StringBuilder source, SqlBuilder sqlBuilder, ref int pi, string logicalName, string valueExpr)
    {
        source.AppendLine($"                var _p{pi} = _c.CreateParameter();");
        source.AppendLine($"                _p{pi}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(logicalName))}\";");
        source.AppendLine($"                _p{pi}.DbType = global::System.Data.DbType.Int32;");
        source.AppendLine($"                _p{pi}.Value = {valueExpr};");
        source.AppendLine($"                _c.Parameters.Add(_p{pi});");
        pi++;
    }

    /// <summary>Strips a trailing nullable annotation/<c>Nullable&lt;&gt;</c> marker from a cursor type display.</summary>
    private static string StripNullable(string typeDisplay)
        => typeDisplay.EndsWith("?", StringComparison.Ordinal) ? typeDisplay.Substring(0, typeDisplay.Length - 1) : typeDisplay;

    private static void EmitFastQuerySingleFromEntity(
        StringBuilder source,
        SqlBuilder sqlBuilder,
        string sqlField,
        EntityData entity,
        string entityParameter,
        string entityType,
        string structMat,
        string cancellation,
        string indent,
        bool includeKey,
        bool isAwait,
        bool emitConcurrencyGuard = false,
        bool forUpdate = false)
    {
        var columns = SelectMutationColumns(entity, includeKey, forUpdate);

        // a ReturnEntity update on a token entity captures the (nullable) result so a null — which
        // otherwise conflates "stale token" with "row deleted" — can throw when the runtime option is set.
        if (emitConcurrencyGuard && entity.ConcurrencyToken is not null)
        {
            source.AppendLine($"{indent}var _result = await Inquiry.QueryGeneratedSingleOrDefaultAsync<{entityType}, {entityType}, {structMat}>(");
            source.AppendLine($"{indent}    new global::Inquiry.Commands.InquiryGeneratedCommand<{entityType}>(");
            source.AppendLine($"{indent}        {sqlField},");
            source.AppendLine($"{indent}        {entityParameter},");
            AppendBinderLambda(source, sqlBuilder, "_e", columns, i => $"_e.{columns[i].PropertyName}", indent + "        ", trailingComma: false);
            source.AppendLine($"{indent}    ),");
            source.AppendLine($"{indent}    default,");
            source.AppendLine($"{indent}    {cancellation}).ConfigureAwait(false);");
            source.AppendLine($"{indent}if (_result is null && Inquiry.ThrowOnConcurrencyConflict) throw new global::Inquiry.InquiryConcurrencyException();");
            source.AppendLine($"{indent}return _result;");
            return;
        }

        var awaitPrefix = isAwait ? "await " : string.Empty;
        var returnSuffix = isAwait ? ".ConfigureAwait(false)" : string.Empty;

        source.AppendLine($"{indent}return {awaitPrefix}Inquiry.QueryGeneratedSingleOrDefaultAsync<{entityType}, {entityType}, {structMat}>(");
        source.AppendLine($"{indent}    new global::Inquiry.Commands.InquiryGeneratedCommand<{entityType}>(");
        source.AppendLine($"{indent}        {sqlField},");
        source.AppendLine($"{indent}        {entityParameter},");
        AppendBinderLambda(source, sqlBuilder, "_e", columns, i => $"_e.{columns[i].PropertyName}", indent + "        ", trailingComma: false);
        source.AppendLine($"{indent}    ),");
        source.AppendLine($"{indent}    default,");
        source.AppendLine($"{indent}    {cancellation}){returnSuffix};");
    }

    private static void EmitStoredProcedure(StringBuilder source, SqlBuilder sqlBuilder, StoreMethodData method, string parameters, string entityType, string structMat, string cancellation, IReadOnlyDictionary<string, EntityData>? entities = null)
    {
        var procParams = Take(method.Parameters, method.Parameters.Count - 1).ToArray();
        var isAsyncEnum = method.ProcedureReturn == ProcedureReturnKind.AsyncEnumerableOfEntity;
        var isMultiResult = method.ProcedureReturn == ProcedureReturnKind.TaskOfMultipleResultSets;
        var isAsync = !isAsyncEnum;
        var hasScalarOutput = method.ProcedureReturn == ProcedureReturnKind.TaskOfOutputScalar;
        var state = new GeneratedCommandState(method.Parameters);

        var returnsRows = method.ProcedureReturn is ProcedureReturnKind.AsyncEnumerableOfEntity
            or ProcedureReturnKind.TaskOfEntity or ProcedureReturnKind.TaskOfMultipleResultSets;
        var resultSetCount = isMultiResult ? method.ProcedureResultSetTypeFqns.Count : 1;
        var refCursorCall = returnsRows
            ? sqlBuilder.BuildProcedureReaderCall(method.ProcedureName!, procParams.Select(static p => p.Name).ToList(), resultSetCount)
            : null;

        AppendHeader(source, method, parameters, isAsync: isAsync);

        var commandText = refCursorCall ?? method.ProcedureName!;
        source.AppendLine($"        var _cmd = new global::Inquiry.Commands.InquiryGeneratedCommand<{state.Type}>(");
        source.AppendLine($"            \"{GeneratorHelpers.Escape(commandText)}\",");
        source.AppendLine($"            {state.Value},");
        source.AppendLine($"            static (global::System.Data.Common.DbCommand _c, {state.Type} _args) =>");
        source.AppendLine("            {");
        AppendGeneratedStateAliases(source, method.Parameters, state, "                ");
        for (var i = 0; i < procParams.Length; i++)
        {
            var p = procParams[i];
            source.AppendLine($"                var _p{i} = _c.CreateParameter();");
            var paramName = refCursorCall is not null
                ? sqlBuilder.RuntimeParameterName(p.Name)
                : sqlBuilder.StoredProcedureParameterName(p.Name);
            source.AppendLine($"                _p{i}.ParameterName = \"{GeneratorHelpers.Escape(paramName)}\";");
            if (p.DbTypeExpression is not null)
            {
                source.AppendLine($"                _p{i}.DbType = {p.DbTypeExpression};");
            }
            if (p.DeclaredLength > 0 && (p.IsStringType || p.IsBinaryType) && !p.IsInputOutput)
            {
                source.AppendLine($"                _p{i}.Size = {p.DeclaredLength.ToString(CultureInfo.InvariantCulture)};");
            }
            else if (p.DeclaredPrecision is > 0 and <= 38 && p.IsDecimalType && !p.IsInputOutput)
            {
                source.AppendLine($"                _p{i}.Precision = {p.DeclaredPrecision.ToString(CultureInfo.InvariantCulture)};");
                if (p.DeclaredScale > 0 && p.DeclaredScale <= p.DeclaredPrecision)
                {
                    source.AppendLine($"                _p{i}.Scale = {p.DeclaredScale.ToString(CultureInfo.InvariantCulture)};");
                }
            }
            if (p.IsInputOutput)
            {
                source.AppendLine($"                _p{i}.Direction = global::System.Data.ParameterDirection.InputOutput;");
                if (p.IsStringType || p.IsBinaryType)
                {
                    var size = p.DeclaredLength > 0 ? p.DeclaredLength.ToString(CultureInfo.InvariantCulture) : "-1";
                    source.AppendLine($"                _p{i}.Size = {size};");
                }
                else if (p.IsDecimalType)
                {
                    var precision = p.DeclaredPrecision is > 0 and <= 38 ? p.DeclaredPrecision : 38;
                    var scale = p.DeclaredScale > 0 && p.DeclaredScale <= precision ? p.DeclaredScale : 10;
                    source.AppendLine($"                _p{i}.Precision = {precision.ToString(CultureInfo.InvariantCulture)};");
                    source.AppendLine($"                _p{i}.Scale = {scale.ToString(CultureInfo.InvariantCulture)};");
                }
            }
            var valueExpr = p.ProcedureValueExpression ?? $"(object?){p.Name} ?? global::System.DBNull.Value";
            source.AppendLine($"                _p{i}.Value = {valueExpr};");
            source.AppendLine($"                _c.Parameters.Add(_p{i});");
        }

        if (hasScalarOutput && method.ProcedureInOutParameterName is null)
        {
            var index = procParams.Length;
            source.AppendLine($"                var _p{index} = _c.CreateParameter();");
            source.AppendLine($"                _p{index}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.StoredProcedureParameterName(method.ProcedureReadBackName!))}\";");
            source.AppendLine($"                _p{index}.Value = {(method.ProcedureReturnsValue ? "0" : "global::System.DBNull.Value")};");
            source.AppendLine($"                _p{index}.Direction = global::System.Data.ParameterDirection.{(method.ProcedureReturnsValue ? "ReturnValue" : "Output")};");
            if (!method.ProcedureReturnsValue && method.ProcedureOutputDbType is not null)
            {
                source.AppendLine($"                _p{index}.DbType = {method.ProcedureOutputDbType};");
            }
            if (!method.ProcedureReturnsValue && method.ProcedureOutputIsString)
            {
                source.AppendLine($"                _p{index}.Size = -1;");
            }
            else if (!method.ProcedureReturnsValue && method.ProcedureOutputIsDecimal)
            {
                source.AppendLine($"                _p{index}.Precision = (byte)38;");
                source.AppendLine($"                _p{index}.Scale = (byte)10;");
            }
            source.AppendLine($"                _c.Parameters.Add(_p{index});");
        }
        source.AppendLine("            },");
        var commandTypeExpr = refCursorCall is not null
            ? "global::System.Data.CommandType.Text"
            : "global::System.Data.CommandType.StoredProcedure";
        source.AppendLine($"            {commandTypeExpr});");

        if (isMultiResult)
        {
            EmitMultiResultSetReturn(source, method, state, cancellation, entities!);
        }
        else
        {
            switch (method.ProcedureReturn)
            {
                case ProcedureReturnKind.AsyncEnumerableOfEntity:
                    source.AppendLine($"        return Inquiry.QueryAsync<{entityType}, {state.Type}, {structMat}>(_cmd, default, {cancellation});");
                    break;
                case ProcedureReturnKind.TaskOfEntity:
                    source.AppendLine($"        return await Inquiry.QuerySingleOrDefaultAsync<{entityType}, {state.Type}, {structMat}>(_cmd, default, {cancellation}).ConfigureAwait(false);");
                    break;
                case ProcedureReturnKind.TaskOfInt:
                    source.AppendLine($"        return await Inquiry.ExecuteAsync(_cmd, {cancellation}).ConfigureAwait(false);");
                    break;
                case ProcedureReturnKind.TaskOfOutputScalar:
                    source.AppendLine($"        return await Inquiry.ExecuteProcedureScalarAsync<{method.ScalarResultType}, {state.Type}>(_cmd, \"{GeneratorHelpers.Escape(sqlBuilder.StoredProcedureParameterName(method.ProcedureReadBackName!))}\", {cancellation}).ConfigureAwait(false);");
                    break;
            }
        }

        source.AppendLine("    }");
    }

    private static void EmitMultiResultSetReturn(StringBuilder source, StoreMethodData method, GeneratedCommandState state, string cancellation, IReadOnlyDictionary<string, EntityData> entities)
    {
        var fqns = method.ProcedureResultSetTypeFqns.AsImmutableArray();
        source.AppendLine($"        await using var _grid = await Inquiry.QueryMultipleAsync<{state.Type}>(_cmd, {cancellation}).ConfigureAwait(false);");
        for (var i = 0; i < fqns.Length; i++)
        {
            var fqn = fqns[i];
            var structMat = entities[fqn].StructMaterializerFullName;
            source.AppendLine($"        var _r{i} = await _grid.ReadListAsync<{fqn}, {structMat}>(default, {cancellation}).ConfigureAwait(false);");
        }
        var tupleElements = string.Join(", ", Enumerable.Range(0, fqns.Length).Select(static i => $"_r{i}"));
        source.AppendLine($"        return ({tupleElements});");
    }

    /// <summary>
    /// Builds the OUTPUT / RETURN-value <c>InquiryParameter</c> the pipeline reads back. A RETURN
    /// value is an integer seeded to 0 with <c>ParameterDirection.ReturnValue</c>; an OUTPUT
    /// parameter starts as <c>DBNull</c> with its DbType (and <c>Size = -1</c> for variable-length
    /// strings so providers allocate a read-back buffer).
    /// </summary>
    private static string BuildProcedureOutputParameter(SqlBuilder sqlBuilder, StoreMethodData method)
    {
        var name = "\"" + GeneratorHelpers.Escape(sqlBuilder.StoredProcedureParameterName(method.ProcedureReadBackName!)) + "\"";
        if (method.ProcedureReturnsValue)
        {
            return $"new global::Inquiry.Parameters.InquiryParameter({name}, 0, direction: global::System.Data.ParameterDirection.ReturnValue)";
        }

        var args = new StringBuilder($"new global::Inquiry.Parameters.InquiryParameter({name}, global::System.DBNull.Value");
        if (method.ProcedureOutputDbType is not null)
        {
            args.Append($", dbType: {method.ProcedureOutputDbType}");
        }

        args.Append(", direction: global::System.Data.ParameterDirection.Output");
        if (method.ProcedureOutputIsString)
        {
            args.Append(", size: -1");
        }
        else if (method.ProcedureOutputIsDecimal)
        {
            // SqlClient defaults a decimal output parameter to scale 0 and rounds the read-back
            // value (19.75 → 20). Stamp a high-fidelity precision/scale: 38/10 preserves money and
            // typical computed decimals losslessly, with ample integer headroom.
            args.Append(", precision: (byte)38, scale: (byte)10");
        }

        return args.Append(')').ToString();
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

    private static string BuildBatchDescriptorFieldName(StoreMethodData method)
    {
        var signature = method.Name + "(" + string.Join(",", method.Parameters.AsImmutableArray().Select(static parameter => parameter.TypeDisplay)) + ")";
        byte[] hash;
        using (var sha = SHA256.Create())
        {
            hash = sha.ComputeHash(Encoding.UTF8.GetBytes(signature));
        }

        var suffix = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
        {
            suffix.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return "_batch_" + method.Name + "_" + suffix;
    }

    private static string NonNullBatchItemsExpression(ParameterData parameter)
    {
        var elementType = parameter.ElementComparisonDisplay
            ?? throw new InvalidOperationException("A batch collection parameter must expose its element type.");
        return $"((global::System.Collections.Generic.IEnumerable<{elementType}>?){parameter.Name}) ?? global::System.Array.Empty<{elementType}>()";
    }

    /// <summary>
    /// Emits the cached whole-chunk descriptor for one multi-row insert operation. The runtime owns
    /// list slicing, bounded buffering, single enumeration, and transaction coordination; generated
    /// code owns only cardinality-specific SQL and one exact-once chunk binder.
    /// </summary>
    public static void EmitInsertAllSupport(
        StringBuilder source,
        StoreMethodData method,
        EntityData entity,
        SqlBuilder sqlBuilder)
    {
        var entityType = entity.FullyQualifiedName;
        var insertable = SelectMutationColumns(entity, includeKey: false);
        var prefix = BuildBatchDescriptorFieldName(method);

        if (insertable.Length == 0)
        {
            source.AppendLine($"    private static readonly global::Inquiry.Commands.InquiryBatchCommand<{entityType}> {prefix} = new(");
            source.AppendLine("        _sqlInsert,");
            source.AppendLine("        static (_, _it) =>");
            source.AppendLine("        {");
            EmitSequentialGuidAssignment(source, entity, "_it", indent: "            ", sqlBuilder);
            EmitAuditAssignments(source, entity, "_it", isInsert: true, indent: "            ");
            if (sqlBuilder.BatchInsertStrategy == BatchInsertStrategy.Row)
            {
                source.AppendLine("        },");
                source.AppendLine("        global::System.Data.CommandType.Text,");
                source.AppendLine("        bindChunk: null,");
                source.AppendLine("        preferPrepareOnce: true);");
            }
            else
            {
                source.AppendLine("        });");
            }
            source.AppendLine();
            return;
        }

        var parametersPerItem = Math.Max(insertable.Length, 1);
        var hardParameterRows = sqlBuilder.HardMaxParametersPerCommand / parametersPerItem;
        var setBasedMaxItemsPerCommand = Math.Min(sqlBuilder.BatchInsertMaxRowsPerCommand, hardParameterRows);
        var maxItemsPerCommand = sqlBuilder.BatchInsertStrategy == BatchInsertStrategy.Adaptive
            ? sqlBuilder.BatchInsertMaxRowsPerCommand
            : setBasedMaxItemsPerCommand;

        if (sqlBuilder.UsesArrayBindingForBatchMutations)
        {
            source.AppendLine($"    private static readonly global::Inquiry.Commands.InquiryBatchCommand<{entityType}> {prefix} = new(");
            source.AppendLine("        _sqlInsert,");
            EmitInsertRowBinder(source, entity, insertable, sqlBuilder, closingSuffix: ",");
            source.AppendLine("        bindChunk: static (_cmd, _items) =>");
            source.AppendLine("        {");
            EmitEntityArrayChunkBinder(source, entity, insertable, sqlBuilder, isInsert: true, indent: "            ");
            source.AppendLine("        });");
            source.AppendLine();
            return;
        }

        if (sqlBuilder.BatchInsertStrategy == BatchInsertStrategy.Row)
        {
            source.AppendLine($"    private static readonly global::Inquiry.Commands.InquiryBatchCommand<{entityType}> {prefix} = new(");
            source.AppendLine("        _sqlInsert,");
            EmitInsertRowBinder(source, entity, insertable, sqlBuilder, closingSuffix: ",");
            source.AppendLine("        global::System.Data.CommandType.Text,");
            source.AppendLine("        bindChunk: null,");
            source.AppendLine("        preferPrepareOnce: true);");
            source.AppendLine();
            return;
        }

        source.AppendLine($"    private static readonly global::Inquiry.Commands.InquiryBatchCommand<{entityType}> {prefix} = new(");
        if (sqlBuilder.BatchInsertStrategy == BatchInsertStrategy.Adaptive)
        {
            source.AppendLine("        _sqlInsert,");
            EmitInsertRowBinder(source, entity, insertable, sqlBuilder, closingSuffix: ",");
        }
        source.AppendLine("        static _count =>");
        source.AppendLine("        {");
        var estimatedInsertRowLength = sqlBuilder.BatchInsertRowClose.Length + sqlBuilder.BatchInsertRowSeparator.Length +
            insertable.Length * 24;
        source.AppendLine($"            var _sql = new global::System.Text.StringBuilder(_sqlInsertAllPrefix, _sqlInsertAllPrefix.Length + (_count * (_sqlInsertAllRowOpen.Length + {estimatedInsertRowLength})));");
        source.AppendLine("            for (var _r = 0; _r < _count; _r++)");
        source.AppendLine("            {");
        source.AppendLine($"                if (_r > 0) _sql.Append(\"{GeneratorHelpers.Escape(sqlBuilder.BatchInsertRowSeparator)}\");");
        source.AppendLine("                _sql.Append(_sqlInsertAllRowOpen);");
        for (var i = 0; i < insertable.Length; i++)
        {
            var segment = i == 0 ? sqlBuilder.BatchInsertSqlParameterPrefix : ", " + sqlBuilder.BatchInsertSqlParameterPrefix;
            source.AppendLine($"                _sql.Append(\"{GeneratorHelpers.Escape(segment)}\").Append(_r).Append(\"_{i}\");");
        }
        source.AppendLine($"                _sql.Append(\"{GeneratorHelpers.Escape(sqlBuilder.BatchInsertRowClose)}\");");
        source.AppendLine("            }");
        if (sqlBuilder.BatchInsertFooter.Length > 0)
        {
            source.AppendLine($"            _sql.Append(\"{GeneratorHelpers.Escape(sqlBuilder.BatchInsertFooter)}\");");
        }
        source.AppendLine("            return _sql.ToString();");
        source.AppendLine("        },");
        source.AppendLine("        static (_cmd, _items) =>");
        source.AppendLine("        {");
        source.AppendLine("            for (var _r = 0; _r < _items.Count; _r++)");
        source.AppendLine("            {");
        source.AppendLine("                var _it = _items[_r];");
        EmitSequentialGuidAssignment(source, entity, "_it", indent: "                ", sqlBuilder);
        EmitAuditAssignments(source, entity, "_it", isInsert: true, indent: "                ");
        for (var i = 0; i < insertable.Length; i++)
        {
            var column = insertable[i];
            source.AppendLine("                {");
            source.AppendLine("                    var _p = _cmd.CreateParameter();");
            source.AppendLine($"                    _p.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.BatchInsertRuntimeParameterPrefix)}\" + _r + \"_{i}\";");
            AppendColumnParameterMetadata(source, column, sqlBuilder, "_p", "                    ", predicate: false);
            source.AppendLine($"                    _p.Value = {BuildParameterValueExpression(column, "_it." + column.PropertyName, sqlBuilder)};");
            source.AppendLine("                    _cmd.Parameters.Add(_p);");
            source.AppendLine("                }");
        }
        source.AppendLine("            }");
        source.AppendLine("        },");
        if (sqlBuilder.BatchInsertStrategy == BatchInsertStrategy.Adaptive)
        {
            source.AppendLine($"        static _items => _items.Count < {sqlBuilder.BatchInsertAdaptiveThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        }
        source.AppendLine($"        parametersPerItem: {parametersPerItem.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        if (sqlBuilder.BatchInsertStrategy == BatchInsertStrategy.Adaptive)
        {
            source.AppendLine($"        maxItemsPerCommand: {maxItemsPerCommand.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            source.AppendLine("        commandType: global::System.Data.CommandType.Text,");
            source.AppendLine($"        setBasedMaxItemsPerCommand: {setBasedMaxItemsPerCommand.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
        }
        else
        {
            source.AppendLine($"        maxItemsPerCommand: {maxItemsPerCommand.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
        }
        source.AppendLine();
    }

    private static void EmitInsertRowBinder(
        StringBuilder source,
        EntityData entity,
        IReadOnlyList<ColumnData> columns,
        SqlBuilder sqlBuilder,
        string closingSuffix)
    {
        source.AppendLine("        static (_t, _it) =>");
        source.AppendLine("        {");
        EmitSequentialGuidAssignment(source, entity, "_it", indent: "            ", sqlBuilder);
        EmitAuditAssignments(source, entity, "_it", isInsert: true, indent: "            ");
        EmitTargetRowParameters(source, columns, sqlBuilder, "_it", indent: "            ");
        source.AppendLine("        }" + closingSuffix);
    }

    private static void EmitTargetRowParameters(
        StringBuilder source,
        IReadOnlyList<ColumnData> columns,
        SqlBuilder sqlBuilder,
        string itemExpression,
        string indent)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            source.AppendLine($"{indent}var _p{i} = _t.CreateParameter();");
            source.AppendLine($"{indent}_p{i}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(column.PropertyName))}\";");
            AppendColumnParameterMetadata(source, column, sqlBuilder, $"_p{i}", indent, predicate: false);
            source.AppendLine($"{indent}_p{i}.Value = {BuildParameterValueExpression(column, itemExpression + "." + column.PropertyName, sqlBuilder)};");
            source.AppendLine($"{indent}_t.AddParameter(_p{i});");
        }
    }

    private static void EmitEntityArrayChunkBinder(
        StringBuilder source,
        EntityData entity,
        IReadOnlyList<ColumnData> columns,
        SqlBuilder sqlBuilder,
        bool isInsert,
        string indent)
    {
        source.AppendLine($"{indent}{sqlBuilder.BuildArrayBindCountAssignment("_cmd", "_items.Count")}");
        for (var i = 0; i < columns.Count; i++)
        {
            source.AppendLine($"{indent}var _values{i} = new object?[_items.Count];");
            if (sqlBuilder.BuildArrayBindSizeExpression($"_values{i}[_i]", $"_value{i}", columns[i]) is not null)
            {
                source.AppendLine($"{indent}var _sizes{i} = new int[_items.Count];");
            }
        }
        source.AppendLine($"{indent}for (var _i = 0; _i < _items.Count; _i++)");
        source.AppendLine($"{indent}{{");
        source.AppendLine($"{indent}    var _it = _items[_i];");
        if (isInsert)
        {
            EmitSequentialGuidAssignment(source, entity, "_it", indent + "    ", sqlBuilder);
        }
        EmitAuditAssignments(source, entity, "_it", isInsert, indent + "    ");
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            source.AppendLine($"{indent}    _values{i}[_i] = {BuildParameterValueExpression(column, "_it." + column.PropertyName, sqlBuilder)};");
            if (sqlBuilder.BuildArrayBindSizeExpression($"_values{i}[_i]", $"_value{i}", column) is { } sizeExpression)
            {
                source.AppendLine($"{indent}    _sizes{i}[_i] = {sizeExpression};");
            }
        }
        source.AppendLine($"{indent}}}");

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            source.AppendLine($"{indent}var _p{i} = _cmd.CreateParameter();");
            source.AppendLine($"{indent}_p{i}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(column.PropertyName))}\";");
            AppendColumnParameterMetadata(source, column, sqlBuilder, $"_p{i}", indent, predicate: false);
            if (sqlBuilder.BuildArrayBindParameterMetadata($"_p{i}", column) is { } providerMetadata)
            {
                source.AppendLine($"{indent}{providerMetadata}");
            }
            source.AppendLine($"{indent}_p{i}.Value = _values{i};");
            if (sqlBuilder.BuildArrayBindSizeExpression($"_values{i}[_i]", $"_value{i}", column) is not null)
            {
                source.AppendLine($"{indent}{sqlBuilder.BuildArrayBindSizeAssignment($"_p{i}", $"_sizes{i}")}");
            }
            source.AppendLine($"{indent}_cmd.Parameters.Add(_p{i});");
        }
    }

    /// <summary>
    /// Emits the cached immutable descriptor used by one generated UpdateAll method. Audit values and
    /// converted provider values are produced in the row binder so they are evaluated exactly once when
    /// the runtime admits that item to its bounded chunk.
    /// </summary>
    public static void EmitUpdateAllDescriptor(
        StringBuilder source,
        StoreMethodData method,
        EntityData entity,
        SqlBuilder sqlBuilder)
    {
        var entityType = entity.FullyQualifiedName;
        var updateColumns = SelectMutationColumns(entity, includeKey: true, forUpdate: true);
        var setColumns = updateColumns.Where(static column => !column.IsKey).ToArray();
        var useSetBasedChunk = sqlBuilder.SupportsSetBasedBatchUpdate &&
            setColumns.Length > 0 &&
            entity.Keys.AsImmutableArray().All(IsSafeSetBasedUpdateKey);
        var prefix = BuildBatchDescriptorFieldName(method);
        source.AppendLine($"    private static readonly global::Inquiry.Commands.InquiryBatchCommand<{entityType}> {prefix} = new(");
        source.AppendLine("        _sqlUpdate,");
        source.AppendLine("        static (_t, _it) =>");
        source.AppendLine("        {");
        EmitAuditAssignments(source, entity, "_it", isInsert: false, indent: "            ");
        for (var i = 0; i < updateColumns.Length; i++)
        {
            var column = updateColumns[i];
            source.AppendLine($"            var _p{i} = _t.CreateParameter();");
            source.AppendLine($"            _p{i}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(column.PropertyName))}\";");
            AppendColumnParameterMetadata(source, column, sqlBuilder, $"_p{i}", "            ", predicate: false);
            source.AppendLine($"            _p{i}.Value = {BuildParameterValueExpression(column, "_it." + column.PropertyName, sqlBuilder)};");
            source.AppendLine($"            _t.AddParameter(_p{i});");
        }

        if (sqlBuilder.UsesArrayBindingForBatchMutations)
        {
            source.AppendLine("        },");
            source.AppendLine("        bindChunk: static (_cmd, _items) =>");
            source.AppendLine("        {");
            EmitEntityArrayChunkBinder(source, entity, updateColumns, sqlBuilder, isInsert: false, indent: "            ");
            source.AppendLine("        });");
        }
        else if (useSetBasedChunk)
        {
            EmitSetBasedUpdateChunk(source, entity, updateColumns, setColumns, sqlBuilder);
        }
        else
        {
            source.AppendLine("        });");
        }
        source.AppendLine();
    }

    private static void EmitSetBasedUpdateChunk(
        StringBuilder source,
        EntityData entity,
        IReadOnlyList<ColumnData> updateColumns,
        IReadOnlyList<ColumnData> setColumns,
        SqlBuilder sqlBuilder)
    {
        var header = sqlBuilder.BuildSetBasedBatchUpdateHeader(entity.Schema, entity.TableName);
        var footer = sqlBuilder.BuildSetBasedBatchUpdateFooter(
            entity.Schema,
            entity.TableName,
            entity.Keys.AsImmutableArray(),
            setColumns);
        var parametersPerItem = updateColumns.Count;
        var maxItemsPerCommand = sqlBuilder.HardMaxParametersPerCommand / parametersPerItem;

        source.AppendLine("        },");
        source.AppendLine("        static _count =>");
        source.AppendLine("        {");
        var estimatedUpdateRowLength = 18 + updateColumns.Count * 24;
        source.AppendLine($"            var _sql = new global::System.Text.StringBuilder(\"{GeneratorHelpers.Escape(header)}\", {header.Length + footer.Length} + (_count * {estimatedUpdateRowLength}));");
        source.AppendLine("            for (var _r = 0; _r < _count; _r++)");
        source.AppendLine("            {");
        source.AppendLine("                _sql.Append(_r == 0 ? \"SELECT \" : \" UNION ALL SELECT \");");
        for (var i = 0; i < updateColumns.Count; i++)
        {
            var column = updateColumns[i];
            if (i > 0)
            {
                source.AppendLine("                _sql.Append(\", \");");
            }
            source.AppendLine($"                _sql.Append(\"@_u\").Append(_r).Append(\"_{i}\");");
            source.AppendLine($"                if (_r == 0) _sql.Append(\" AS {GeneratorHelpers.Escape(sqlBuilder.QuoteIdentifier(column.ColumnName))}\");");
        }
        source.AppendLine("            }");
        source.AppendLine($"            return _sql.Append(\"{GeneratorHelpers.Escape(footer)}\").ToString();");
        source.AppendLine("        },");
        source.AppendLine("        static (_cmd, _items) =>");
        source.AppendLine("        {");
        source.AppendLine("            for (var _r = 0; _r < _items.Count; _r++)");
        source.AppendLine("            {");
        source.AppendLine("                var _it = _items[_r];");
        EmitAuditAssignments(source, entity, "_it", isInsert: false, indent: "                ");
        for (var i = 0; i < updateColumns.Count; i++)
        {
            var column = updateColumns[i];
            source.AppendLine("                {");
            source.AppendLine("                    var _p = _cmd.CreateParameter();");
            source.AppendLine($"                    _p.ParameterName = \"@_u\" + _r + \"_{i}\";");
            AppendColumnParameterMetadata(source, column, sqlBuilder, "_p", "                    ", predicate: false);
            source.AppendLine($"                    _p.Value = {BuildParameterValueExpression(column, "_it." + column.PropertyName, sqlBuilder)};");
            source.AppendLine("                    _cmd.Parameters.Add(_p);");
            source.AppendLine("                }");
        }
        source.AppendLine("            }");
        source.AppendLine("        },");
        EmitSetBasedUpdateSelector(source, entity.Keys.AsImmutableArray());
        source.AppendLine($"        parametersPerItem: {parametersPerItem.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        source.AppendLine($"        maxItemsPerCommand: {maxItemsPerCommand.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
    }

    private static void EmitSetBasedUpdateSelector(StringBuilder source, IReadOnlyList<ColumnData> keys)
    {
        var keyType = keys.Count == 1
            ? keys[0].Type.DisplayName
            : "(" + string.Join(", ", keys.Select(static key => key.Type.DisplayName)) + ")";
        var keyValue = keys.Count == 1
            ? "_items[_i]." + keys[0].PropertyName
            : "(" + string.Join(", ", keys.Select(static key => "_items[_i]." + key.PropertyName)) + ")";

        source.AppendLine("        static _items =>");
        source.AppendLine("        {");
        source.AppendLine("            if (_items.Count < 2) return false;");
        source.AppendLine($"            var _keys = new global::System.Collections.Generic.HashSet<{keyType}>(_items.Count);");
        source.AppendLine("            for (var _i = 0; _i < _items.Count; _i++)");
        source.AppendLine("            {");
        source.AppendLine($"                if (!_keys.Add({keyValue})) return false;");
        source.AppendLine("            }");
        source.AppendLine("            return true;");
        source.AppendLine("        },");
    }

    private static bool IsSafeSetBasedUpdateKey(ColumnData key)
        => !key.IsNullable &&
           key.Converter is null &&
           !key.EnumAsString &&
           key.TypeClass is DbTypeClass.Boolean or DbTypeClass.Byte or DbTypeClass.Int16 or
               DbTypeClass.Int32 or DbTypeClass.Int64 or DbTypeClass.Decimal or DbTypeClass.Guid;

    /// <summary>
    /// Emits a cached descriptor for DeleteAll. Array-DML dialects use the fixed single-key statement
    /// plus an array binder; other dialects use one bounded collection-parameter command per chunk.
    /// </summary>
    public static void EmitDeleteAllDescriptor(
        StringBuilder source,
        StoreMethodData method,
        EntityData entity,
        SqlBuilder sqlBuilder,
        CollectionParameterResolution resolution)
    {
        var keyType = entity.Keys[0].Type.DisplayName;
        var prefix = BuildBatchDescriptorFieldName(method);
        source.AppendLine($"    private static readonly global::Inquiry.Commands.InquiryBatchCommand<{keyType}> {prefix} = new(");

        if (sqlBuilder.UsesArrayBindingForBatchMutations)
        {
            var key = entity.Keys[0];
            source.AppendLine("        _sqlDeleteAllItem,");
            source.AppendLine("        static (_t, _key) =>");
            source.AppendLine("        {");
            source.AppendLine("            var _p = _t.CreateParameter();");
            source.AppendLine($"            _p.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(key.PropertyName))}\";");
            AppendColumnParameterMetadata(source, key, sqlBuilder, "_p", "            ", predicate: true);
            source.AppendLine($"            _p.Value = {BuildParameterValueExpression(key, "_key", sqlBuilder)};");
            source.AppendLine("            _t.AddParameter(_p);");
            source.AppendLine("        },");
            source.AppendLine("        bindChunk: static (_cmd, _keys) =>");
            source.AppendLine("        {");
            source.AppendLine($"            {sqlBuilder.BuildArrayBindCountAssignment("_cmd", "_keys.Count")}");
            source.AppendLine("            var _values = new object?[_keys.Count];");
            if (sqlBuilder.BuildArrayBindSizeExpression("_values[_i]", "_value", key) is not null)
            {
                source.AppendLine("            var _sizes = new int[_keys.Count];");
            }
            source.AppendLine("            for (var _i = 0; _i < _keys.Count; _i++)");
            source.AppendLine("            {");
            source.AppendLine($"                _values[_i] = {BuildParameterValueExpression(key, "_keys[_i]", sqlBuilder)};");
            if (sqlBuilder.BuildArrayBindSizeExpression("_values[_i]", "_value", key) is { } sizeExpression)
            {
                source.AppendLine($"                _sizes[_i] = {sizeExpression};");
            }
            source.AppendLine("            }");
            source.AppendLine("            var _p = _cmd.CreateParameter();");
            source.AppendLine($"            _p.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(key.PropertyName))}\";");
            AppendColumnParameterMetadata(source, key, sqlBuilder, "_p", "            ", predicate: true);
            if (sqlBuilder.BuildArrayBindParameterMetadata("_p", key) is { } providerMetadata)
            {
                source.AppendLine($"            {providerMetadata}");
            }
            source.AppendLine("            _p.Value = _values;");
            if (sqlBuilder.BuildArrayBindSizeExpression("_values[_i]", "_value", key) is not null)
            {
                source.AppendLine($"            {sqlBuilder.BuildArrayBindSizeAssignment("_p", "_sizes")}");
            }
            source.AppendLine("            _cmd.Parameters.Add(_p);");
            source.AppendLine("        });");
            source.AppendLine();
            return;
        }

        source.AppendLine("        static _ => _sqlDeleteAll,");
        source.AppendLine("        static (_c, _keys) =>");
        source.AppendLine("        {");
        source.AppendLine(CollectionBindingExpression(
            sqlBuilder,
            entity.Keys[0],
            entity.Schema,
            sqlBuilder.ParameterName("keys"),
            "_keys",
            isNegatedCollection: false,
            elementIsNullable: method.Parameters[0].ElementIsNullable,
            resolution: resolution,
            maxParametersExpression: "0"));
        source.AppendLine("        },");
        source.AppendLine("        parametersPerItem: 0,");
        source.AppendLine("        maxItemsPerCommand: global::System.Int32.MaxValue);");
        source.AppendLine();
    }

    private static string NonNullableValueExpression(TypeData type, string accessor)
    {
        if (!type.IsNullable) return accessor;
        return type.IsValueType ? $"{accessor}.Value" : $"{accessor}!";
    }

    /// <summary>
    /// Emits runtime binding for a column-backed collection used by IN/NOT IN predicates and DeleteAll.
    /// A NOT IN collection always uses the sentinel <c>ExpandNotIn</c> (so an empty collection is
    /// dialect-uniform); a plain IN/DeleteAll uses the dialect's array bind (when supported) or the
    /// sentinel <c>Expand</c>.
    /// <para>
    /// When the column has a value transform (enum-as-string or a value converter) the raw collection
    /// is projected through that transform before being passed to the runtime helper, mirroring what
    /// <see cref="BuildParameterValueExpression"/> does for scalar predicates.
    /// </para>
    /// </summary>
    private static string CollectionBindingExpression(
        SqlBuilder sqlBuilder,
        ColumnData column,
        string? owningSchema,
        string sqlParameterName,
        string arg,
        bool isNegatedCollection,
        bool elementIsNullable,
        CollectionParameterResolution? resolution,
        string maxParametersExpression = "Inquiry.MaxParametersPerCommand")
    {
        var name = GeneratorHelpers.Escape(sqlParameterName);
        var projected = ProjectedCollectionExpression(sqlBuilder, column, arg, elementIsNullable);
        // Stamp the same DbType the scalar binder resolves for this column so an IN element binds with
        // the right type (e.g. DateTime2 on SQL Server, not legacy datetime). The array path leaves it
        // to the provider, which infers the element type from the typed native array.
        var dbType = ResolveDbType(column, sqlBuilder);
        var dbTypeArg = dbType is null ? string.Empty : $", dbType: {dbType}";
        // Carry the declared Size/Precision/Scale of the IN column onto each element parameter so the
        // sp_executesql signature stays stable across value lengths (SQL Server only — same gating as the
        // scalar predicate path; #56/#102). The Expand/ExpandNotIn overload that takes these requires a
        // dbType, but a declared string/decimal column always resolves a non-null DbType (String/AnsiString/
        // Decimal), so dbTypeArg is non-empty whenever sizePrecisionArgs is — the call always type-checks.
        var sizePrecisionArgs = BuildSizePrecisionArgs(column, sqlBuilder);
        if (isNegatedCollection)
        {
            return $"                global::Inquiry.Parameters.InquiryInExpansion.ExpandNotIn(_c, \"{name}\", {projected}, {maxParametersExpression}{dbTypeArg}{sizePrecisionArgs});";
        }

        if (sqlBuilder.UseArrayInParameters)
        {
            if (resolution is null || !resolution.IsValid)
                throw new InvalidOperationException("Collection transport must be resolved once before method emission.");
            return "                " + sqlBuilder.BuildCollectionParameterBinding(
                new CollectionParameterBindingContext(resolution, "_c", name, projected));
        }

        return $"                global::Inquiry.Parameters.InquiryInExpansion.Expand(_c, \"{name}\", {projected}, {maxParametersExpression}{dbTypeArg}{sizePrecisionArgs});";
    }

    /// <summary>
    /// Returns the collection expression to pass to the runtime IN/NOT IN helper. When the column
    /// stores a transformed representation (converter or enum-as-string) the raw enumerable is wrapped
    /// in an <c>Enumerable.Select</c> projection so every element reaches the helper in provider form —
    /// converter first, mirroring the scalar binder and materializer. The projection preserves the two
    /// null-handling guarantees of the scalar path: a null collection flows through unprojected (the
    /// helpers treat null as empty, but <c>Enumerable.Select(null, …)</c> would throw), and a
    /// nullable value/reference converter model binds <c>null</c> rather than calling
    /// <c>ToProvider</c> for that element.
    /// </summary>
    private static string ProjectedCollectionExpression(
        SqlBuilder sqlBuilder,
        ColumnData column,
        string arg,
        bool elementIsNullable)
    {
        var nullableModelElement = elementIsNullable
            || (column.Converter is not null && !column.Type.IsValueType);
        var value = nullableModelElement
            ? column.Type.IsValueType ? "_e.Value" : "_e"
            : "_e";
        string providerValue;
        string providerTypeName;
        Microsoft.CodeAnalysis.SpecialType providerSpecialType;
        string? nullableResultType = null;
        var requiresProjection = false;

        if (column.Converter is { } converter)
        {
            providerValue = ConverterInvocationEmitter.ToProvider(converter, value);
            providerTypeName = converter.ProviderType?.NonNullableDisplayName ?? converter.ProviderTypeDisplay;
            providerSpecialType = converter.ProviderSpecialType;
            nullableResultType = converter.ProviderType?.IsValueType == true
                ? converter.ProviderType.NonNullableDisplayName + "?"
                : converter.ProviderTypeDisplay + "?";
            requiresProjection = true;
        }
        else if (column.EnumAsString)
        {
            providerValue = $"{value}.ToString()";
            providerTypeName = "global::System.String";
            providerSpecialType = Microsoft.CodeAnalysis.SpecialType.System_String;
            requiresProjection = true;
        }
        else
        {
            providerSpecialType = column.Type.IsEnum ? column.Type.EnumUnderlyingSpecialType : column.Type.SpecialType;
            providerTypeName = column.Type.IsEnum ? SpecialTypeName(providerSpecialType) : column.Type.NonNullableDisplayName;
            providerValue = column.Type.IsEnum ? $"({providerTypeName}){value}" : value;
            requiresProjection = column.Type.IsEnum;
        }

        var storage = sqlBuilder.BuildCollectionElementExpression(new CollectionElementExpressionContext(
            providerValue, providerTypeName, providerSpecialType));
        requiresProjection |= storage.IsTransformed;
        if (!requiresProjection) return arg;
        if (storage.IsTransformed) nullableResultType = storage.StorageTypeName + "?";

        var selector = nullableModelElement
            ? column.Type.IsValueType
                ? $"static _e => _e.HasValue ? ({nullableResultType ?? storage.StorageTypeName + "?"}){storage.ValueExpression} : null"
                : $"static _e => _e is null ? ({nullableResultType ?? storage.StorageTypeName + "?"})null : {storage.ValueExpression}"
            : $"static _e => {storage.ValueExpression}";
        return NullGuardedSelect(arg, selector);
    }

    private static string SpecialTypeName(Microsoft.CodeAnalysis.SpecialType type) => type switch
    {
        Microsoft.CodeAnalysis.SpecialType.System_SByte => "global::System.SByte",
        Microsoft.CodeAnalysis.SpecialType.System_Byte => "global::System.Byte",
        Microsoft.CodeAnalysis.SpecialType.System_Int16 => "global::System.Int16",
        Microsoft.CodeAnalysis.SpecialType.System_UInt16 => "global::System.UInt16",
        Microsoft.CodeAnalysis.SpecialType.System_Int32 => "global::System.Int32",
        Microsoft.CodeAnalysis.SpecialType.System_UInt32 => "global::System.UInt32",
        Microsoft.CodeAnalysis.SpecialType.System_Int64 => "global::System.Int64",
        Microsoft.CodeAnalysis.SpecialType.System_UInt64 => "global::System.UInt64",
        _ => "global::System.Int32",
    };

    /// <summary>
    /// Wraps <paramref name="arg"/> in <c>Enumerable.Select(arg, selector)</c> behind a null guard so a
    /// null collection still reaches the runtime helper (which treats it as empty) instead of throwing.
    /// </summary>
    private static string NullGuardedSelect(string arg, string selector)
        => $"{arg} is null ? null : global::System.Linq.Enumerable.Select({arg}, {selector})";

    private static void EmitSelectOneByKeyEager(StringBuilder source, SqlBuilder sqlBuilder, StoreMethodData method, string parameters, string entityType, string cancellation, EntityData entity, Dictionary<string, EntityData> relationChildEntities, string parentSelectField)
    {
        // Single-round-trip (grid) path when EVERY relation can be expressed with the input key alone:
        // collection and many-to-many children filter by the parent key directly; reference (belongs-to)
        // children use a scalar subquery (SELECT parentFK FROM parent WHERE parentPK = @key) so the
        // value is resolved server-side without materializing the parent first. The grid path requires a
        // dialect that can return multiple result sets from one command — a ;-separated batch on most
        // dialects, Oracle's DBMS_SQL.RETURN_RESULT PL/SQL wrapper via the MultiResultBatch* hooks.
        var allKeyFilterable = entity.Relations.Count > 0 && sqlBuilder.SupportsMultiResultBatch;
        foreach (var relation in entity.Relations)
        {
            if (!relationChildEntities.ContainsKey(relation.PropertyName))
            {
                allKeyFilterable = false;
                break;
            }
        }

        if (allKeyFilterable)
        {
            EmitSelectOneByKeyEagerGrid(source, sqlBuilder, method, parameters, entityType, cancellation, entity, relationChildEntities, parentSelectField);
        }
        else
        {
            EmitSelectOneByKeyEagerSeparate(source, sqlBuilder, method, parameters, entityType, cancellation, entity, relationChildEntities, parentSelectField);
        }
    }

    // Appends the combined multi-result command text: each SELECT const joined through the dialect's
    // MultiResultBatch hooks — "" / ";" / "" on the ;-batching dialects (unchanged shape), Oracle's
    // DBMS_SQL.RETURN_RESULT PL/SQL wrapper otherwise. The hooks are emitted as string literals between
    // the const fields, so csc folds the whole expression at compile time.
    private static void AppendGridCommandText(StringBuilder source, SqlBuilder sqlBuilder, List<string> sqlFields)
    {
        source.Append("        var _sql = ");
        if (sqlBuilder.MultiResultBatchPrefix.Length > 0)
        {
            source.Append($"\"{GeneratorHelpers.Escape(sqlBuilder.MultiResultBatchPrefix)}\" + ");
        }

        for (var i = 0; i < sqlFields.Count; i++)
        {
            if (i > 0)
            {
                source.Append($" + \"{GeneratorHelpers.Escape(sqlBuilder.MultiResultBatchSeparator)}\" + ");
            }

            source.Append(sqlFields[i]);
        }

        if (sqlBuilder.MultiResultBatchSuffix.Length > 0)
        {
            source.Append($" + \"{GeneratorHelpers.Escape(sqlBuilder.MultiResultBatchSuffix)}\"");
        }

        source.AppendLine(";");
    }

    // Single round trip: one command with the parent SELECT + each key-filterable child SELECT, read in
    // order through an InquiryGridReader. Matches what Dapper (QueryMultiple), DLG (multi-result proc), and
    // a hand-written two-result-set ADO command do.
    private static void EmitSelectOneByKeyEagerGrid(StringBuilder source, SqlBuilder sqlBuilder, StoreMethodData method, string parameters, string entityType, string cancellation, EntityData entity, Dictionary<string, EntityData> relationChildEntities, string parentSelectField)
    {
        var keyParamName = method.Parameters[0].Name;
        var parentStructMat = entity.StructMaterializerFullName;
        var keyDbType = ResolveDbType(entity.Keys[0], sqlBuilder);
        var parentKeyProp = entity.Keys[0].PropertyName;
        AppendHeader(source, method, parameters, isAsync: true);

        // Combined command text: parent SELECT + each child SELECT, joined via the dialect's batch hooks.
        // Collection and M:N relations use the by-FK const; reference (belongs-to) relations use the
        // _ByKey subquery const so the FK value is resolved server-side in the same round trip.
        var sqlFields = new List<string> { parentSelectField };
        foreach (var relation in entity.Relations)
        {
            var suffix = relation.IsCollection || relation.IsManyToMany ? "" : "_ByKey";
            sqlFields.Add($"_sql_{relation.PropertyName}{suffix}");
        }
        AppendGridCommandText(source, sqlBuilder, sqlFields);

        // Deduped parameters, all bound to the input key value. Non-M:N collection children filter by
        // their own FK param; M:N and reference children use the parent-key param (already first).
        var paramNames = new List<string> { parentKeyProp };
        foreach (var relation in entity.Relations)
        {
            if (relation.IsCollection && !relation.IsManyToMany)
            {
                var paramName = relation.ForeignKeyProperty;
                if (!paramNames.Contains(paramName))
                    paramNames.Add(paramName);
            }
        }

        source.AppendLine("        await using var _grid = await Inquiry.QueryMultipleAsync(");
        source.AppendLine($"            new global::Inquiry.Commands.InquiryGeneratedCommand<{method.Parameters[0].TypeDisplay}>(");
        source.AppendLine("                _sql,");
        source.AppendLine($"                {keyParamName},");
        source.AppendLine($"                static (global::System.Data.Common.DbCommand _c, {method.Parameters[0].TypeDisplay} _key) =>");
        source.AppendLine("                {");
        for (var i = 0; i < paramNames.Count; i++)
        {
            var keyValue = BuildParameterValueExpression(entity.Keys[0], "_key", sqlBuilder);
            source.AppendLine($"                    var _p{i} = _c.CreateParameter();");
            source.AppendLine($"                    _p{i}.ParameterName = \"{GeneratorHelpers.Escape(sqlBuilder.RuntimeParameterName(paramNames[i]))}\";");
            if (keyDbType is not null) source.AppendLine($"                    _p{i}.DbType = {keyDbType};");
            AppendSizePrecision(source, entity.Keys[0], sqlBuilder, $"_p{i}", "                    ");
            source.AppendLine($"                    _p{i}.Value = {keyValue};");
            source.AppendLine($"                    _c.Parameters.Add(_p{i});");
        }
        source.AppendLine("                }),");
        source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
        source.AppendLine($"        var _entity = await _grid.ReadGeneratedSingleOrDefaultAsync<{entityType}, {parentStructMat}>(default, {cancellation}).ConfigureAwait(false);");
        source.AppendLine("        if (_entity is not null)");
        source.AppendLine("        {");
        foreach (var relation in entity.Relations)
        {
            var childEntity = relationChildEntities[relation.PropertyName];
            if (relation.IsCollection || relation.IsManyToMany)
                source.AppendLine($"            _entity.{relation.PropertyName} = await _grid.ReadListAsync<{childEntity.FullyQualifiedName}, {childEntity.StructMaterializerFullName}>(default, {cancellation}).ConfigureAwait(false);");
            else
                source.AppendLine($"            _entity.{relation.PropertyName} = await _grid.ReadGeneratedSingleOrDefaultAsync<{childEntity.FullyQualifiedName}, {childEntity.StructMaterializerFullName}>(default, {cancellation}).ConfigureAwait(false);");
        }
        source.AppendLine("        }");
        source.AppendLine("        return _entity;");
        source.AppendLine("    }");
    }

    private static void EmitSelectOneByKeyEagerSeparate(StringBuilder source, SqlBuilder sqlBuilder, StoreMethodData method, string parameters, string entityType, string cancellation, EntityData entity, Dictionary<string, EntityData> relationChildEntities, string parentSelectField)
    {
        // Eager-on-composite is rejected in validation, so entity.Keys.Count == 1 here.
        var keyParamName = method.Parameters[0].Name;
        // Bind the key/FK predicate parameters with their resolved DbType (e.g. AnsiString for a
        // non-unicode varchar key) so the eager-load lookups SEEK the varchar index instead of scanning —
        // mirroring the main (non-eager) param path. Without this the params default to inferred nvarchar.
        var parentStructMat = entity.StructMaterializerFullName;
        AppendHeader(source, method, parameters, isAsync: true);
        source.AppendLine($"        var _entity = await Inquiry.QueryGeneratedSingleOrDefaultAsync<{entityType}, {method.Parameters[0].TypeDisplay}, {parentStructMat}>(");
        AppendSingleParameterGeneratedCommand(source, sqlBuilder, parentSelectField, method.Parameters[0].TypeDisplay, keyParamName, entity.Keys[0].PropertyName, entity.Keys[0], "            ");
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
            if (relation.IsManyToMany)
            {
                // The JOIN const filters by this entity's key (bound as the parent key parameter).
                source.AppendLine($"            var _{relation.PropertyName}_list = new global::System.Collections.Generic.List<{childType}>();");
                source.AppendLine($"            await foreach (var _child in Inquiry.QueryAsync<{childType}, {method.Parameters[0].TypeDisplay}, {childStructMat}>(");
                AppendSingleParameterGeneratedCommand(source, sqlBuilder, fieldName, method.Parameters[0].TypeDisplay, "_entity." + entity.Keys[0].PropertyName, entity.Keys[0].PropertyName, entity.Keys[0], "                ");
                source.AppendLine("                default,");
                source.AppendLine($"                {cancellation}).ConfigureAwait(false))");
                source.AppendLine($"                _{relation.PropertyName}_list.Add(_child);");
                source.AppendLine($"            _entity.{relation.PropertyName} = _{relation.PropertyName}_list;");
                continue;
            }

            if (relation.IsCollection)
            {
                source.AppendLine($"            var _{relation.PropertyName}_list = new global::System.Collections.Generic.List<{childType}>();");
                source.AppendLine($"            await foreach (var _child in Inquiry.QueryAsync<{childType}, {method.Parameters[0].TypeDisplay}, {childStructMat}>(");
                AppendSingleParameterGeneratedCommand(source, sqlBuilder, fieldName, method.Parameters[0].TypeDisplay, "_entity." + entity.Keys[0].PropertyName, relation.ForeignKeyProperty, entity.Keys[0], "                ");
                source.AppendLine("                default,");
                source.AppendLine($"                {cancellation}).ConfigureAwait(false))");
                source.AppendLine($"                _{relation.PropertyName}_list.Add(_child);");
                source.AppendLine($"            _entity.{relation.PropertyName} = _{relation.PropertyName}_list;");
            }
            else
            {
                var parentKeyPropertyName = childEntity.Keys[0].PropertyName;
                var parentFkColumn = FindColumn(entity, relation.ForeignKeyProperty)!;
                source.AppendLine($"            _entity.{relation.PropertyName} = await Inquiry.QueryGeneratedSingleOrDefaultAsync<{childType}, {parentFkColumn.Type.DisplayName}, {childStructMat}>(");
                AppendSingleParameterGeneratedCommand(source, sqlBuilder, fieldName, parentFkColumn.Type.DisplayName, "_entity." + relation.ForeignKeyProperty, parentKeyPropertyName, childEntity.Keys[0], "                ");
                source.AppendLine("                default,");
                source.AppendLine($"                {cancellation}).ConfigureAwait(false);");
            }
        }
        source.AppendLine("        }");
        source.AppendLine("        return _entity;");
        source.AppendLine("    }");
    }

    private static void EmitSelectAllEager(StringBuilder source, SqlBuilder sqlBuilder, StoreMethodData method, string entityType, string cancellation, EntityData entity, Dictionary<string, EntityData> relationChildEntities, Dictionary<string, EntityData> relationJunctionEntities, string parentSelectField)
    {
        var parametersWithAttr = GetParameterDeclaration(method.Parameters, enumeratorCancellation: true);
        var parentStructMat = entity.StructMaterializerFullName;
        AppendHeader(source, method, parametersWithAttr, isAsync: true);

        // Relations that actually emit a child fetch (a relation with no resolved child entity is skipped).
        var emittedRelations = new List<RelationData>();
        foreach (var relation in entity.Relations)
        {
            if (relationChildEntities.ContainsKey(relation.PropertyName))
            {
                emittedRelations.Add(relation);
            }
        }

        // Single-round-trip (grid) path: one multi-result command — the parent SELECT plus each relation's
        // child (and junction) SELECT — read in order through an InquiryGridReader, instead of one round trip
        // per relation (#70). Requires a dialect that returns multiple result sets from one command: a
        // ;-separated batch on most dialects, Oracle's DBMS_SQL.RETURN_RESULT PL/SQL wrapper via the
        // MultiResultBatch* hooks.
        var useGrid = sqlBuilder.SupportsMultiResultBatch && emittedRelations.Count > 0;
        var relationSqlSuffix = method.IncludeDeleted && entity.SoftDeleteColumn is not null
            ? "_IncludeDeleted"
            : string.Empty;

        // Emits the opener for iterating one relation's rows: from the grid (the next pre-read result set) on
        // the single-round-trip path, or a per-relation query on the fallback path. The loop body is identical.
        void AppendRowLoop(string loopVar, string rowType, string rowStructMat, string sqlField)
            => source.AppendLine(useGrid
                ? $"        foreach (var {loopVar} in await _grid.ReadListAsync<{rowType}, {rowStructMat}>(default, {cancellation}).ConfigureAwait(false))"
                : $"        await foreach (var {loopVar} in Inquiry.QueryAsync<{rowType}, byte, {rowStructMat}>({EmptyGeneratedCommand(sqlField)}, default, {cancellation}).ConfigureAwait(false))");

        if (useGrid)
        {
            // The combined command is parameterless: the child _All / _Junction selects use subquery
            // filters (no @-parameters), so there is nothing to bind.
            var sqlFields = new List<string> { parentSelectField };
            foreach (var relation in emittedRelations)
            {
                sqlFields.Add($"_sql_{relation.PropertyName}_All{relationSqlSuffix}");
                if (relation.IsManyToMany)
                {
                    sqlFields.Add($"_sql_{relation.PropertyName}_Junction{relationSqlSuffix}");
                }
            }
            AppendGridCommandText(source, sqlBuilder, sqlFields);
            source.AppendLine("        await using var _grid = await Inquiry.QueryMultipleAsync(");
            source.AppendLine($"            {EmptyGeneratedCommand("_sql")},");
            source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
            source.AppendLine($"        var _entities = await _grid.ReadListAsync<{entityType}, {parentStructMat}>(default, {cancellation}).ConfigureAwait(false);");
        }
        else
        {
            source.AppendLine($"        var _entities = new global::System.Collections.Generic.List<{entityType}>();");
            source.AppendLine($"        await foreach (var _e in Inquiry.QueryAsync<{entityType}, byte, {parentStructMat}>({EmptyGeneratedCommand(parentSelectField)}, default, {cancellation}).ConfigureAwait(false))");
            source.AppendLine("            _entities.Add(_e);");
        }
        source.AppendLine("        if (_entities.Count == 0)");
        source.AppendLine("            yield break;");
        source.AppendLine();

        foreach (var relation in emittedRelations)
        {
            var childEntity = relationChildEntities[relation.PropertyName];
            var childType = childEntity.FullyQualifiedName;
            var fieldName = $"_sql_{relation.PropertyName}";
            var childStructMat = childEntity.StructMaterializerFullName;
            if (relation.IsManyToMany)
            {
                // Load participating children indexed by key, then load participating junction rows and group the children
                // under each parent key — a two-query in-memory assembly (no N+1) reusing both materializers.
                var junctionEntity = relationJunctionEntities[relation.PropertyName];
                var junctionType = junctionEntity.FullyQualifiedName;
                var junctionStructMat = junctionEntity.StructMaterializerFullName;
                var childKey = childEntity.Keys[0];
                var childKeyType = childKey.Type.NonNullableDisplayName;
                var jParentFk = FindColumn(junctionEntity, relation.JunctionParentForeignKeyProperty!)!;
                var jChildFk = FindColumn(junctionEntity, relation.JunctionChildForeignKeyProperty!)!;
                var parentKeyType = entity.Keys[0].Type.NonNullableDisplayName;

                source.AppendLine($"        var _childByKey_{relation.PropertyName} = new global::System.Collections.Generic.Dictionary<{childKeyType}, {childType}>();");
                AppendRowLoop("_c", childType, childStructMat, $"{fieldName}_All{relationSqlSuffix}");
                source.AppendLine("        {");
                if (childKey.Type.IsNullable)
                {
                    source.AppendLine($"            if (_c.{childKey.PropertyName} is null) continue;");
                }
                source.AppendLine($"            _childByKey_{relation.PropertyName}[{NonNullableValueExpression(childKey.Type, $"_c.{childKey.PropertyName}")}] = _c;");
                source.AppendLine("        }");

                source.AppendLine($"        var _grouped_{relation.PropertyName} = new global::System.Collections.Generic.Dictionary<{parentKeyType}, global::System.Collections.Generic.List<{childType}>>(_entities.Count);");
                AppendRowLoop("_j", junctionType, junctionStructMat, $"{fieldName}_Junction{relationSqlSuffix}");
                source.AppendLine("        {");
                if (jChildFk.Type.IsNullable)
                {
                    source.AppendLine($"            if (_j.{jChildFk.PropertyName} is null) continue;");
                }
                if (jParentFk.Type.IsNullable)
                {
                    source.AppendLine($"            if (_j.{jParentFk.PropertyName} is null) continue;");
                }
                source.AppendLine($"            if (!_childByKey_{relation.PropertyName}.TryGetValue({NonNullableValueExpression(jChildFk.Type, $"_j.{jChildFk.PropertyName}")}, out var _child)) continue;");
                source.AppendLine($"            if (!_grouped_{relation.PropertyName}.TryGetValue({NonNullableValueExpression(jParentFk.Type, $"_j.{jParentFk.PropertyName}")}, out var _grp))");
                source.AppendLine("            {");
                source.AppendLine($"                _grp = new global::System.Collections.Generic.List<{childType}>();");
                source.AppendLine($"                _grouped_{relation.PropertyName}[{NonNullableValueExpression(jParentFk.Type, $"_j.{jParentFk.PropertyName}")}] = _grp;");
                source.AppendLine("            }");
                source.AppendLine("            _grp.Add(_child);");
                source.AppendLine("        }");
                continue;
            }

            if (relation.IsCollection)
            {
                var childFkColumn = FindColumn(childEntity, relation.ForeignKeyProperty);
                var childFkNullable = childFkColumn?.Type.IsNullable ?? false;
                var fkKeyType = childFkColumn?.Type.NonNullableDisplayName ?? "object";

                source.AppendLine($"        var _grouped_{relation.PropertyName} = new global::System.Collections.Generic.Dictionary<{fkKeyType}, global::System.Collections.Generic.List<{childType}>>(_entities.Count);");
                AppendRowLoop("_c", childType, childStructMat, $"{fieldName}_All{relationSqlSuffix}");
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

                source.AppendLine($"        var _parents_{relation.PropertyName} = new global::System.Collections.Generic.Dictionary<{parentKeyType}, {childType}>(_entities.Count);");
                if (childKeyNullable)
                {
                    AppendRowLoop("_p", childType, childStructMat, $"{fieldName}_All{relationSqlSuffix}");
                    source.AppendLine("        {");
                    source.AppendLine($"            if (_p.{relatedKeyProperty} is null) continue;");
                    source.AppendLine($"            _parents_{relation.PropertyName}[{NonNullableValueExpression(childEntity.Keys[0].Type, $"_p.{relatedKeyProperty}")}] = _p;");
                    source.AppendLine("        }");
                }
                else
                {
                    AppendRowLoop("_p", childType, childStructMat, $"{fieldName}_All{relationSqlSuffix}");
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

    /// <summary>
    /// Emits a throwing-stub body for a method whose operation the active dialect cannot emit (e.g.
    /// Oracle INSERT…RETURNING). The partial declaration is still satisfied so the rest of the store
    /// compiles; calling the method throws <see cref="System.NotSupportedException"/>. Paired with the
    /// INQ039 diagnostic reported by <c>StoreProcessor</c>.
    /// </summary>
    public static void EmitUnsupportedStub(StringBuilder source, StoreMethodData method, string reason)
    {
        var parameters = GetParameterDeclaration(method.Parameters);
        AppendHeader(source, method, parameters, isAsync: false);
        var escaped = reason.Replace("\\", "\\\\").Replace("\"", "\\\"");
        source.AppendLine($"        throw new global::System.NotSupportedException(\"{escaped}\");");
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

    private static bool HasSequentialGuidKey(EntityData entity)
    {
        foreach (var key in entity.Keys.AsImmutableArray())
        {
            if (key.IsSequentialGuid)
            {
                return true;
            }
        }

        return false;
    }

    private static string SequentialGuidUnsetCheck(ColumnData key, string access)
        => key.Type.IsNullable
            ? $"{access} is null || {access} == global::System.Guid.Empty"
            : $"{access} == global::System.Guid.Empty";

    /// <summary>
    /// Emits the unset-key check + dialect-aware sequential GUID assignment for a single entity
    /// parameter (null/empty for Guid?). The factory call is dialect-specific via
    /// <see cref="SqlBuilder.SequentialGuidFactoryExpression"/>.
    /// Every [InquiryKey(SequentialGuid = true)] key is assigned (a composite key may flag more
    /// than one part). The assignment mutates the caller's entity so the generated key is
    /// observable after the call. INQ047 validation already cleared the flag for anything that
    /// is not a plain client-supplied Guid key.
    /// </summary>
    private static void EmitSequentialGuidAssignment(StringBuilder source, EntityData entity, string parameter, string indent, SqlBuilder sqlBuilder)
    {
        foreach (var key in entity.Keys.AsImmutableArray())
        {
            if (!key.IsSequentialGuid)
            {
                continue;
            }

            var access = $"{parameter}.{key.PropertyName}";
            source.AppendLine($"{indent}if ({SequentialGuidUnsetCheck(key, access)})");
            source.AppendLine($"{indent}{{");
            source.AppendLine($"{indent}    {access} = {sqlBuilder.SequentialGuidFactoryExpression};");
            source.AppendLine($"{indent}}}");
            source.AppendLine();
        }
    }

    /// <summary>
    /// Emits a bulk-copy-dialect <c>[InquiryBulkInsert]</c> method: a static
    /// <c>InquiryBulkInsertDefinition&lt;T&gt;</c> field (raw table/columns + an ordinal accessor
    /// reusing the binder's converter/enum-aware value expressions) and a body that streams the
    /// rows through <c>IInquiry.BulkInsertAsync</c>. Sequential-GUID keys and auditing timestamps
    /// are stamped per row by a local iterator as the stream is enumerated, so the semantics match
    /// the batch-insert path without buffering the collection.
    /// </summary>
    private static void EmitBulkInsert(
        StringBuilder source,
        StoreMethodData method,
        string parameters,
        EntityData entity,
        string entityType,
        string cancellation,
        SqlBuilder sqlBuilder)
    {
        var insertable = SelectMutationColumns(entity, includeKey: false);
        var itemsExpression = NonNullBatchItemsExpression(method.Parameters[0]);
        var definitionField = $"_bulkDef_{method.Name}";

        var dbTypeExprs = insertable.Select(c => ResolveDbType(c, sqlBuilder)).ToArray();
        var hasColumnTypes = dbTypeExprs.All(e => e is not null);
        var fieldTypeExprs = insertable.Select(c => BulkFieldTypeExpression(c, sqlBuilder)).ToArray();

        source.AppendLine($"    private static readonly global::Inquiry.BulkCopy.InquiryBulkInsertDefinition<{entityType}> {definitionField} = new(");
        source.AppendLine($"        {GeneratorHelpers.Literal(entity.Schema)},");
        source.AppendLine($"        {GeneratorHelpers.Literal(entity.TableName)},");
        source.AppendLine($"        new[] {{ {string.Join(", ", insertable.Select(c => GeneratorHelpers.Literal(c.ColumnName)))} }},");
        source.AppendLine("        static (_e, _i) => _i switch");
        source.AppendLine("        {");
        for (var i = 0; i < insertable.Length; i++)
        {
            source.AppendLine($"            {i} => {BuildParameterValueExpression(insertable[i], "_e." + insertable[i].PropertyName, sqlBuilder)},");
        }

        source.AppendLine("            _ => throw new global::System.ArgumentOutOfRangeException(nameof(_i)),");
        if (hasColumnTypes)
        {
            source.AppendLine($"        }},");
            source.AppendLine($"        new global::System.Data.DbType[] {{ {string.Join(", ", dbTypeExprs)} }},");
        }
        else
        {
            source.AppendLine("        null,");
        }
        source.AppendLine($"        new global::System.Type[] {{ {string.Join(", ", fieldTypeExprs)} }});");
        source.AppendLine();

        var hasStamps = HasSequentialGuidKey(entity);
        if (!hasStamps)
        {
            foreach (var column in entity.Columns.AsImmutableArray())
            {
                if (IsCreatedAudit(column) || IsModifiedAudit(column))
                {
                    hasStamps = true;
                    break;
                }
            }
        }

        AppendHeader(source, method, parameters, isAsync: false);
        if (hasStamps)
        {
            source.AppendLine($"        return Inquiry.BulkInsertAsync({definitionField}, _Stamped({itemsExpression}), {cancellation});");
            source.AppendLine();
            source.AppendLine($"        static global::System.Collections.Generic.IEnumerable<{entityType}> _Stamped(global::System.Collections.Generic.IEnumerable<{entityType}> _source)");
            source.AppendLine("        {");
            source.AppendLine("            foreach (var _e in _source)");
            source.AppendLine("            {");
            EmitSequentialGuidAssignment(source, entity, "_e", indent: "                ", sqlBuilder);
            EmitAuditAssignments(source, entity, "_e", isInsert: true, indent: "                ");
            source.AppendLine("                yield return _e;");
            source.AppendLine("            }");
            source.AppendLine("        }");
        }
        else
        {
            source.AppendLine($"        return Inquiry.BulkInsertAsync({definitionField}, {itemsExpression}, {cancellation});");
        }

        source.AppendLine("    }");
    }

    /// <summary>The stamp value for an auditing column: <c>UtcNow</c> for a timestamp, the ambient user otherwise.</summary>
    private static string AuditStampValue(ColumnData column)
    {
        if (column.IsCreatedBy || column.IsModifiedBy)
        {
            // CurrentUser is string?; null-forgive when assigning into a non-nullable string column
            // so generated code stays warning-clean for warnings-as-errors consumers.
            return column.Type.IsNullable
                ? "global::Inquiry.InquiryAuditContext.CurrentUser"
                : "global::Inquiry.InquiryAuditContext.CurrentUser!";
        }

        return column.Type.NonNullableDisplayName == "global::System.DateTimeOffset"
            ? "global::System.DateTimeOffset.UtcNow"
            : "global::System.DateTime.UtcNow";
    }

    /// <summary>The "unset" check for a created-* auditing column: null/empty for a string, default/null for a timestamp.</summary>
    private static string AuditUnsetCheck(ColumnData column, string access)
    {
        if (column.IsCreatedBy)
        {
            return $"global::System.String.IsNullOrEmpty({access})";
        }

        return column.Type.IsNullable ? $"{access} is null" : $"{access} == default";
    }

    private static bool IsCreatedAudit(ColumnData column) => column.IsCreatedAt || column.IsCreatedBy;
    private static bool IsModifiedAudit(ColumnData column) => column.IsModifiedAt || column.IsModifiedBy;

    /// <summary>
    /// Emits the auditing stamps for a single entity parameter, before binding. Insert/upsert: a
    /// created-* column (<c>[InquiryCreatedAt]</c>/<c>[InquiryCreatedBy]</c>) is stamped only when
    /// unset (an existing row keeps its stored value because the update SET excludes it), and a
    /// modified-* column is stamped unconditionally. Update: modified-* only (see
    /// <see cref="EmitModifiedAuditAssignment"/>). The stamps mutate the caller's entity, so the
    /// written values are observable after the call.
    /// </summary>
    private static void EmitAuditAssignments(StringBuilder source, EntityData entity, string parameter, bool isInsert, string indent)
    {
        foreach (var column in entity.Columns.AsImmutableArray())
        {
            var access = $"{parameter}.{column.PropertyName}";
            if (IsCreatedAudit(column) && isInsert)
            {
                source.AppendLine($"{indent}if ({AuditUnsetCheck(column, access)})");
                source.AppendLine($"{indent}{{");
                source.AppendLine($"{indent}    {access} = {AuditStampValue(column)};");
                source.AppendLine($"{indent}}}");
                source.AppendLine();
            }
            else if (IsModifiedAudit(column))
            {
                source.AppendLine($"{indent}{access} = {AuditStampValue(column)};");
                source.AppendLine();
            }
        }
    }

    /// <summary>Update-path stamp: modified-* auditing columns only (created-* are immutable).</summary>
    private static void EmitModifiedAuditAssignment(StringBuilder source, EntityData entity, string parameter, string indent)
        => EmitAuditAssignments(source, entity, parameter, isInsert: false, indent);

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

    /// <summary>
    /// Parameter declaration for a generated <c>I{StoreName}</c> interface signature. Unlike
    /// <see cref="GetParameterDeclaration"/> (the implementation half, where repeating a default fires
    /// CS1066), the interface carries each parameter's rendered default value so optional arguments
    /// survive calls through the interface.
    /// </summary>
    public static string GetInterfaceParameterDeclaration(EquatableArray<ParameterData> parameters)
    {
        var parts = new List<string>(parameters.Count);
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var suffix = parameter.DefaultValueLiteral is null ? string.Empty : " = " + parameter.DefaultValueLiteral;
            parts.Add($"{parameter.TypeDisplay} {parameter.Name}{suffix}");
        }

        return string.Join(", ", parts);
    }

    private static ColumnData[] SelectMutationColumns(EntityData entity, bool includeKey, bool forUpdate = false)
        => entity.Columns.AsImmutableArray()
            // a database-managed token (rowversion) is supplied by the database, so it is never bound
            // for INSERT (includeKey == false). For UPDATE (includeKey == true) it stays bound — the
            // WHERE composes @token from its original value, the SET never touches it.
            // forUpdate additionally drops created-* auditing columns ([InquiryCreatedAt]/
            // [InquiryCreatedBy]): the UPDATE SET excludes them (creation metadata is immutable), so
            // binding them would leave unreferenced parameters. Upserts pass forUpdate: false — their
            // insert branch references the parameter.
            // A server-computed column is never bound for INSERT or UPDATE (the database computes it).
            .Where(c => (includeKey ? c.IsKey || !c.IsGenerated : !c.IsGenerated && !c.UseDatabaseDefault && !c.IsDatabaseGeneratedToken)
                && !(forUpdate && c.IsCreatedAudit)
                && string.IsNullOrEmpty(c.ComputedExpression))
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

    private sealed class GeneratedCommandState
    {
        private readonly int _count;
        private readonly bool _includeMaxParameters;

        public GeneratedCommandState(EquatableArray<ParameterData> parameters, bool includeMaxParameters = false)
        {
            _includeMaxParameters = includeMaxParameters;
            _count = parameters.Count > 0 && parameters[parameters.Count - 1].IsCancellationToken
                ? parameters.Count - 1
                : parameters.Count;

            if (_count == 0 && !includeMaxParameters)
            {
                Type = "byte";
                Value = "default";
            }
            else if (_count == 1 && !includeMaxParameters)
            {
                Type = parameters[0].TypeDisplay;
                Value = parameters[0].Name;
            }
            else
            {
                var types = Take(parameters, _count).Select((p, i) => p.TypeDisplay + " Arg" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
                var values = Take(parameters, _count).Select((p, i) => "Arg" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + p.Name).ToList();
                if (includeMaxParameters)
                {
                    types.Add("int MaxParameters");
                    values.Add("MaxParameters: Inquiry.MaxParametersPerCommand");
                }
                Type = "(" + string.Join(", ", types) + ")";
                Value = "(" + string.Join(", ", values) + ")";
            }
        }

        public string Type { get; }

        public string Value { get; }

        public string Reference(int parameterIndex)
            => _count == 1 && !_includeMaxParameters
                ? "_args"
                : "_args.Arg" + parameterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public string MaxParametersReference => "_args.MaxParameters";
    }

    private static string EmptyGeneratedCommand(string sqlField, bool storedProcedure = false)
        => "new global::Inquiry.Commands.InquiryGeneratedCommand<byte>(" + sqlField
            + ", default, static (_, _) => { }"
            + (storedProcedure ? ", global::System.Data.CommandType.StoredProcedure" : string.Empty)
            + ")";

    private static void AppendGeneratedStateAliases(
        StringBuilder source,
        EquatableArray<ParameterData> parameters,
        GeneratedCommandState state,
        string indent)
    {
        var count = parameters.Count > 0 && parameters[parameters.Count - 1].IsCancellationToken
            ? parameters.Count - 1
            : parameters.Count;
        for (var i = 0; i < count; i++)
        {
            source.AppendLine($"{indent}var {parameters[i].Name} = {state.Reference(i)};");
        }
    }
}
