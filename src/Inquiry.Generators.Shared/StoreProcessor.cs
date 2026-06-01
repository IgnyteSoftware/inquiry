using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace Inquiry.Generators;

/// <summary>
/// Discovers generated stores and emits the concrete derived store class per user store.
/// Discovery (symbol-dependent: operation mapping, return-type and partial validation) runs in the
/// syntax-provider transform and produces a value-equatable <see cref="StoreData"/>. Linking to the
/// entity, parameter/field validation, and emission run in the combined output stage, which is where
/// the entity model is available — so a changed entity correctly re-emits its stores.
/// </summary>
internal static class StoreProcessor
{
    // ---- Discovery (transform) -----------------------------------------------------------

    /// <summary>Extracts the cacheable model for one candidate store symbol, or null if the class is
    /// not an <c>InquiryStore&lt;T&gt;</c>.</summary>
    public static StoreData? Extract(INamedTypeSymbol storeSymbol, CancellationToken cancellationToken)
    {
        if (!GeneratorHelpers.TryGetStoreEntityType(storeSymbol, out var entityType))
        {
            return null;
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticData>();
        var location = storeSymbol.Locations.FirstOrDefault();

        // Store-level gates, short-circuited in the same order as before so a single representative
        // diagnostic is reported per malformed store.
        var emittable = true;
        if (!IsPartial(storeSymbol))
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.StoreMustBePartial, location, storeSymbol.Name));
            emittable = false;
        }
        else if (storeSymbol.ContainingType is not null)
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.StoreCannotBeNested, location, storeSymbol.Name, storeSymbol.ContainingType.ToDisplayString()));
            emittable = false;
        }
        else if (storeSymbol.IsAbstract)
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.StoreCannotBeAbstract, location, storeSymbol.Name));
            emittable = false;
        }

        var methods = ImmutableArray.CreateBuilder<StoreMethodData>();
        if (emittable)
        {
            foreach (var method in storeSymbol.GetMembers().OfType<IMethodSymbol>().Where(static m => m.MethodKind == MethodKind.Ordinary))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = GetOperation(method, out var operationAttribute);
                if (operation == StoreOperation.None)
                {
                    continue;
                }

                var model = ExtractMethod(method, operation, operationAttribute!, entityType, diagnostics);
                if (model is not null)
                {
                    methods.Add(model);
                }
            }
        }

        return new StoreData(
            Name: storeSymbol.Name,
            Namespace: storeSymbol.ContainingNamespace.IsGlobalNamespace ? null : storeSymbol.ContainingNamespace.ToDisplayString(),
            FullyQualifiedName: storeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EntityFullyQualifiedName: entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsEmittable: emittable,
            Methods: new EquatableArray<StoreMethodData>(methods.ToImmutable()),
            Location: LocationData.From(location),
            Diagnostics: new EquatableArray<DiagnosticData>(diagnostics.ToImmutable()));
    }

    private static StoreMethodData? ExtractMethod(
        IMethodSymbol method,
        StoreOperation operation,
        AttributeData attribute,
        ITypeSymbol entityType,
        ImmutableArray<DiagnosticData>.Builder diagnostics)
    {
        var location = method.Locations.FirstOrDefault();

        if (!method.IsPartialDefinition)
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.MethodMustBePartial, location, method.Name));
            return null;
        }

        var returnsEntity = operation is StoreOperation.Insert or StoreOperation.Update or StoreOperation.Upsert &&
            GeneratorHelpers.GetNamedBool(attribute, "ReturnEntity");

        if (!HasSupportedReturnType(operation, method.ReturnType, entityType, returnsEntity))
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.UnsupportedReturnType, location, method.Name, method.ReturnType.ToDisplayString()));
            return null;
        }

        var fieldNames = ImmutableArray<string>.Empty;
        if (operation == StoreOperation.SelectAllByField)
        {
            var names = GeneratorHelpers.GetConstructorStringArray(attribute);
            if (names is null || names.Length == 0)
            {
                diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.UnknownField, location, method.Name, "<none>"));
                return null;
            }
            fieldNames = names.ToImmutableArray();
        }

        var predicates = ImmutableArray<PredicateData>.Empty;
        if (operation == StoreOperation.SelectAllByPredicate)
        {
            predicates = ReadWherePredicates(method);
            if (predicates.Length == 0)
            {
                diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.PredicateParameterMismatch, location, method.Name));
                return null;
            }
        }

        string? procedureName = null;
        if (operation == StoreOperation.StoredProcedure)
        {
            procedureName = GeneratorHelpers.GetConstructorString(attribute);
            if (string.IsNullOrEmpty(procedureName))
            {
                diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.UnsupportedReturnType, location, method.Name, "StoredProcedure requires a non-empty procedure name"));
                return null;
            }
        }

        var returnsList = operation is StoreOperation.SelectAll or StoreOperation.SelectAllByField or StoreOperation.SelectAllByPredicate &&
            IsTaskOfReadOnlyList(method.ReturnType, entityType);
        var procedureReturn = operation == StoreOperation.StoredProcedure
            ? ClassifyProcedureReturn(method.ReturnType, entityType)
            : ProcedureReturnKind.None;

        // ORDER BY / pagination (W2). Parsed here; order fields are resolved against the entity columns
        // (and validated) in the combined emit stage, mirroring SelectAllByField field resolution.
        var orderBy = ImmutableArray<OrderItem>.Empty;
        var pagination = Pagination.None;
        var keysetFields = ImmutableArray<string>.Empty;
        var keysetDescending = false;

        if (operation is StoreOperation.SelectAll or StoreOperation.SelectAllByField)
        {
            orderBy = ParseOrderBy(GeneratorHelpers.GetNamedString(attribute, "OrderBy"));
            if (GeneratorHelpers.GetNamedBool(attribute, "Paged"))
            {
                pagination = Pagination.Offset;
            }
        }
        else if (operation == StoreOperation.KeysetPage)
        {
            var names = GeneratorHelpers.GetConstructorStringArray(attribute);
            if (names is null || names.Length == 0)
            {
                diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.UnknownOrderField, location, method.Name, "<none>"));
                return null;
            }

            keysetFields = names.ToImmutableArray();
            // Direction is an enum named arg; its underlying int is 0 = Forward, 1 = Backward.
            keysetDescending = GeneratorHelpers.GetNamedInt(attribute, "Direction") == 1;
            pagination = Pagination.Keyset;
            orderBy = keysetFields.Select(f => new OrderItem(f, keysetDescending)).ToImmutableArray();
        }

        var parameters = method.Parameters.Select(ToParameterData).ToImmutableArray();

        // W8: IncludeDeleted opts a SELECT out of the soft-delete filter; HardDelete keeps a literal
        // DELETE on a soft-delete entity. Both are read regardless of operation (no-ops where the named
        // argument is absent) — routing decides where they matter.
        var includeDeleted = operation is StoreOperation.SelectAll or StoreOperation.SelectAllEager
            or StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager
            or StoreOperation.SelectAllByField or StoreOperation.SelectAllByPredicate &&
            GeneratorHelpers.GetNamedBool(attribute, "IncludeDeleted");
        var hardDelete = operation == StoreOperation.DeleteOneByKey && GeneratorHelpers.GetNamedBool(attribute, "HardDelete");

        return new StoreMethodData(
            Name: method.Name,
            Operation: operation,
            ReturnTypeDisplay: method.ReturnType.ToDisplayString(KnownSymbols.FullyQualifiedNullableFormat),
            Parameters: new EquatableArray<ParameterData>(parameters),
            FieldNames: new EquatableArray<string>(fieldNames),
            Predicates: new EquatableArray<PredicateData>(predicates),
            ProcedureName: procedureName,
            ReturnsEntity: returnsEntity,
            ReturnsList: returnsList,
            ProcedureReturn: procedureReturn,
            Location: LocationData.From(location))
        {
            OrderBy = new EquatableArray<OrderItem>(orderBy),
            Pagination = pagination,
            KeysetFields = new EquatableArray<string>(keysetFields),
            KeysetDescending = keysetDescending,
            IncludeDeleted = includeDeleted,
            HardDelete = hardDelete,
        };
    }

    private static ParameterData ToParameterData(IParameterSymbol parameter) => new(
        parameter.Name,
        parameter.Type.ToDisplayString(KnownSymbols.FullyQualifiedNullableFormat),
        parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        GeneratorHelpers.IsCancellationToken(parameter.Type))
    {
        ElementComparisonDisplay = GetEnumerableElementComparisonDisplay(parameter.Type),
    };

    /// <summary>
    /// Returns the <c>FullyQualifiedFormat</c> of the element type when <paramref name="type"/> is (or
    /// implements) <c>IEnumerable&lt;T&gt;</c> but is not a string; otherwise null. Used for IN-collection
    /// element-type validation.
    /// </summary>
    private static string? GetEnumerableElementComparisonDisplay(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            return null;
        }

        var enumerable = type.AllInterfaces.FirstOrDefault(static i =>
            i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);

        if (enumerable is null &&
            type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            enumerable = named;
        }

        return enumerable is { TypeArguments.Length: 1 }
            ? enumerable.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : null;
    }

    /// <summary>
    /// Reads every <c>[InquiryWhere]</c> on the method in declaration order. <c>GetAttributes()</c>
    /// preserves source order for a single symbol, so the criteria bind positionally to the method's
    /// parameters exactly as written. The <c>Compare</c> enum value arrives as its underlying int and
    /// maps one-to-one onto <see cref="SqlCompareOp"/> (same declaration order).
    /// </summary>
    private static ImmutableArray<PredicateData> ReadWherePredicates(IMethodSymbol method)
    {
        var builder = ImmutableArray.CreateBuilder<PredicateData>();
        foreach (var candidate in method.GetAttributes())
        {
            if (candidate.AttributeClass?.Name != "InquiryWhereAttribute" || !GeneratorHelpers.IsStoreAttribute(candidate))
            {
                continue;
            }

            if (candidate.ConstructorArguments.Length == 0 || candidate.ConstructorArguments[0].Value is not string field)
            {
                continue;
            }

            var op = SqlCompareOp.Equal;
            if (candidate.ConstructorArguments.Length > 1 && candidate.ConstructorArguments[1].Value is int opValue)
            {
                op = (SqlCompareOp)opValue;
            }

            builder.Add(new PredicateData(field, op, GeneratorHelpers.GetNamedBool(candidate, "Or")));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Parses an <c>OrderBy</c> attribute string (<c>"field [ASC|DESC], field2 [ASC|DESC]"</c>) into
    /// ordered terms. Direction defaults to ASC. Fields are kept raw (resolved + quoted at emit). v1
    /// supports only <c>field [ASC|DESC]</c> — collation/NULLS ordering is out of scope.
    /// </summary>
    private static ImmutableArray<OrderItem> ParseOrderBy(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return ImmutableArray<OrderItem>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<OrderItem>();
        foreach (var rawTerm in spec!.Split(','))
        {
            var term = rawTerm.Trim();
            if (term.Length == 0)
            {
                continue;
            }

            var parts = term.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var field = parts[0];
            var descending = parts.Length > 1 && string.Equals(parts[1], "DESC", StringComparison.OrdinalIgnoreCase);
            builder.Add(new OrderItem(field, descending));
        }

        return builder.ToImmutable();
    }

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
                case "InquirySelectAllAttribute": attribute = candidate; return StoreOperation.SelectAll;
                case "InquirySelectAllEagerAttribute": attribute = candidate; return StoreOperation.SelectAllEager;
                case "InquirySelectOneByKeyAttribute": attribute = candidate; return StoreOperation.SelectOneByKey;
                case "InquirySelectOneByKeyEagerAttribute": attribute = candidate; return StoreOperation.SelectOneByKeyEager;
                case "InquirySelectAllByFieldAttribute": attribute = candidate; return StoreOperation.SelectAllByField;
                case "InquirySelectAllByPredicateAttribute": attribute = candidate; return StoreOperation.SelectAllByPredicate;
                case "InquiryKeysetPageAttribute": attribute = candidate; return StoreOperation.KeysetPage;
                case "InquiryInsertAttribute": attribute = candidate; return StoreOperation.Insert;
                case "InquiryUpdateAttribute": attribute = candidate; return StoreOperation.Update;
                case "InquiryUpsertAttribute": attribute = candidate; return StoreOperation.Upsert;
                case "InquiryDeleteOneByKeyAttribute": attribute = candidate; return StoreOperation.DeleteOneByKey;
                case "InquiryRestoreOneByKeyAttribute": attribute = candidate; return StoreOperation.RestoreOneByKey;
                case "InquiryStoredProcedureAttribute": attribute = candidate; return StoreOperation.StoredProcedure;
            }
        }

        attribute = null;
        return StoreOperation.None;
    }

    private static bool IsPartial(INamedTypeSymbol storeSymbol)
        => storeSymbol.DeclaringSyntaxReferences.Any(static r =>
            r.GetSyntax() is ClassDeclarationSyntax cls && cls.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static bool HasSupportedReturnType(StoreOperation operation, ITypeSymbol returnType, ITypeSymbol entityType, bool returnsEntity)
    {
        return operation switch
        {
            StoreOperation.SelectAll or StoreOperation.SelectAllByField or StoreOperation.SelectAllByPredicate =>
                GeneratorHelpers.IsGenericType(returnType, "System.Collections.Generic.IAsyncEnumerable<T>", entityType) ||
                IsTaskOfReadOnlyList(returnType, entityType),
            StoreOperation.KeysetPage =>
                IsTaskOfInquiryPage(returnType, entityType),
            StoreOperation.SelectAllEager =>
                GeneratorHelpers.IsGenericType(returnType, "System.Collections.Generic.IAsyncEnumerable<T>", entityType),
            StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", entityType),
            StoreOperation.Insert or StoreOperation.Upsert when returnsEntity =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", entityType),
            StoreOperation.Insert or StoreOperation.Upsert =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Int32),
            StoreOperation.Update when returnsEntity =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", entityType),
            StoreOperation.Update or StoreOperation.DeleteOneByKey or StoreOperation.RestoreOneByKey =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Boolean),
            StoreOperation.StoredProcedure =>
                ClassifyProcedureReturn(returnType, entityType) != ProcedureReturnKind.None,
            _ => false,
        };
    }

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

    /// <summary>
    /// True when <paramref name="returnType"/> is <c>Task&lt;InquiryPage&lt;TEntity, TCursor&gt;&gt;</c>
    /// for the store's entity. The cursor type argument is unconstrained here (validated against the
    /// keyset key parameter at emit).
    /// </summary>
    private static bool IsTaskOfInquiryPage(ITypeSymbol returnType, ITypeSymbol entitySymbol)
    {
        if (returnType is not INamedTypeSymbol task ||
            !task.IsGenericType ||
            task.TypeArguments.Length != 1 ||
            task.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::System.Threading.Tasks.Task<TResult>")
        {
            return false;
        }

        if (task.TypeArguments[0] is not INamedTypeSymbol page ||
            !page.IsGenericType ||
            page.TypeArguments.Length != 2 ||
            page.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::Inquiry.Paging.InquiryPage<TEntity, TCursor>")
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(page.TypeArguments[0], entitySymbol);
    }

    private static ProcedureReturnKind ClassifyProcedureReturn(ITypeSymbol returnType, ITypeSymbol entityType)
    {
        if (GeneratorHelpers.IsGenericType(returnType, "System.Collections.Generic.IAsyncEnumerable<T>", entityType))
        {
            return ProcedureReturnKind.AsyncEnumerableOfEntity;
        }

        if (GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", entityType))
        {
            return ProcedureReturnKind.TaskOfEntity;
        }

        if (GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Int32))
        {
            return ProcedureReturnKind.TaskOfInt;
        }

        return ProcedureReturnKind.None;
    }

    // ---- Emit (combined output stage) ----------------------------------------------------

    public static StoreRegistration? Emit(
        SourceProductionContext context,
        StoreData store,
        IReadOnlyDictionary<string, EntityData> entities,
        SqlBuilder sqlBuilder)
    {
        if (!store.IsEmittable)
        {
            return null;
        }

        if (!entities.TryGetValue(store.EntityFullyQualifiedName, out var entity) || !entity.IsMapped)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.StoreEntityNotMapped,
                store.Location?.ToLocation(),
                store.Name,
                StripGlobalPrefix(store.EntityFullyQualifiedName)));
            return null;
        }

        // Per-method combined validation. Successful methods carry their resolved field columns and,
        // for SelectAllByPredicate, the resolved predicate plan; ordered/paged/keyset selects also carry
        // a resolved select plan (ORDER BY columns + pagination).
        var valid = new List<(StoreMethodData Method, IReadOnlyList<ColumnData> FieldColumns, ResolvedPredicatePlan? PredicatePlan, ResolvedSelectPlan? SelectPlan)>();
        foreach (var method in store.Methods)
        {
            if (TryValidateForEmit(context, method, entity, out var fieldColumns, out var predicatePlan, out var selectPlan))
            {
                valid.Add((method, fieldColumns, predicatePlan, selectPlan));
            }
        }

        if (valid.Count == 0)
        {
            return null;
        }

        // W6: a database-managed concurrency token (e.g. rowversion) is only supported on dialects with a
        // native row-version type — currently SQL Server. On any other dialect it has no portable
        // semantics, so reject it at emit (reusing INQ006; the W6 reserved block is fully claimed by the
        // entity-level INQ028/INQ029). Upsert on a token entity has unclear conflict semantics in v1, so
        // it is likewise rejected.
        if (entity.ConcurrencyToken is { IsDatabaseGeneratedToken: true } && sqlBuilder.DialectName != "SqlServer")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.InvalidParameters, store.Location?.ToLocation(), store.Name));
            return null;
        }

        if (entity.ConcurrencyToken is not null && store.Methods.AsImmutableArray().Any(static m => m.Operation == StoreOperation.Upsert))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.InvalidParameters, store.Location?.ToLocation(), store.Name));
            return null;
        }

        var relationChildEntities = BuildRelationChildEntities(entity, entities);
        var entityColumns = ToColumnList(entity.Columns);
        var ctx = new SqlBuildContext(sqlBuilder, entity.Schema, entity.TableName, entityColumns);

        // W8: when the entity has a soft-delete column, an IncludeDeleted select is built from a context
        // with the soft-delete filter suppressed (keeps the SqlBuilder select signatures stable). When
        // there is no soft-delete column this is identical to ctx and is never used.
        var hasSoftDelete = entity.SoftDeleteColumn is not null;
        var ctxIncludeDeleted = hasSoftDelete
            ? new SqlBuildContext(sqlBuilder, entity.Schema, entity.TableName, entityColumns, suppressSoftDelete: true)
            : ctx;
        SqlBuildContext CtxFor(StoreMethodData m) => hasSoftDelete && m.IncludeDeleted ? ctxIncludeDeleted : ctx;

        var key = entity.Keys[0];
        var keyMayBeDatabaseSupplied = key.IsGenerated || key.UseDatabaseDefault;
        var nullableDatabaseSuppliedKeyUpsert = keyMayBeDatabaseSupplied && key.Type.IsNullable &&
            valid.Any(static m => m.Method.Operation == StoreOperation.Upsert);

        // A SelectAll method with a resolved plan (ORDER BY / paging) emits its own per-method const, so
        // only a plain SelectAll or any SelectAllEager needs the shared _sqlSelectAll. W8: a method that
        // opts into IncludeDeleted gets its own unfiltered per-method const instead of the shared one.
        bool UsesSharedSelect((StoreMethodData Method, IReadOnlyList<ColumnData> FieldColumns, ResolvedPredicatePlan? PredicatePlan, ResolvedSelectPlan? SelectPlan) m)
            => !(hasSoftDelete && m.Method.IncludeDeleted);

        var needsSelectAll = valid.Any(m => UsesSharedSelect(m) &&
            ((m.Method.Operation == StoreOperation.SelectAll && m.SelectPlan is null) ||
             m.Method.Operation == StoreOperation.SelectAllEager));
        var needsSelectByKey = valid.Any(m => UsesSharedSelect(m) && m.Method.Operation is StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager);
        var needsInsert = valid.Any(static m => m.Method.Operation == StoreOperation.Insert && !m.Method.ReturnsEntity) ||
            nullableDatabaseSuppliedKeyUpsert && valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && !m.Method.ReturnsEntity);
        var needsUpdate = valid.Any(static m => m.Method.Operation == StoreOperation.Update && !m.Method.ReturnsEntity);
        var needsUpsert = valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && !m.Method.ReturnsEntity);
        var needsInsertReturning = valid.Any(static m => m.Method.Operation == StoreOperation.Insert && m.Method.ReturnsEntity) ||
            nullableDatabaseSuppliedKeyUpsert && valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && m.Method.ReturnsEntity);
        var needsUpdateReturning = valid.Any(static m => m.Method.Operation == StoreOperation.Update && m.Method.ReturnsEntity);
        var needsUpsertReturning = valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && m.Method.ReturnsEntity);

        // W8 delete routing. The shared _sqlDeleteByKey is the "default" delete statement: a literal
        // DELETE for an ordinary entity, or the soft UPDATE for a soft-delete entity. A HardDelete method
        // on a soft-delete entity additionally needs a separate literal-DELETE const so both can coexist.
        var needsDeleteByKey = valid.Any(m => m.Method.Operation == StoreOperation.DeleteOneByKey && !(m.Method.HardDelete && hasSoftDelete));
        var needsHardDeleteByKey = hasSoftDelete && valid.Any(static m => m.Method.Operation == StoreOperation.DeleteOneByKey && m.Method.HardDelete);
        var needsRestore = valid.Any(static m => m.Method.Operation == StoreOperation.RestoreOneByKey);

        var byFieldOps = valid
            .Where(m => UsesSharedSelect(m) && m.Method.Operation == StoreOperation.SelectAllByField && m.SelectPlan is null && m.FieldColumns.Count > 0)
            .GroupBy(static m => StoreOperationEmitter.BuildFieldSuffix(m.FieldColumns))
            .Select(static g => g.First().FieldColumns)
            .ToArray();

        // W8: per-method base SELECT const name for each non-plan select. Defaults to the shared const;
        // an IncludeDeleted select on a soft-delete entity gets its own unfiltered per-method const.
        var baseSelectFields = new Dictionary<string, string>(System.StringComparer.Ordinal);

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        GeneratorHelpers.AppendNamespaceStart(source, store.Namespace);
        source.AppendLine($"partial class {store.Name}");
        source.AppendLine("{");

        if (needsSelectAll) AppendConstSql(source, "_sqlSelectAll", sqlBuilder.BuildSelectAllSql(ctx));
        if (needsSelectByKey) AppendConstSql(source, "_sqlSelectByKey", sqlBuilder.BuildSelectByKeySql(ctx));
        if (needsInsert) AppendConstSql(source, "_sqlInsert", sqlBuilder.BuildInsertSql(ctx));
        if (needsUpdate) AppendConstSql(source, "_sqlUpdate", sqlBuilder.BuildUpdateSql(ctx));
        if (needsUpsert) AppendConstSql(source, "_sqlUpsert", sqlBuilder.BuildUpsertSql(ctx));
        if (needsInsertReturning) AppendConstSql(source, "_sqlInsertReturning", sqlBuilder.BuildInsertReturningSql(ctx));
        if (needsUpdateReturning) AppendConstSql(source, "_sqlUpdateReturning", sqlBuilder.BuildUpdateReturningSql(ctx));
        if (needsUpsertReturning) AppendConstSql(source, "_sqlUpsertReturning", sqlBuilder.BuildUpsertReturningSql(ctx));
        if (needsDeleteByKey) AppendConstSql(source, "_sqlDeleteByKey", hasSoftDelete ? sqlBuilder.BuildSoftDeleteByKeySql(ctx) : sqlBuilder.BuildDeleteByKeySql(ctx));
        if (needsHardDeleteByKey) AppendConstSql(source, "_sqlHardDeleteByKey", sqlBuilder.BuildDeleteByKeySql(ctx));
        if (needsRestore) AppendConstSql(source, "_sqlRestoreByKey", sqlBuilder.BuildRestoreByKeySql(ctx));

        foreach (var fieldColumns in byFieldOps)
        {
            AppendConstSql(source, "_sqlSelectBy_" + StoreOperationEmitter.BuildFieldSuffix(fieldColumns), sqlBuilder.BuildSelectByFieldSql(ctx, ToColumnList(fieldColumns)));
        }

        foreach (var (method, _, predicatePlan, _) in valid)
        {
            if (method.Operation == StoreOperation.SelectAllByPredicate && predicatePlan is not null)
            {
                AppendConstSql(source, "_sqlPredicate_" + method.Name, sqlBuilder.BuildSelectByPredicateSql(CtxFor(method), predicatePlan.Predicates));
            }
        }

        // Ordered / paged / keyset selects each get a self-contained per-method const built from the
        // base SELECT plus a uniform ORDER BY and a (dialect-specific) pagination or keyset tail.
        foreach (var (method, fieldColumns, _, selectPlan) in valid)
        {
            if (selectPlan is not null)
            {
                AppendConstSql(source, selectPlan.SqlFieldName, BuildSelectPlanSql(sqlBuilder, CtxFor(method), fieldColumns, selectPlan));
            }
        }

        // W8: emit an unfiltered per-method base SELECT const for each non-plan IncludeDeleted select on
        // a soft-delete entity, and record the field name the emitter should use for that method.
        foreach (var (method, fieldColumns, _, selectPlan) in valid)
        {
            if (!(hasSoftDelete && method.IncludeDeleted) || selectPlan is not null)
            {
                continue;
            }

            switch (method.Operation)
            {
                case StoreOperation.SelectAll:
                case StoreOperation.SelectAllEager:
                {
                    var field = "_sqlSelectAll_" + method.Name;
                    AppendConstSql(source, field, sqlBuilder.BuildSelectAllSql(ctxIncludeDeleted));
                    baseSelectFields[method.Name] = field;
                    break;
                }

                case StoreOperation.SelectOneByKey:
                case StoreOperation.SelectOneByKeyEager:
                {
                    var field = "_sqlSelectByKey_" + method.Name;
                    AppendConstSql(source, field, sqlBuilder.BuildSelectByKeySql(ctxIncludeDeleted));
                    baseSelectFields[method.Name] = field;
                    break;
                }

                case StoreOperation.SelectAllByField when fieldColumns.Count > 0:
                {
                    var field = "_sqlSelectBy_" + StoreOperationEmitter.BuildFieldSuffix(fieldColumns) + "_" + method.Name;
                    AppendConstSql(source, field, sqlBuilder.BuildSelectByFieldSql(ctxIncludeDeleted, ToColumnList(fieldColumns)));
                    baseSelectFields[method.Name] = field;
                    break;
                }
            }
        }

        if (relationChildEntities.Count > 0)
        {
            var emittedRelations = new HashSet<string>();
            foreach (var relation in entity.Relations)
            {
                if (!relationChildEntities.TryGetValue(relation.PropertyName, out var childEntity))
                {
                    continue;
                }

                if (!emittedRelations.Add(childEntity.FullyQualifiedName))
                {
                    continue;
                }

                var childCtx = new SqlBuildContext(sqlBuilder, childEntity.Schema, childEntity.TableName, ToColumnList(childEntity.Columns));
                var filterColumn = relation.IsCollection
                    ? FindColumn(childEntity, relation.ForeignKeyProperty)!
                    : childEntity.Keys[0];

                AppendConstSql(source, "_sql_" + relation.PropertyName, sqlBuilder.BuildSelectByFieldSql(childCtx, new List<IColumn> { filterColumn }));
                AppendConstSql(source, "_sql_" + relation.PropertyName + "_All", sqlBuilder.BuildSelectAllSql(childCtx));
            }
        }

        source.AppendLine();
        source.AppendLine($"    public {store.Name}(global::Inquiry.IInquiry inquiry)");
        source.AppendLine("        : base(inquiry)");
        source.AppendLine("    {");
        source.AppendLine("    }");

        foreach (var (method, fieldColumns, predicatePlan, selectPlan) in valid)
        {
            source.AppendLine();
            baseSelectFields.TryGetValue(method.Name, out var baseSelectField);
            StoreOperationEmitter.Emit(source, method, fieldColumns, predicatePlan, selectPlan, entity, relationChildEntities, baseSelectField);
        }

        source.AppendLine("}");
        GeneratorHelpers.AppendNamespaceEnd(source, store.Namespace);

        context.AddSource($"{store.Name}.InquiryStore.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        return new StoreRegistration(store.FullyQualifiedName);
    }

    private static bool TryValidateForEmit(SourceProductionContext context, StoreMethodData method, EntityData entity, out IReadOnlyList<ColumnData> fieldColumns, out ResolvedPredicatePlan? predicatePlan, out ResolvedSelectPlan? selectPlan)
    {
        fieldColumns = Array.Empty<ColumnData>();
        predicatePlan = null;
        selectPlan = null;

        // W8: restore only makes sense on a soft-delete entity. Without the indicator column the restore
        // UPDATE has nothing to clear, so reject the method (reusing the invalid-parameters diagnostic —
        // the W8 ID block, INQ033/INQ034, is fully claimed by the column-level diagnostics).
        if (method.Operation == StoreOperation.RestoreOneByKey && entity.SoftDeleteColumn is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.InvalidParameters, method.Location?.ToLocation(), method.Name));
            return false;
        }

        if (method.Operation == StoreOperation.KeysetPage)
        {
            return TryValidateKeysetPage(context, method, entity, out selectPlan);
        }

        if (method.Operation == StoreOperation.SelectAllByField)
        {
            var resolved = new List<ColumnData>(method.FieldNames.Count);
            foreach (var name in method.FieldNames)
            {
                var column = FindColumn(entity, name);
                if (column is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.UnknownField, method.Location?.ToLocation(), method.Name, name));
                    return false;
                }
                resolved.Add(column);
            }
            fieldColumns = resolved;
        }

        if (method.Operation == StoreOperation.SelectAllByPredicate)
        {
            if (!TryResolvePredicates(context, method, entity, out predicatePlan))
            {
                return false;
            }

            // Predicate methods validate their own parameter layout in TryResolvePredicates; the
            // only remaining gate is the trailing CancellationToken.
            if (method.Parameters.Count == 0 || !method.Parameters[method.Parameters.Count - 1].IsCancellationToken)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.InvalidParameters, method.Location?.ToLocation(), method.Name));
                return false;
            }

            return true;
        }

        if (method.Operation is StoreOperation.SelectAllEager or StoreOperation.SelectOneByKeyEager && entity.Keys.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.EagerLoadingOnCompositeKeyParent, method.Location?.ToLocation(), method.Name, entity.Name));
            return false;
        }

        // ORDER BY / offset pagination (W2) for SelectAll / SelectAllByField. Resolve order fields and,
        // when paging, validate the trailing (offset, limit) int parameters; emit a per-method const.
        if (method.Operation is StoreOperation.SelectAll or StoreOperation.SelectAllByField &&
            (method.OrderBy.Count > 0 || method.Pagination != Pagination.None))
        {
            if (!TryResolveOrderColumns(context, method, entity, out var orderColumns))
            {
                return false;
            }

            // Offset paging requires ORDER BY (deterministic order; SqlServer FETCH needs it) and must be
            // buffered (Task<IReadOnlyList<T>>) so the offset/limit ints can sit ahead of the token.
            if (method.Pagination == Pagination.Offset)
            {
                if (orderColumns.Count == 0 || !method.ReturnsList || !HasOffsetPagingParameters(method, entity, fieldColumns))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.PagingRequiresOrderBy, method.Location?.ToLocation(), method.Name));
                    return false;
                }
            }
            else if (!HasSupportedParameters(method, entity, fieldColumns))
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.InvalidParameters, method.Location?.ToLocation(), method.Name));
                return false;
            }

            selectPlan = new ResolvedSelectPlan
            {
                SqlFieldName = "_sql_" + method.Name,
                OrderColumns = orderColumns,
                Pagination = method.Pagination,
            };

            return true;
        }

        if (!HasSupportedParameters(method, entity, fieldColumns))
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.InvalidParameters, method.Location?.ToLocation(), method.Name));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves a method's parsed ORDER BY terms against the entity columns, reporting INQ021 for an
    /// unknown field. Empty input yields an empty list (caller decides whether that is allowed).
    /// </summary>
    private static bool TryResolveOrderColumns(SourceProductionContext context, StoreMethodData method, EntityData entity, out IReadOnlyList<(ColumnData Column, bool Descending)> orderColumns)
    {
        var resolved = new List<(ColumnData, bool)>(method.OrderBy.Count);
        foreach (var term in method.OrderBy.AsImmutableArray())
        {
            var column = FindColumn(entity, term.Field);
            if (column is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.UnknownOrderField, method.Location?.ToLocation(), method.Name, term.Field));
                orderColumns = Array.Empty<(ColumnData, bool)>();
                return false;
            }

            resolved.Add((column, term.Descending));
        }

        orderColumns = resolved;
        return true;
    }

    /// <summary>
    /// Validates the parameter shape of an offset-paged method: the field/filter parameters (none for
    /// SelectAll, the field columns for SelectAllByField) followed by two <c>int</c> parameters
    /// (offset, limit) and the trailing cancellation token.
    /// </summary>
    private static bool HasOffsetPagingParameters(StoreMethodData method, EntityData entity, IReadOnlyList<ColumnData> fieldColumns)
    {
        var parameters = method.Parameters;
        if (parameters.Count == 0 || !parameters[parameters.Count - 1].IsCancellationToken)
        {
            return false;
        }

        var filterCount = method.Operation == StoreOperation.SelectAllByField ? fieldColumns.Count : 0;
        var nonCancellationCount = parameters.Count - 1;
        if (nonCancellationCount != filterCount + 2)
        {
            return false;
        }

        if (method.Operation == StoreOperation.SelectAllByField &&
            !MatchesPositionalColumns(method, filterCount, fieldColumns))
        {
            return false;
        }

        // The two paging parameters must be int (offset, then limit).
        return parameters[filterCount].ComparisonDisplay == "int" &&
            parameters[filterCount + 1].ComparisonDisplay == "int";
    }

    /// <summary>
    /// Validates and resolves a keyset-page method: <c>Task&lt;InquiryPage&lt;TEntity, TCursor&gt;&gt;</c>
    /// return shape, a nullable cursor parameter, an <c>int pageSize</c>, then the cancellation token. The
    /// cursor parameter type must match the single key column's nullable type, or a nullable value tuple of
    /// the key column types for a composite keyset.
    /// </summary>
    private static bool TryValidateKeysetPage(SourceProductionContext context, StoreMethodData method, EntityData entity, out ResolvedSelectPlan? selectPlan)
    {
        selectPlan = null;

        var keysetColumns = new List<ColumnData>(method.KeysetFields.Count);
        foreach (var name in method.KeysetFields.AsImmutableArray())
        {
            var column = FindColumn(entity, name);
            if (column is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.UnknownOrderField, method.Location?.ToLocation(), method.Name, name));
                return false;
            }

            keysetColumns.Add(column);
        }

        var parameters = method.Parameters;
        // (cursor, pageSize, cancellationToken)
        if (parameters.Count != 3 ||
            !parameters[2].IsCancellationToken ||
            parameters[1].ComparisonDisplay != "int" ||
            !CursorParameterMatches(parameters[0], keysetColumns))
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.PagingRequiresOrderBy, method.Location?.ToLocation(), method.Name));
            return false;
        }

        var orderColumns = keysetColumns.Select(c => (c, method.KeysetDescending)).ToList();
        selectPlan = new ResolvedSelectPlan
        {
            SqlFieldName = "_sql_" + method.Name,
            OrderColumns = orderColumns,
            Pagination = Pagination.Keyset,
            KeysetColumns = keysetColumns,
        };

        return true;
    }

    /// <summary>
    /// True when the keyset cursor parameter is the nullable form of the single key column's type, or a
    /// nullable value tuple of the composite key columns' types.
    /// </summary>
    private static bool CursorParameterMatches(ParameterData cursor, IReadOnlyList<ColumnData> keysetColumns)
    {
        if (keysetColumns.Count == 1)
        {
            // The cursor is the nullable form of the key (null = first page). For a nullable key column
            // (e.g. int?) that is the column's own display; for a non-nullable key (e.g. long) it is the
            // key type plus "?".
            var columnType = keysetColumns[0].Type;
            var expectedNullable = columnType.IsNullable ? columnType.DisplayName : columnType.NonNullableDisplayName + "?";
            return cursor.TypeDisplay == expectedNullable;
        }

        var tupleElements = string.Join(", ", keysetColumns.Select(c => c.Type.NonNullableDisplayName));
        var expectedTuple = "(" + tupleElements + ")?";
        return cursor.TypeDisplay == expectedTuple;
    }

    /// <summary>
    /// Resolves each <c>[InquiryWhere]</c> criterion against the entity columns and binds the operators
    /// positionally to the method parameters. Reports INQ007 for an unknown field, INQ018 for a bad
    /// <c>In</c> collection, and INQ019 for any arity / parameter-order / Like-on-non-string mismatch.
    /// </summary>
    private static bool TryResolvePredicates(SourceProductionContext context, StoreMethodData method, EntityData entity, out ResolvedPredicatePlan? plan)
    {
        plan = null;
        var nonCancellationCount = method.Parameters.Count - 1;
        var predicates = new List<SqlPredicate>(method.Predicates.Count);
        var bindings = new List<PredicateBinding>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var paramIndex = 0;
        var hasIn = false;

        foreach (var predicate in method.Predicates.AsImmutableArray())
        {
            var column = FindColumn(entity, predicate.Field);
            if (column is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.UnknownField, method.Location?.ToLocation(), method.Name, predicate.Field));
                return false;
            }

            var arity = SqlPredicate.ParameterArity(predicate.Op);
            if (paramIndex + arity > nonCancellationCount)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.PredicateParameterMismatch, method.Location?.ToLocation(), method.Name));
                return false;
            }

            switch (predicate.Op)
            {
                case SqlCompareOp.IsNull:
                case SqlCompareOp.IsNotNull:
                    predicates.Add(new SqlPredicate(column, predicate.Op, null, null, predicate.IsOr));
                    break;

                case SqlCompareOp.Between:
                {
                    if (!ParameterMatchesColumn(method.Parameters[paramIndex], column) ||
                        !ParameterMatchesColumn(method.Parameters[paramIndex + 1], column))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.PredicateParameterMismatch, method.Location?.ToLocation(), method.Name));
                        return false;
                    }

                    var lo = UniqueName(usedNames, column.PropertyName + "_lo");
                    var hi = UniqueName(usedNames, column.PropertyName + "_hi");
                    predicates.Add(new SqlPredicate(column, predicate.Op, "@" + lo, "@" + hi, predicate.IsOr));
                    bindings.Add(new PredicateBinding("@" + lo, paramIndex, column, isCollection: false));
                    bindings.Add(new PredicateBinding("@" + hi, paramIndex + 1, column, isCollection: false));
                    paramIndex += 2;
                    break;
                }

                case SqlCompareOp.In:
                {
                    // IN matches the collection element against the column's non-nullable type: a set
                    // of values to test membership against is never itself nullable.
                    var element = method.Parameters[paramIndex].ElementComparisonDisplay;
                    if (element is null || element != column.Type.NonNullableDisplayName)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.PredicateInRequiresCollection, method.Location?.ToLocation(), method.Name, predicate.Field));
                        return false;
                    }

                    var name = UniqueName(usedNames, column.PropertyName);
                    predicates.Add(new SqlPredicate(column, predicate.Op, "@" + name, null, predicate.IsOr));
                    bindings.Add(new PredicateBinding("@" + name, paramIndex, column, isCollection: true));
                    paramIndex += 1;
                    hasIn = true;
                    break;
                }

                case SqlCompareOp.Like:
                {
                    if (column.Type.NonNullableDisplayName != "string" ||
                        !ParameterMatchesColumn(method.Parameters[paramIndex], column))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.PredicateParameterMismatch, method.Location?.ToLocation(), method.Name));
                        return false;
                    }

                    var name = UniqueName(usedNames, column.PropertyName);
                    predicates.Add(new SqlPredicate(column, predicate.Op, "@" + name, null, predicate.IsOr));
                    bindings.Add(new PredicateBinding("@" + name, paramIndex, column, isCollection: false));
                    paramIndex += 1;
                    break;
                }

                default:
                {
                    if (!ParameterMatchesColumn(method.Parameters[paramIndex], column))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.PredicateParameterMismatch, method.Location?.ToLocation(), method.Name));
                        return false;
                    }

                    var name = UniqueName(usedNames, column.PropertyName);
                    predicates.Add(new SqlPredicate(column, predicate.Op, "@" + name, null, predicate.IsOr));
                    bindings.Add(new PredicateBinding("@" + name, paramIndex, column, isCollection: false));
                    paramIndex += 1;
                    break;
                }
            }
        }

        if (paramIndex != nonCancellationCount)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.PredicateParameterMismatch, method.Location?.ToLocation(), method.Name));
            return false;
        }

        plan = new ResolvedPredicatePlan(predicates, bindings, hasIn);
        return true;
    }

    private static bool ParameterMatchesColumn(ParameterData parameter, ColumnData column)
        => parameter.ComparisonDisplay == column.Type.DisplayName;

    private static string UniqueName(HashSet<string> used, string candidate)
    {
        if (used.Add(candidate))
        {
            return candidate;
        }

        for (var i = 2; ; i++)
        {
            var next = candidate + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (used.Add(next))
            {
                return next;
            }
        }
    }

    private static bool HasSupportedParameters(StoreMethodData method, EntityData entity, IReadOnlyList<ColumnData> fieldColumns)
    {
        var parameters = method.Parameters;
        if (parameters.Count == 0 || !parameters[parameters.Count - 1].IsCancellationToken)
        {
            return false;
        }

        var nonCancellationCount = parameters.Count - 1;

        return method.Operation switch
        {
            StoreOperation.SelectAll or StoreOperation.SelectAllEager => parameters.Count == 1,
            StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager or StoreOperation.DeleteOneByKey or StoreOperation.RestoreOneByKey =>
                MatchesPositionalColumns(method, nonCancellationCount, entity.Keys.AsImmutableArray()),
            StoreOperation.SelectAllByField =>
                fieldColumns.Count > 0 && MatchesPositionalColumns(method, nonCancellationCount, fieldColumns),
            StoreOperation.Insert or StoreOperation.Update or StoreOperation.Upsert =>
                parameters.Count == 2 && parameters[0].ComparisonDisplay == entity.FullyQualifiedName,
            StoreOperation.StoredProcedure => true,
            _ => false,
        };
    }

    private static bool MatchesPositionalColumns(StoreMethodData method, int nonCancellationCount, IReadOnlyList<ColumnData> columns)
    {
        if (nonCancellationCount != columns.Count)
        {
            return false;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            // ColumnData.Type.DisplayName and ParameterData.ComparisonDisplay are both
            // FullyQualifiedFormat, so string equality matches SymbolEqualityComparer.Default here.
            if (method.Parameters[i].ComparisonDisplay != columns[i].Type.DisplayName)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, EntityData> BuildRelationChildEntities(EntityData entity, IReadOnlyDictionary<string, EntityData> entities)
    {
        var result = new Dictionary<string, EntityData>();
        foreach (var relation in entity.Relations)
        {
            if (entities.TryGetValue(relation.ChildEntityFullyQualifiedName, out var child))
            {
                result[relation.PropertyName] = child;
            }
        }

        return result;
    }

    private static ColumnData? FindColumn(EntityData entity, string nameOrColumn)
    {
        foreach (var column in entity.Columns.AsImmutableArray())
        {
            if (string.Equals(column.PropertyName, nameOrColumn, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column.ColumnName, nameOrColumn, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        return null;
    }

    private static List<IColumn> ToColumnList(EquatableArray<ColumnData> columns)
    {
        var list = new List<IColumn>(columns.Count);
        foreach (var column in columns.AsImmutableArray())
        {
            list.Add(column);
        }

        return list;
    }

    private static List<IColumn> ToColumnList(IReadOnlyList<ColumnData> columns)
    {
        var list = new List<IColumn>(columns.Count);
        foreach (var column in columns)
        {
            list.Add(column);
        }

        return list;
    }

    private static void AppendConstSql(StringBuilder source, string fieldName, string sql)
    {
        source.AppendLine($"    private const string {fieldName} = \"{GeneratorHelpers.Escape(sql)}\";");
    }

    // Synthetic paging parameter names (not entity columns). Bound by the generated paging binder.
    internal const string OffsetParameterName = "@__offset";
    internal const string LimitParameterName = "@__limit";
    internal const string PageSizeParameterName = "@__pageSize";

    internal static string KeysetCursorParameterName(int index) => "@__cursor" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Composes the per-method SQL for an ordered / offset-paged / keyset-paged select: the dialect's
    /// base SELECT (with WHERE for SelectAllByField or the keyset cursor predicate), a uniform ORDER BY,
    /// and the dialect-specific pagination tail.
    /// </summary>
    private static string BuildSelectPlanSql(SqlBuilder sqlBuilder, SqlBuildContext ctx, IReadOnlyList<ColumnData> fieldColumns, ResolvedSelectPlan plan)
    {
        var orderTerms = new List<OrderByTerm>(plan.OrderColumns.Count);
        foreach (var (column, descending) in plan.OrderColumns)
        {
            orderTerms.Add(new OrderByTerm(sqlBuilder.QuoteIdentifier(column.ColumnName), descending));
        }

        string baseSql;
        SqlSelectOptions options;

        if (plan.Pagination == Pagination.Keyset)
        {
            var keysetColumns = plan.KeysetColumns.Select(c => sqlBuilder.QuoteIdentifier(c.ColumnName)).ToList();
            var cursorParams = new List<string>(keysetColumns.Count);
            for (var i = 0; i < keysetColumns.Count; i++)
            {
                cursorParams.Add(KeysetCursorParameterName(i));
            }

            // Keyset requests pageSize+1 rows via FETCH/LIMIT. SqlServer FETCH needs an OFFSET, which is
            // a literal 0 here (keyset never skips), so the same BuildPaginationClause serves all dialects.
            // Keyset terms all share one direction, so the first order column's direction is the keyset's.
            options = new SqlSelectOptions(
                orderTerms,
                offsetParameter: "0",
                limitParameter: PageSizeParameterName,
                keysetColumns: keysetColumns,
                keysetCursorParameters: cursorParams,
                keysetDescending: plan.OrderColumns.Count > 0 && plan.OrderColumns[0].Descending);

            var keysetWhere = sqlBuilder.BuildKeysetPredicate(options);
            // W8: keyset selects compose the soft-delete active filter onto the cursor predicate (the
            // keyset op has no IncludeDeleted opt-out). AppendWhere is internal to SqlBuilder, so the same
            // AND-composition is applied inline here against the precomputed fragment.
            if (ctx.SoftDeleteActivePredicate.Length > 0)
            {
                keysetWhere += " AND " + ctx.SoftDeleteActivePredicate;
            }
            baseSql = "SELECT " + ctx.SelectColumns + " FROM " + ctx.Table + " WHERE " + keysetWhere;
        }
        else
        {
            options = plan.Pagination == Pagination.Offset
                ? new SqlSelectOptions(orderTerms, offsetParameter: OffsetParameterName, limitParameter: LimitParameterName)
                : new SqlSelectOptions(orderTerms);

            baseSql = fieldColumns.Count > 0
                ? sqlBuilder.BuildSelectByFieldSql(ctx, ToColumnList(fieldColumns))
                : sqlBuilder.BuildSelectAllSql(ctx);
        }

        var orderByClause = sqlBuilder.BuildOrderByClause(options);
        var sql = orderByClause.Length > 0 ? baseSql + " " + orderByClause : baseSql;

        if (plan.Pagination != Pagination.None)
        {
            sql += " " + sqlBuilder.BuildPaginationClause(options);
        }

        return sql;
    }

    private static string StripGlobalPrefix(string fullyQualifiedName)
        => fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualifiedName.Substring("global::".Length)
            : fullyQualifiedName;
}
