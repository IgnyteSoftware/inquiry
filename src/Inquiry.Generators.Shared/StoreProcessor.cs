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

        var parameters = method.Parameters.Select(ToParameterData).ToImmutableArray();

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
            Location: LocationData.From(location));
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
                case "InquiryInsertAttribute": attribute = candidate; return StoreOperation.Insert;
                case "InquiryUpdateAttribute": attribute = candidate; return StoreOperation.Update;
                case "InquiryUpsertAttribute": attribute = candidate; return StoreOperation.Upsert;
                case "InquiryDeleteOneByKeyAttribute": attribute = candidate; return StoreOperation.DeleteOneByKey;
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
            StoreOperation.Update or StoreOperation.DeleteOneByKey =>
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
        // for SelectAllByPredicate, the resolved predicate plan.
        var valid = new List<(StoreMethodData Method, IReadOnlyList<ColumnData> FieldColumns, ResolvedPredicatePlan? PredicatePlan)>();
        foreach (var method in store.Methods)
        {
            if (TryValidateForEmit(context, method, entity, out var fieldColumns, out var predicatePlan))
            {
                valid.Add((method, fieldColumns, predicatePlan));
            }
        }

        if (valid.Count == 0)
        {
            return null;
        }

        var relationChildEntities = BuildRelationChildEntities(entity, entities);
        var ctx = new SqlBuildContext(sqlBuilder, entity.Schema, entity.TableName, ToColumnList(entity.Columns));

        var key = entity.Keys[0];
        var keyMayBeDatabaseSupplied = key.IsGenerated || key.UseDatabaseDefault;
        var nullableDatabaseSuppliedKeyUpsert = keyMayBeDatabaseSupplied && key.Type.IsNullable &&
            valid.Any(static m => m.Method.Operation == StoreOperation.Upsert);

        var needsSelectAll = valid.Any(static m => m.Method.Operation is StoreOperation.SelectAll or StoreOperation.SelectAllEager);
        var needsSelectByKey = valid.Any(static m => m.Method.Operation is StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager);
        var needsInsert = valid.Any(static m => m.Method.Operation == StoreOperation.Insert && !m.Method.ReturnsEntity) ||
            nullableDatabaseSuppliedKeyUpsert && valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && !m.Method.ReturnsEntity);
        var needsUpdate = valid.Any(static m => m.Method.Operation == StoreOperation.Update && !m.Method.ReturnsEntity);
        var needsUpsert = valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && !m.Method.ReturnsEntity);
        var needsInsertReturning = valid.Any(static m => m.Method.Operation == StoreOperation.Insert && m.Method.ReturnsEntity) ||
            nullableDatabaseSuppliedKeyUpsert && valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && m.Method.ReturnsEntity);
        var needsUpdateReturning = valid.Any(static m => m.Method.Operation == StoreOperation.Update && m.Method.ReturnsEntity);
        var needsUpsertReturning = valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && m.Method.ReturnsEntity);
        var needsDelete = valid.Any(static m => m.Method.Operation == StoreOperation.DeleteOneByKey);

        var byFieldOps = valid
            .Where(static m => m.Method.Operation == StoreOperation.SelectAllByField && m.FieldColumns.Count > 0)
            .GroupBy(static m => StoreOperationEmitter.BuildFieldSuffix(m.FieldColumns))
            .Select(static g => g.First().FieldColumns)
            .ToArray();

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
        if (needsDelete) AppendConstSql(source, "_sqlDeleteByKey", sqlBuilder.BuildDeleteByKeySql(ctx));

        foreach (var fieldColumns in byFieldOps)
        {
            AppendConstSql(source, "_sqlSelectBy_" + StoreOperationEmitter.BuildFieldSuffix(fieldColumns), sqlBuilder.BuildSelectByFieldSql(ctx, ToColumnList(fieldColumns)));
        }

        foreach (var (method, _, predicatePlan) in valid)
        {
            if (method.Operation == StoreOperation.SelectAllByPredicate && predicatePlan is not null)
            {
                AppendConstSql(source, "_sqlPredicate_" + method.Name, sqlBuilder.BuildSelectByPredicateSql(ctx, predicatePlan.Predicates));
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

        foreach (var (method, fieldColumns, predicatePlan) in valid)
        {
            source.AppendLine();
            StoreOperationEmitter.Emit(source, method, fieldColumns, predicatePlan, entity, relationChildEntities);
        }

        source.AppendLine("}");
        GeneratorHelpers.AppendNamespaceEnd(source, store.Namespace);

        context.AddSource($"{store.Name}.InquiryStore.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        return new StoreRegistration(store.FullyQualifiedName);
    }

    private static bool TryValidateForEmit(SourceProductionContext context, StoreMethodData method, EntityData entity, out IReadOnlyList<ColumnData> fieldColumns, out ResolvedPredicatePlan? predicatePlan)
    {
        fieldColumns = Array.Empty<ColumnData>();
        predicatePlan = null;

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

        if (!HasSupportedParameters(method, entity, fieldColumns))
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.InvalidParameters, method.Location?.ToLocation(), method.Name));
            return false;
        }

        return true;
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
            StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager or StoreOperation.DeleteOneByKey =>
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

    private static string StripGlobalPrefix(string fullyQualifiedName)
        => fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualifiedName.Substring("global::".Length)
            : fullyQualifiedName;
}
