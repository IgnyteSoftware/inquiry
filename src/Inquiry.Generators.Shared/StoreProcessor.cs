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
            GenerateInterface: HasGenerateInterfaceAttribute(storeSymbol),
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

        if (!HasSupportedReturnType(operation, method.ReturnType, entityType, returnsEntity, attribute))
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.UnsupportedReturnType, location, method.Name, method.ReturnType.ToDisplayString()));
            return null;
        }

        // FieldNames double as the SET field list for UpdateByPredicate (the [InquiryUpdateWhere]
        // constructor arguments), resolved against the entity's mutable columns at emit.
        var fieldNames = ImmutableArray<string>.Empty;
        if (operation is StoreOperation.SelectAllByField or StoreOperation.FullTextSearch or StoreOperation.UpdateByPredicate)
        {
            var names = GeneratorHelpers.GetConstructorStringArray(attribute);
            if (names is null || names.Length == 0)
            {
                // A field-less [InquirySelectAllByField] derives its filter columns from the method
                // name (Spring Data convention); the other operations still require explicit fields.
                var derived = operation == StoreOperation.SelectAllByField ? DeriveFieldNamesFromMethodName(method.Name) : null;
                if (derived is null || derived.Length == 0)
                {
                    diagnostics.Add(DiagnosticData.Create(
                        operation == StoreOperation.SelectAllByField
                            ? InquiryDiagnosticDescriptors.DerivedQueryNameInvalid
                            : InquiryDiagnosticDescriptors.UnknownField,
                        location, method.Name, "<none>"));
                    return null;
                }

                fieldNames = derived.ToImmutableArray();
            }
            else
            {
                fieldNames = names.ToImmutableArray();
            }
        }

        var predicates = ImmutableArray<PredicateData>.Empty;
        if (operation is StoreOperation.SelectAllByPredicate or StoreOperation.UpdateByPredicate or StoreOperation.DeleteByPredicate)
        {
            predicates = ReadWherePredicates(method);
            if (predicates.Length == 0)
            {
                // A predicate select with no criteria is a parameter mismatch (INQ019); a set-based
                // mutation with no criteria would touch every row, so it gets its own diagnostic (INQ023).
                diagnostics.Add(DiagnosticData.Create(
                    operation == StoreOperation.SelectAllByPredicate
                        ? InquiryDiagnosticDescriptors.PredicateParameterMismatch
                        : InquiryDiagnosticDescriptors.PredicateMutationRequiresWhere,
                    location, method.Name));
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

        string? aggregateFunction = null;
        string? aggregateColumn = null;
        string? scalarResultType = null;
        if (operation == StoreOperation.Aggregate)
        {
            var fnValue = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value : null;
            aggregateFunction = (fnValue is int fnInt ? fnInt : 0) switch
            {
                1 => "AVG",
                2 => "MIN",
                3 => "MAX",
                _ => "SUM",
            };
            aggregateColumn = attribute.ConstructorArguments.Length > 1 ? attribute.ConstructorArguments[1].Value as string : null;
            scalarResultType = method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } aggTask
                ? aggTask.TypeArguments[0].ToDisplayString(KnownSymbols.FullyQualifiedNullableFormat)
                : null;
        }

        // SelectAll uses the shape (buffered IReadOnlyList vs streaming) and records its element
        // type for projection resolution at emit; other select ops stay entity-typed.
        string? resultElementTypeFqn = null;
        bool returnsList;
        if (operation is StoreOperation.SelectAll or StoreOperation.SelectAllByField)
        {
            returnsList = TryGetSelectElementType(method.ReturnType, out var selectElement, out var isList) && isList;
            if (selectElement is not null)
            {
                resultElementTypeFqn = selectElement.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }
        else
        {
            returnsList = operation is StoreOperation.SelectAllByPredicate or StoreOperation.FullTextSearch &&
                IsTaskOfReadOnlyList(method.ReturnType, entityType);
        }
        var procedureReturn = ProcedureReturnKind.None;
        string? procReadBackName = null;
        var procReturnsValue = false;
        string? procOutputDbType = null;
        var procOutputIsString = false;
        var procOutputIsDecimal = false;
        if (operation == StoreOperation.StoredProcedure)
        {
            if (HasScalarProcedureOutput(attribute))
            {
                // The return shape was already validated as Task<TScalar> by HasSupportedReturnType.
                var scalarSymbol = ((INamedTypeSymbol)method.ReturnType).TypeArguments[0];
                scalarResultType = scalarSymbol.ToDisplayString(KnownSymbols.FullyQualifiedNullableFormat);

                var outputParam = GeneratorHelpers.GetNamedString(attribute, "OutputParameter");
                var returnsValueArg = GeneratorHelpers.GetNamedBool(attribute, "ReturnsValue");
                if (!string.IsNullOrEmpty(outputParam) && returnsValueArg)
                {
                    diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.StoredProcedureScalarOutputInvalid, location, method.Name,
                        "OutputParameter and ReturnsValue cannot both be set."));
                    return null;
                }

                if (returnsValueArg)
                {
                    if (scalarSymbol.SpecialType != SpecialType.System_Int32)
                    {
                        diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.StoredProcedureScalarOutputInvalid, location, method.Name,
                            "a RETURN value is an integer, so the method must be declared Task<int>."));
                        return null;
                    }

                    procReturnsValue = true;
                    procReadBackName = "@__inquiry_return";
                }
                else
                {
                    procReadBackName = NormalizeParameterName(outputParam!);
                    var scalarType = TypeData.Create(scalarSymbol, scalarSymbol.NullableAnnotation);
                    procOutputDbType = DbTypeMapper.TryGetDbTypeExpression(scalarType);
                    procOutputIsString = scalarType.SpecialType == SpecialType.System_String;
                    procOutputIsDecimal = scalarType.SpecialType == SpecialType.System_Decimal;
                }

                procedureReturn = ProcedureReturnKind.TaskOfOutputScalar;
            }
            else
            {
                procedureReturn = ClassifyProcedureReturn(method.ReturnType, entityType);
            }
        }

        // ORDER BY / pagination. Parsed here; order fields are resolved against the entity columns
        // (and validated) in the combined emit stage, mirroring SelectAllByField field resolution.
        var orderBy = ImmutableArray<OrderItem>.Empty;
        var pagination = Pagination.None;
        var keysetFields = ImmutableArray<string>.Empty;
        var keysetDescending = false;

        if (operation is StoreOperation.SelectAll or StoreOperation.SelectAllByField)
        {
            orderBy = ParseOrderBy(GeneratorHelpers.GetNamedString(attribute, "OrderBy"), method.Name, location, diagnostics);
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

        // IncludeDeleted opts a SELECT out of the soft-delete filter; HardDelete keeps a literal
        // DELETE on a soft-delete entity. Both are read regardless of operation (no-ops where the named
        // argument is absent) — routing decides where they matter.
        var includeDeleted = operation is StoreOperation.SelectAll or StoreOperation.SelectAllEager
            or StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager
            or StoreOperation.SelectAllByField or StoreOperation.SelectAllByPredicate &&
            GeneratorHelpers.GetNamedBool(attribute, "IncludeDeleted");
        var hardDelete = operation is StoreOperation.DeleteOneByKey or StoreOperation.DeleteByPredicate &&
            GeneratorHelpers.GetNamedBool(attribute, "HardDelete");

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
            AggregateFunction = aggregateFunction,
            AggregateColumn = aggregateColumn,
            ScalarResultType = scalarResultType,
            ResultElementTypeFqn = resultElementTypeFqn,
            ProcedureReadBackName = procReadBackName,
            ProcedureReturnsValue = procReturnsValue,
            ProcedureOutputDbType = procOutputDbType,
            ProcedureOutputIsString = procOutputIsString,
            ProcedureOutputIsDecimal = procOutputIsDecimal,
        };
    }

    /// <summary>Prefixes a parameter name with <c>@</c> when it carries no provider sigil already.</summary>
    private static string NormalizeParameterName(string name)
        => name.Length > 0 && name[0] is '@' or ':' or '$' or '?' ? name : "@" + name;

    /// <summary>
    /// Derives the filter-field names from a method name following the <c>…By&lt;Field&gt;[And&lt;Field&gt;…]</c>
    /// convention (Spring Data style): strips a trailing <c>Async</c>, takes the segment after the
    /// first PascalCase <c>By</c>, and splits it on <c>And</c> word boundaries. Returns null when the
    /// name has no such <c>By&lt;Field&gt;</c> segment. Each segment is resolved against the entity's
    /// columns by the normal field-resolution path (an unknown one is INQ007).
    /// </summary>
    private static string[]? DeriveFieldNamesFromMethodName(string methodName)
    {
        var name = methodName;
        if (name.EndsWith("Async", System.StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - "Async".Length);
        }

        // The delimiter is the first PascalCase "By" (capital B, 'y', then an uppercase field char),
        // so a lowercase "by" inside a word (e.g. "Rugby") never matches.
        var byIndex = -1;
        for (var i = 0; i + 2 < name.Length; i++)
        {
            if (name[i] == 'B' && name[i + 1] == 'y' && char.IsUpper(name[i + 2]))
            {
                byIndex = i;
                break;
            }
        }

        if (byIndex < 0)
        {
            return null;
        }

        var fieldsPart = name.Substring(byIndex + 2);
        if (fieldsPart.Length == 0)
        {
            return null;
        }

        // Split on a PascalCase "And" boundary — capital "And" followed by an uppercase letter and
        // preceded by a lowercase letter or digit — so "CountryAndCity" splits but "Brand" (lowercase
        // 'and') and "AndrewId" (leading "And") stay whole.
        var segments = new List<string>();
        var start = 0;
        for (var i = 1; i + 3 < fieldsPart.Length; i++)
        {
            if (fieldsPart[i] == 'A' && fieldsPart[i + 1] == 'n' && fieldsPart[i + 2] == 'd'
                && char.IsUpper(fieldsPart[i + 3])
                && (char.IsLower(fieldsPart[i - 1]) || char.IsDigit(fieldsPart[i - 1])))
            {
                segments.Add(fieldsPart.Substring(start, i - start));
                start = i + 3;
                i = start; // resume scanning after the consumed "And"
            }
        }

        segments.Add(fieldsPart.Substring(start));
        return segments.ToArray();
    }

    private static ParameterData ToParameterData(IParameterSymbol parameter) => new(
        parameter.Name,
        parameter.Type.ToDisplayString(KnownSymbols.FullyQualifiedNullableFormat),
        parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        GeneratorHelpers.IsCancellationToken(parameter.Type))
    {
        ElementComparisonDisplay = GetEnumerableElementComparisonDisplay(parameter.Type),
        DefaultValueLiteral = GetDefaultValueLiteral(parameter),
    };

    private static bool HasGenerateInterfaceAttribute(INamedTypeSymbol storeSymbol)
        => storeSymbol.GetAttributes().Any(static a =>
            a.AttributeClass?.Name == "InquiryGenerateInterfaceAttribute" && GeneratorHelpers.IsStoreAttribute(a));

    /// <summary>
    /// Renders the parameter's explicit default value as source, or null when it has none. Only the
    /// constant shapes legal as default parameter values need handling: null / <c>default</c>, bool,
    /// char, string, enum (rendered as a fully-qualified cast of the underlying constant), and the
    /// numeric primitives (suffixed where the bare literal would not convert, e.g. <c>float</c>).
    /// Consumed by the generated <c>I{StoreName}</c> interface signatures (see
    /// <see cref="ParameterData.DefaultValueLiteral"/>).
    /// </summary>
    private static string? GetDefaultValueLiteral(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            return null;
        }

        var value = parameter.ExplicitDefaultValue;
        if (value is null)
        {
            // `= null` on a reference / nullable-value type, or `= default` on a value type
            // (the trailing CancellationToken being the overwhelmingly common case).
            return parameter.Type.IsValueType ? "default" : "null";
        }

        var type = parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : parameter.Type;

        if (type.TypeKind == TypeKind.Enum)
        {
            return $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)})";
        }

        return value switch
        {
            bool b => b ? "true" : "false",
            string s => "\"" + GeneratorHelpers.Escape(s) + "\"",
            char c => SymbolDisplay.FormatPrimitive(c, quoteStrings: true, useHexadecimalNumbers: false),
            float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f",
            double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d",
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
            uint ui => ui.ToString(System.Globalization.CultureInfo.InvariantCulture) + "U",
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            ulong ul => ul.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL",
            _ => System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "default",
        };
    }

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

            builder.Add(new PredicateData(field, op, GeneratorHelpers.GetNamedBool(candidate, "Or"))
            {
                JsonPath = GeneratorHelpers.GetNamedString(candidate, "JsonPath"),
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Parses an <c>OrderBy</c> attribute string (<c>"field [ASC|DESC], field2 [ASC|DESC]"</c>) into
    /// ordered terms. Fields are kept raw (resolved + quoted at emit). v1 supports only
    /// <c>field [ASC|DESC]</c> — collation/NULLS ordering is out of scope. A direction token that
    /// is not ASC or DESC (case-insensitive), or any trailing tokens beyond the direction, reports
    /// INQ042 — the pre-INQ042 parser silently fell back to ASC on typos like "DESCS" or "DEC".
    /// </summary>
    private static ImmutableArray<OrderItem> ParseOrderBy(
        string? spec,
        string methodName,
        Location? location,
        ImmutableArray<DiagnosticData>.Builder diagnostics)
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
            var descending = false;

            if (parts.Length == 2)
            {
                if (string.Equals(parts[1], "DESC", StringComparison.OrdinalIgnoreCase))
                {
                    descending = true;
                }
                else if (!string.Equals(parts[1], "ASC", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.InvalidOrderByDirection, location, methodName, term, parts[1]));
                    continue;
                }
            }
            else if (parts.Length > 2)
            {
                // Trailing garbage (e.g. "Name ASC NULLS FIRST") — name the first unrecognised token.
                diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.InvalidOrderByDirection, location, methodName, term, parts[2]));
                continue;
            }

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
                case "InquiryCountAttribute": attribute = candidate; return StoreOperation.Count;
                case "InquiryAggregateAttribute": attribute = candidate; return StoreOperation.Aggregate;
                case "InquiryFullTextSearchAttribute": attribute = candidate; return StoreOperation.FullTextSearch;
                case "InquiryInsertAllAttribute": attribute = candidate; return StoreOperation.InsertAll;
                case "InquiryBulkInsertAttribute": attribute = candidate; return StoreOperation.BulkInsert;
                case "InquiryDeleteAllAttribute": attribute = candidate; return StoreOperation.DeleteAll;
                case "InquiryUpdateAllAttribute": attribute = candidate; return StoreOperation.UpdateAll;
                case "InquiryInsertAttribute": attribute = candidate; return StoreOperation.Insert;
                case "InquiryUpdateAttribute": attribute = candidate; return StoreOperation.Update;
                case "InquiryUpsertAttribute": attribute = candidate; return StoreOperation.Upsert;
                case "InquiryDeleteOneByKeyAttribute": attribute = candidate; return StoreOperation.DeleteOneByKey;
                case "InquiryUpdateWhereAttribute": attribute = candidate; return StoreOperation.UpdateByPredicate;
                case "InquiryDeleteWhereAttribute": attribute = candidate; return StoreOperation.DeleteByPredicate;
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

    private static bool HasSupportedReturnType(StoreOperation operation, ITypeSymbol returnType, ITypeSymbol entityType, bool returnsEntity, AttributeData attribute)
    {
        return operation switch
        {
            // SelectAll and SelectAllByField element types may be the entity OR an
            // [InquiryProjection] of it, so they are accepted by shape here (any named element) and
            // resolved against the projection registry at emit.
            StoreOperation.SelectAll or StoreOperation.SelectAllByField =>
                TryGetSelectElementType(returnType, out _, out _),
            StoreOperation.SelectAllByPredicate or StoreOperation.FullTextSearch =>
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
            StoreOperation.Count =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Int64),
            StoreOperation.Aggregate => IsTaskOfSingleTypeArgument(returnType),
            StoreOperation.InsertAll or StoreOperation.DeleteAll or StoreOperation.UpdateAll or
            StoreOperation.UpdateByPredicate or StoreOperation.DeleteByPredicate =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Int32),
            // Bulk insert returns the rows-written count as long (SqlBulkCopy's RowsCopied is Int64).
            StoreOperation.BulkInsert =>
                GeneratorHelpers.IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Int64),
            // With OutputParameter/ReturnsValue, the method shape is Task<TScalar> for any single
            // scalar T; the detailed validation (mutual exclusion, RETURN-must-be-int) emits INQ051
            // in discovery. Without it, the classic IAsyncEnumerable/Task<Entity?>/Task<int> shapes.
            StoreOperation.StoredProcedure when HasScalarProcedureOutput(attribute) =>
                IsTaskOfSingleTypeArgument(returnType),
            StoreOperation.StoredProcedure =>
                ClassifyProcedureReturn(returnType, entityType) != ProcedureReturnKind.None,
            _ => false,
        };
    }

    /// <summary>True when an <c>[InquiryStoredProcedure]</c> sets <c>OutputParameter</c> or <c>ReturnsValue</c>.</summary>
    private static bool HasScalarProcedureOutput(AttributeData attribute)
        => !string.IsNullOrEmpty(GeneratorHelpers.GetNamedString(attribute, "OutputParameter"))
            || GeneratorHelpers.GetNamedBool(attribute, "ReturnsValue");

    private static bool IsEnumerableOfEntity(ParameterData parameter, EntityData entity)
        => IsEnumerableOfType(parameter, entity.FullyQualifiedName);

    /// <summary>True when the parameter is a common read-only collection of <paramref name="elementTypeDisplay"/>.</summary>
    private static bool IsEnumerableOfType(ParameterData parameter, string elementTypeDisplay)
    {
        var fqn = elementTypeDisplay;
        var d = parameter.ComparisonDisplay;
        return d == "global::System.Collections.Generic.IEnumerable<" + fqn + ">"
            || d == "global::System.Collections.Generic.IReadOnlyList<" + fqn + ">"
            || d == "global::System.Collections.Generic.IReadOnlyCollection<" + fqn + ">"
            || d == "global::System.Collections.Generic.IList<" + fqn + ">"
            || d == "global::System.Collections.Generic.ICollection<" + fqn + ">"
            || d == "global::System.Collections.Generic.List<" + fqn + ">";
    }

    private static bool IsTaskOfSingleTypeArgument(ITypeSymbol returnType)
        => returnType is INamedTypeSymbol task
            && task.IsGenericType
            && task.TypeArguments.Length == 1
            && task.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Threading.Tasks.Task<TResult>";

    /// <summary>
    /// Extracts the element type of a select-list return — the <c>T</c> in
    /// <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> (<paramref name="isList"/> true) or
    /// <c>IAsyncEnumerable&lt;T&gt;</c> (false), where <c>T</c> is a named type. Returns false for any
    /// other shape. The element may be the store's entity or an <c>[InquiryProjection]</c> of it.
    /// </summary>
    private static bool TryGetSelectElementType(ITypeSymbol returnType, out INamedTypeSymbol element, out bool isList)
    {
        element = null!;
        isList = false;

        if (returnType is not INamedTypeSymbol outer || !outer.IsGenericType || outer.TypeArguments.Length != 1)
        {
            return false;
        }

        var outerName = outer.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (outerName == "global::System.Collections.Generic.IAsyncEnumerable<T>")
        {
            if (outer.TypeArguments[0] is INamedTypeSymbol asyncElement)
            {
                element = asyncElement;
                isList = false;
                return true;
            }

            return false;
        }

        if (outerName == "global::System.Threading.Tasks.Task<TResult>" &&
            outer.TypeArguments[0] is INamedTypeSymbol inner &&
            inner.IsGenericType &&
            inner.TypeArguments.Length == 1 &&
            inner.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Collections.Generic.IReadOnlyList<T>" &&
            inner.TypeArguments[0] is INamedTypeSymbol listElement)
        {
            element = listElement;
            isList = true;
            return true;
        }

        return false;
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
        IReadOnlyDictionary<string, ProjectionData> projections,
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

        // Resolve the parent's relation → child-entity map up-front so per-method validation can
        // diagnose relation-shape errors (missing foreign-key column, composite-key child) before
        // the emitter runs. Previously this was built later (just before emission), causing
        // bad relations to surface as null-forgive NREs at generator time or invalid generated
        // C# with no clear diagnostic.
        var relationChildEntities = BuildRelationChildEntities(entity, entities);

        // Per-method combined validation. Successful methods carry their resolved field columns and,
        // for SelectAllByPredicate, the resolved predicate plan; ordered/paged/keyset selects also carry
        // a resolved select plan (ORDER BY columns + pagination).
        var valid = new List<(StoreMethodData Method, IReadOnlyList<ColumnData> FieldColumns, ResolvedPredicatePlan? PredicatePlan, ResolvedSelectPlan? SelectPlan)>();
        foreach (var method in store.Methods)
        {
            if (TryValidateForEmit(context, method, entity, relationChildEntities, sqlBuilder, out var fieldColumns, out var predicatePlan, out var selectPlan))
            {
                valid.Add((method, fieldColumns, predicatePlan, selectPlan));
            }
        }

        // resolve projection-returning SelectAll methods. A select whose element type is not the
        // store's entity must be a known [InquiryProjection] of it. Soft-delete / global-filter entities
        // are supported: the projection SELECT AND-composes the entity's active-row filter (the projection
        // context is built with those columns, below). Invalid ones are diagnosed and dropped.
        var projectionMethods = new Dictionary<string, ProjectionData>(StringComparer.Ordinal);
        for (var i = valid.Count - 1; i >= 0; i--)
        {
            var m = valid[i].Method;
            if (m.ResultElementTypeFqn is null || m.ResultElementTypeFqn == store.EntityFullyQualifiedName)
            {
                continue;
            }

            if (!projections.TryGetValue(m.ResultElementTypeFqn, out var projection))
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.ProjectionNotMapped,
                    m.Location?.ToLocation(), m.Name, StripGlobalPrefix(m.ResultElementTypeFqn)));
                valid.RemoveAt(i);
                continue;
            }

            if (projection.EntityFullyQualifiedName != store.EntityFullyQualifiedName)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.ProjectionEntityMismatch,
                    m.Location?.ToLocation(), m.Name, StripGlobalPrefix(m.ResultElementTypeFqn),
                    StripGlobalPrefix(projection.EntityFullyQualifiedName), StripGlobalPrefix(store.EntityFullyQualifiedName)));
                valid.RemoveAt(i);
                continue;
            }

            projectionMethods[m.Name] = projection;
        }

        if (valid.Count == 0)
        {
            return null;
        }

        // a database-managed concurrency token (e.g. rowversion) is only supported on dialects with a
        // native row-version type — currently SQL Server. On any other dialect it has no portable
        // semantics, so reject it at emit (reusing INQ006; the reserved block is fully claimed by the
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

        var entityColumns = ToColumnList(entity.Columns);
        var ctx = new SqlBuildContext(sqlBuilder, entity.Schema, entity.TableName, entityColumns);

        // [InquiryGlobalFilter] columns: a projection's column subset omits them, so they must be passed
        // explicitly to projection contexts (like the soft-delete column) to keep the active-row filter intact.
        var entityGlobalFilters = entityColumns.Where(static c => c.IsGlobalFilter).ToList();

        // when the entity has a soft-delete column, an IncludeDeleted select is built from a context
        // with the soft-delete filter suppressed (keeps the SqlBuilder select signatures stable). When
        // there is no soft-delete column this is identical to ctx and is never used.
        var hasSoftDelete = entity.SoftDeleteColumn is not null;
        var ctxIncludeDeleted = hasSoftDelete
            ? new SqlBuildContext(sqlBuilder, entity.Schema, entity.TableName, entityColumns, suppressSoftDelete: true)
            : ctx;
        SqlBuildContext CtxFor(StoreMethodData m) => hasSoftDelete && m.IncludeDeleted ? ctxIncludeDeleted : ctx;

        // Keyless (view) entities have no key column; the database-supplied-key upsert check below is
        // upsert-only, and views reject upserts (INQ052), so a keyless entity short-circuits to false.
        var key = entity.Keys.Count > 0 ? entity.Keys[0] : null;
        var keyMayBeDatabaseSupplied = key is not null && (key.IsGenerated || key.UseDatabaseDefault);
        var nullableDatabaseSuppliedKeyUpsert = keyMayBeDatabaseSupplied && key!.Type.IsNullable &&
            valid.Any(static m => m.Method.Operation == StoreOperation.Upsert);

        // A SelectAll method with a resolved plan (ORDER BY / paging) emits its own per-method const, so
        // only a plain SelectAll or any SelectAllEager needs the shared _sqlSelectAll. A method that
        // opts into IncludeDeleted gets its own unfiltered per-method const instead of the shared one.
        bool UsesSharedSelect((StoreMethodData Method, IReadOnlyList<ColumnData> FieldColumns, ResolvedPredicatePlan? PredicatePlan, ResolvedSelectPlan? SelectPlan) m)
            => !(hasSoftDelete && m.Method.IncludeDeleted);

        var needsSelectAll = valid.Any(m => UsesSharedSelect(m) &&
            ((m.Method.Operation == StoreOperation.SelectAll && m.SelectPlan is null) ||
             m.Method.Operation == StoreOperation.SelectAllEager));
        var needsSelectByKey = valid.Any(m => UsesSharedSelect(m) && m.Method.Operation is StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager);
        var needsInsert = valid.Any(static m => m.Method.Operation == StoreOperation.Insert && !m.Method.ReturnsEntity) ||
            nullableDatabaseSuppliedKeyUpsert && valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && !m.Method.ReturnsEntity);
        // UpdateAll reuses the single-row _sqlUpdate const (one UPDATE per item via the batch API).
        var needsUpdate = valid.Any(static m =>
            (m.Method.Operation == StoreOperation.Update && !m.Method.ReturnsEntity) ||
            m.Method.Operation == StoreOperation.UpdateAll);
        var needsUpsert = valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && !m.Method.ReturnsEntity);
        var needsInsertReturning = valid.Any(static m => m.Method.Operation == StoreOperation.Insert && m.Method.ReturnsEntity) ||
            nullableDatabaseSuppliedKeyUpsert && valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && m.Method.ReturnsEntity);
        var needsUpdateReturning = valid.Any(static m => m.Method.Operation == StoreOperation.Update && m.Method.ReturnsEntity);
        var needsUpsertReturning = valid.Any(static m => m.Method.Operation == StoreOperation.Upsert && m.Method.ReturnsEntity);

        // delete routing. The shared _sqlDeleteByKey is the "default" delete statement: a literal
        // DELETE for an ordinary entity, or the soft UPDATE for a soft-delete entity. A HardDelete method
        // on a soft-delete entity additionally needs a separate literal-DELETE const so both can coexist.
        var needsDeleteByKey = valid.Any(m => m.Method.Operation == StoreOperation.DeleteOneByKey && !(m.Method.HardDelete && hasSoftDelete));
        var needsHardDeleteByKey = hasSoftDelete && valid.Any(static m => m.Method.Operation == StoreOperation.DeleteOneByKey && m.Method.HardDelete);
        var needsRestore = valid.Any(static m => m.Method.Operation == StoreOperation.RestoreOneByKey);
        var needsCount = valid.Any(static m => m.Method.Operation == StoreOperation.Count);
        // A [InquiryBulkInsert] on a dialect without a native bulk-copy API compiles down to the
        // batch-insert body, so it needs the same baked consts.
        var needsInsertAll = valid.Any(m => m.Method.Operation == StoreOperation.InsertAll
            || (m.Method.Operation == StoreOperation.BulkInsert && !sqlBuilder.SupportsBulkCopy));
        var needsDeleteAll = valid.Any(static m => m.Method.Operation == StoreOperation.DeleteAll);

        var byFieldOps = valid
            .Where(m => UsesSharedSelect(m) && m.Method.Operation == StoreOperation.SelectAllByField && m.SelectPlan is null && m.FieldColumns.Count > 0)
            .GroupBy(static m => StoreOperationEmitter.BuildFieldSuffix(m.FieldColumns))
            .Select(static g => g.First().FieldColumns)
            .ToArray();

        // per-method base SELECT const name for each non-plan select. Defaults to the shared const;
        // an IncludeDeleted select on a soft-delete entity gets its own unfiltered per-method const.
        var baseSelectFields = new Dictionary<string, string>(System.StringComparer.Ordinal);

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        GeneratorHelpers.AppendNamespaceStart(source, store.Namespace);
        // An [InquiryGenerateInterface] store implements its generated I{Store} interface; adding
        // the base list on the generated partial declaration is legal and keeps the user's half clean.
        source.AppendLine(store.GenerateInterface
            ? $"partial class {store.Name} : I{store.Name}"
            : $"partial class {store.Name}");
        source.AppendLine("{");

        if (needsSelectAll) AppendConstSql(source, "_sqlSelectAll", sqlBuilder.BuildSelectAllSql(ctx));
        if (needsSelectByKey) AppendConstSql(source, "_sqlSelectByKey", sqlBuilder.BuildSelectByKeySql(ctx));
        if (needsInsert) AppendConstSql(source, "_sqlInsert", sqlBuilder.BuildInsertSql(ctx));
        if (needsUpdate) AppendConstSql(source, "_sqlUpdate", sqlBuilder.BuildUpdateSql(ctx));
        // Some operations are not universally supported (e.g. Oracle has no RETURNING result set, and no
        // MERGE upsert on a database-generated key). The builder throws NotSupportedException for those;
        // catch it so a single unsupported operation degrades to a diagnostic + throwing stub (below)
        // instead of silently aborting the whole generator. A null reason means "supported (or not needed)".
        var upsertError = TryBuildDegradableConst(source, "_sqlUpsert", needsUpsert, () => sqlBuilder.BuildUpsertSql(ctx));
        var insertReturningError = TryBuildDegradableConst(source, "_sqlInsertReturning", needsInsertReturning, () => sqlBuilder.BuildInsertReturningSql(ctx));
        var updateReturningError = TryBuildDegradableConst(source, "_sqlUpdateReturning", needsUpdateReturning, () => sqlBuilder.BuildUpdateReturningSql(ctx));
        var upsertReturningError = TryBuildDegradableConst(source, "_sqlUpsertReturning", needsUpsertReturning, () => sqlBuilder.BuildUpsertReturningSql(ctx));
        if (needsDeleteByKey) AppendConstSql(source, "_sqlDeleteByKey", hasSoftDelete ? sqlBuilder.BuildSoftDeleteByKeySql(ctx) : sqlBuilder.BuildDeleteByKeySql(ctx));
        if (needsHardDeleteByKey) AppendConstSql(source, "_sqlHardDeleteByKey", sqlBuilder.BuildDeleteByKeySql(ctx));
        if (needsRestore) AppendConstSql(source, "_sqlRestoreByKey", sqlBuilder.BuildRestoreByKeySql(ctx));
        if (needsCount) AppendConstSql(source, "_sqlCount", sqlBuilder.BuildCountSql(ctx));
        // InsertAll is supported on every dialect via the SqlBuilder batch-insert shape hooks (Oracle emits
        // INSERT ALL … SELECT FROM dual; everyone else multi-row VALUES). The header + per-row open are
        // baked consts the emitter assembles at runtime. DeleteAll uses the IN-expansion path (every dialect).
        if (needsInsertAll)
        {
            AppendConstSql(source, "_sqlInsertAllPrefix", sqlBuilder.BuildBatchInsertHeader(ctx));
            AppendConstSql(source, "_sqlInsertAllRowOpen", sqlBuilder.BuildBatchInsertRowOpen(ctx));
        }
        if (needsDeleteAll) AppendConstSql(source, "_sqlDeleteAll", hasSoftDelete ? sqlBuilder.BuildSoftDeleteAllByKeysSql(ctx) : sqlBuilder.BuildDeleteAllByKeysSql(ctx));

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

        // Per-method set-based mutation consts. UpdateByPredicate carries its SET columns in the
        // resolved field columns; DeleteByPredicate picks the soft UPDATE form on a soft-delete entity
        // unless HardDelete forces the literal DELETE (mirroring DeleteOneByKey's routing).
        foreach (var (method, fieldColumns, predicatePlan, _) in valid)
        {
            if (predicatePlan is null)
            {
                continue;
            }

            if (method.Operation == StoreOperation.UpdateByPredicate)
            {
                AppendConstSql(source, "_sqlUpdateWhere_" + method.Name, sqlBuilder.BuildUpdateByPredicateSql(ctx, ToColumnList(fieldColumns), predicatePlan.Predicates));
            }
            else if (method.Operation == StoreOperation.DeleteByPredicate)
            {
                AppendConstSql(source, "_sqlDeleteWhere_" + method.Name, hasSoftDelete && !method.HardDelete
                    ? sqlBuilder.BuildSoftDeleteByPredicateSql(ctx, predicatePlan.Predicates)
                    : sqlBuilder.BuildDeleteByPredicateSql(ctx, predicatePlan.Predicates));
            }
        }

        foreach (var (method, _, _, _) in valid)
        {
            if (method.Operation == StoreOperation.Aggregate)
            {
                var aggColumn = FindColumn(entity, method.AggregateColumn!)!;
                AppendConstSql(source, "_sqlAgg_" + method.Name, sqlBuilder.BuildAggregateSql(ctx, method.AggregateFunction!, sqlBuilder.QuoteIdentifier(aggColumn.ColumnName)));
            }
        }

        foreach (var (method, fieldColumns, _, _) in valid)
        {
            if (method.Operation == StoreOperation.FullTextSearch)
            {
                AppendConstSql(source, "_sqlFts_" + method.Name, sqlBuilder.BuildFullTextSearchSql(CtxFor(method), fieldColumns));
            }
        }

        // Ordered / paged / keyset selects each get a self-contained per-method const built from the
        // base SELECT plus a uniform ORDER BY and a (dialect-specific) pagination or keyset tail.
        foreach (var (method, fieldColumns, _, selectPlan) in valid)
        {
            if (selectPlan is not null)
            {
                // an ordered/paged projection method builds its plan SQL over the projection's columns,
                // composing the entity's active-row filter (soft-delete suppressed for IncludeDeleted)
                // just like a non-projection select — the projection columns don't carry those
                // indicator/filter columns, so pass them explicitly.
                var planCtx = projectionMethods.TryGetValue(method.Name, out var projForPlan)
                    ? new SqlBuildContext(sqlBuilder, entity.Schema, entity.TableName, ToColumnList(projForPlan.Columns),
                        suppressSoftDelete: hasSoftDelete && method.IncludeDeleted,
                        softDeletePredicateColumn: entity.SoftDeleteColumn,
                        globalFilterPredicateColumns: entityGlobalFilters)
                    : CtxFor(method);
                AppendConstSql(source, selectPlan.SqlFieldName, BuildSelectPlanSql(sqlBuilder, planCtx, fieldColumns, selectPlan));

                // Keyset paging emits a second const: the first-page (null-cursor) query has no cursor
                // predicate, so the seek query above can use the plain sargable `key > @cursor` (index seek)
                // instead of a non-sargable (@cursor IS NULL OR …) guard. The emitter picks between them.
                if (selectPlan.Pagination == Pagination.Keyset)
                {
                    AppendConstSql(source, selectPlan.SqlFieldName + "_first", BuildKeysetFirstPageSql(sqlBuilder, planCtx, selectPlan));
                }
            }
        }

        // emit an unfiltered per-method base SELECT const for each non-plan IncludeDeleted select on
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

        // Relation SELECT consts (_sql_<PropertyName>) are consumed only by the eager loaders
        // (EmitSelectAllEager / EmitSelectOneByKeyEager). Emit them only when a valid eager method
        // survives. This avoids dead consts and, crucially, avoids running the relation-const
        // emission for a malformed relation (e.g. a typo'd collection foreign key) on a store with
        // no eager method, which would otherwise null-forgive FindColumn(...) into an NRE. A bad
        // relation that IS eager-loaded is reported as INQ040/INQ041 by TryValidateForEmit, which
        // drops the eager method — so no valid eager method remains and this block is skipped.
        var hasEagerMethod = valid.Any(static m =>
            m.Method.Operation is StoreOperation.SelectAllEager or StoreOperation.SelectOneByKeyEager);
        if (relationChildEntities.Count > 0 && hasEagerMethod)
        {
            var emittedRelations = new HashSet<string>();
            foreach (var relation in entity.Relations)
            {
                if (!relationChildEntities.TryGetValue(relation.PropertyName, out var childEntity))
                {
                    continue;
                }

                // Dedup by relation property name, not child entity type. A parent with two
                // navigations to the same child (CreatedBy / UpdatedBy → User) needs distinct
                // _sql_<PropertyName> consts: each generated eager loader references its own
                // relation's const by property name (line 877-878). Deduping by child type
                // would skip the second relation's consts and leave a dangling reference.
                if (!emittedRelations.Add(relation.PropertyName))
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

        // one SELECT-over-projection-columns const per non-plan projection-returning method
        // (SelectAll → SELECT all projection cols; SelectAllByField → … WHERE field = @x). Ordered/paged
        // projection methods get their const from the plan loop above (also over the projection ctx).
        // On a soft-delete entity the projection AND-composes the active-row filter (suppressed for
        // IncludeDeleted): the projection columns don't carry the indicator, so the entity's soft-delete
        // column is passed in for predicate computation only — it never joins the projection SELECT list.
        foreach (var (method, fieldColumns, _, selectPlan) in valid)
        {
            if (selectPlan is not null || !projectionMethods.TryGetValue(method.Name, out var proj))
            {
                continue;
            }

            var projCtx = new SqlBuildContext(sqlBuilder, entity.Schema, entity.TableName, ToColumnList(proj.Columns),
                suppressSoftDelete: hasSoftDelete && method.IncludeDeleted,
                softDeletePredicateColumn: entity.SoftDeleteColumn,
                globalFilterPredicateColumns: entityGlobalFilters);
            var projSql = method.Operation == StoreOperation.SelectAllByField
                ? sqlBuilder.BuildSelectByFieldSql(projCtx, ToColumnList(fieldColumns))
                : sqlBuilder.BuildSelectAllSql(projCtx);
            AppendConstSql(source, "_sqlProj_" + method.Name, projSql);
        }

        source.AppendLine();
        source.AppendLine($"    public {store.Name}(global::Inquiry.IInquiry inquiry)");
        source.AppendLine("        : base(inquiry)");
        source.AppendLine("    {");
        source.AppendLine("    }");

        foreach (var (method, fieldColumns, predicatePlan, selectPlan) in valid)
        {
            source.AppendLine();

            // Graceful degradation: if this method's RETURNING operation could not be emitted for the
            // active dialect, report INQ039 and emit a throwing stub instead of an un-compilable body.
            var unsupportedReason = method.Operation switch
            {
                StoreOperation.Insert when method.ReturnsEntity => insertReturningError,
                StoreOperation.Update when method.ReturnsEntity => updateReturningError,
                // Returning upsert uses _sqlUpsertReturning, or _sqlInsertReturning on the null-key path.
                StoreOperation.Upsert when method.ReturnsEntity => upsertReturningError ?? insertReturningError,
                // Non-returning upsert uses _sqlUpsert (throws for a generated-key MERGE on Oracle).
                StoreOperation.Upsert => upsertError,
                _ => null,
            };
            if (unsupportedReason is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InquiryDiagnosticDescriptors.DialectOperationNotSupported,
                    method.Location?.ToLocation(),
                    method.Name, sqlBuilder.DialectName, unsupportedReason));
                StoreOperationEmitter.EmitUnsupportedStub(source, method, unsupportedReason);
                continue;
            }

            if (projectionMethods.TryGetValue(method.Name, out var projection))
            {
                // Project: select the projection's columns and materialize the projection type.
                StoreOperationEmitter.Emit(source, method, fieldColumns, predicatePlan, selectPlan, entity, relationChildEntities,
                    sqlBuilder, "_sqlProj_" + method.Name, projection.FullyQualifiedName, projection.StructMaterializerFullName);
            }
            else
            {
                baseSelectFields.TryGetValue(method.Name, out var baseSelectField);
                StoreOperationEmitter.Emit(source, method, fieldColumns, predicatePlan, selectPlan, entity, relationChildEntities, sqlBuilder, baseSelectField);
            }
        }

        source.AppendLine("}");

        string? interfaceFullyQualifiedName = null;
        if (store.GenerateInterface)
        {
            interfaceFullyQualifiedName = store.Namespace is null
                ? $"global::I{store.Name}"
                : $"global::{store.Namespace}.I{store.Name}";
            EmitStoreInterface(source, store, valid.Select(static m => m.Method));
        }

        GeneratorHelpers.AppendNamespaceEnd(source, store.Namespace);

        context.AddSource($"{store.Name}.InquiryStore.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        return new StoreRegistration(store.FullyQualifiedName, interfaceFullyQualifiedName);
    }

    /// <summary>
    /// Emits the <c>public partial interface I{StoreName}</c> for an <c>[InquiryGenerateInterface]</c>
    /// store, mirroring the signature of every generator-implemented store method. Unlike the
    /// implementation half (where repeating a default fires CS1066), the interface carries each
    /// parameter's default value so optional arguments survive calls through the interface.
    /// </summary>
    private static void EmitStoreInterface(StringBuilder source, StoreData store, IEnumerable<StoreMethodData> methods)
    {
        source.AppendLine();
        source.AppendLine("/// <summary>");
        source.AppendLine($"/// Generated interface for <see cref=\"{store.Name}\"/>. Registered in DI as a scoped");
        source.AppendLine($"/// forward to the concrete store, so services can depend on (and mock) the interface.");
        source.AppendLine("/// </summary>");
        source.AppendLine($"public partial interface I{store.Name}");
        source.AppendLine("{");

        var first = true;
        foreach (var method in methods)
        {
            if (!first)
            {
                source.AppendLine();
            }
            first = false;

            source.AppendLine($"    /// <summary>Generated signature of <c>{store.Name}.{method.Name}</c>.</summary>");
            source.AppendLine($"    {method.ReturnTypeDisplay} {method.Name}({StoreOperationEmitter.GetInterfaceParameterDeclaration(method.Parameters)});");
        }

        source.AppendLine("}");
    }

    /// <summary>True for store operations that write — everything an [InquiryView] entity forbids.</summary>
    private static bool IsMutatingOperation(StoreOperation operation)
        => operation is StoreOperation.Insert or StoreOperation.Update or StoreOperation.Upsert
            or StoreOperation.DeleteOneByKey or StoreOperation.RestoreOneByKey
            or StoreOperation.InsertAll or StoreOperation.BulkInsert or StoreOperation.UpdateAll
            or StoreOperation.DeleteAll or StoreOperation.UpdateByPredicate or StoreOperation.DeleteByPredicate;

    private static bool TryValidateForEmit(SourceProductionContext context, StoreMethodData method, EntityData entity, IReadOnlyDictionary<string, EntityData> relationChildEntities, SqlBuilder sqlBuilder, out IReadOnlyList<ColumnData> fieldColumns, out ResolvedPredicatePlan? predicatePlan, out ResolvedSelectPlan? selectPlan)
    {
        fieldColumns = Array.Empty<ColumnData>();
        predicatePlan = null;
        selectPlan = null;

        // A view-mapped entity is read-only: reject any non-read operation up front (INQ052). That
        // is every mutating operation, plus [InquiryStoredProcedure] — a procedure is arbitrary SQL
        // not bound to the view and can write, so it must not ride a read-only view store.
        if (entity.IsView && (IsMutatingOperation(method.Operation) || method.Operation == StoreOperation.StoredProcedure))
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.ViewIsReadOnly, method.Location?.ToLocation(), method.Name, StripGlobalPrefix(entity.FullyQualifiedName)));
            return false;
        }

        // A key-based select or eager load needs a key. Only a keyless view can reach this (tables
        // always have a key); reject it here so emission never dereferences a missing key (INQ053).
        if (entity.Keys.Count == 0 &&
            method.Operation is StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager or StoreOperation.SelectAllEager)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.OperationRequiresKey, method.Location?.ToLocation(), method.Name, StripGlobalPrefix(entity.FullyQualifiedName)));
            return false;
        }

        // restore only makes sense on a soft-delete entity. Without the indicator column the restore
        // UPDATE has nothing to clear, so reject the method (reusing the invalid-parameters diagnostic —
        // the ID block, INQ033/INQ034, is fully claimed by the column-level diagnostics).
        if (method.Operation == StoreOperation.RestoreOneByKey && entity.SoftDeleteColumn is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.InvalidParameters, method.Location?.ToLocation(), method.Name));
            return false;
        }

        if (method.Operation == StoreOperation.KeysetPage)
        {
            return TryValidateKeysetPage(context, method, entity, out selectPlan);
        }

        // Set-based mutations bypass the token check/advance (the WHERE binds no @token and the SET
        // never bumps it), so a concurrency-token entity rejects them outright — same rationale as the
        // batch-mutation rejection, hence the shared INQ022.
        if (entity.ConcurrencyToken is not null && method.Operation is StoreOperation.UpdateAll or StoreOperation.DeleteAll
            or StoreOperation.UpdateByPredicate or StoreOperation.DeleteByPredicate)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.BatchMutationUnsupportedWithConcurrencyToken,
                method.Location?.ToLocation(),
                method.Name,
                entity.Name));
            return false;
        }

        if (method.Operation == StoreOperation.FullTextSearch && !sqlBuilder.SupportsFullTextSearch)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.FullTextSearchNotSupported, method.Location?.ToLocation(), method.Name));
            return false;
        }

        if (method.Operation is StoreOperation.SelectAllByField or StoreOperation.FullTextSearch)
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

        if (method.Operation == StoreOperation.Aggregate && FindColumn(entity, method.AggregateColumn!) is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.UnknownField, method.Location?.ToLocation(), method.Name, method.AggregateColumn));
            return false;
        }

        if (method.Operation == StoreOperation.SelectAllByPredicate)
        {
            if (!TryResolvePredicates(context, method, entity, sqlBuilder, out predicatePlan))
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

        if (method.Operation is StoreOperation.UpdateByPredicate or StoreOperation.DeleteByPredicate)
        {
            // SET columns (UpdateByPredicate only; FieldNames is empty for DeleteByPredicate). Each
            // field must resolve to a column the ORM may assign: not a key, not database-generated,
            // not the soft-delete indicator (owned by delete/restore), not a concurrency token.
            var setColumns = new List<ColumnData>(method.FieldNames.Count);
            foreach (var name in method.FieldNames)
            {
                var column = FindColumn(entity, name);
                if (column is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.UnknownField, method.Location?.ToLocation(), method.Name, name));
                    return false;
                }

                if (column.IsKey || column.IsGenerated || column.SoftDelete != SoftDeleteKind.None || column.IsConcurrencyToken)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SetFieldNotUpdatable, method.Location?.ToLocation(), method.Name, name));
                    return false;
                }

                setColumns.Add(column);
            }

            fieldColumns = setColumns;

            // Trailing CancellationToken plus enough leading parameters for the SET values; the first
            // N non-token parameters must match the SET columns' types positionally.
            if (method.Parameters.Count == 0 ||
                !method.Parameters[method.Parameters.Count - 1].IsCancellationToken ||
                method.Parameters.Count - 1 < setColumns.Count)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.InvalidParameters, method.Location?.ToLocation(), method.Name));
                return false;
            }

            for (var i = 0; i < setColumns.Count; i++)
            {
                if (!ParameterMatchesColumn(method.Parameters[i], setColumns[i]))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.InvalidParameters, method.Location?.ToLocation(), method.Name));
                    return false;
                }
            }

            // The remaining parameters bind the [InquiryWhere] criteria positionally; passing the SET
            // columns seeds both the positional cursor and the parameter-name pool (see
            // TryResolvePredicates) so predicate names can never collide with SET names.
            return TryResolvePredicates(context, method, entity, sqlBuilder, out predicatePlan, setColumns);
        }

        if (method.Operation is StoreOperation.SelectAllEager or StoreOperation.SelectOneByKeyEager && entity.Keys.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.EagerLoadingOnCompositeKeyParent, method.Location?.ToLocation(), method.Name, entity.Name));
            return false;
        }

        // For each relation the eager-loading method will traverse, an invalid shape (typo'd
        // ForeignKey or composite-key child) would null-forgive into an NRE / invalid C# at emit.
        // Those are reported once at declaration time (ValidateRelations); here we only DROP the
        // eager method so the emitter never processes the bad relation.
        if (method.Operation is StoreOperation.SelectAllEager or StoreOperation.SelectOneByKeyEager)
        {
            foreach (var relation in entity.Relations)
            {
                if (!relationChildEntities.TryGetValue(relation.PropertyName, out var childEntity))
                {
                    // Relation to a non-[InquiryTable] type; the emitter handles this gracefully.
                    continue;
                }

                var ownerHasFk = relation.IsCollection
                    ? FindColumn(childEntity, relation.ForeignKeyProperty) is not null
                    : FindColumn(entity, relation.ForeignKeyProperty) is not null;

                if (!ownerHasFk || childEntity.Keys.Count > 1)
                {
                    return false;
                }
            }
        }

        // ORDER BY / offset pagination for SelectAll / SelectAllByField. Resolve order fields and,
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
    private static bool TryResolvePredicates(SourceProductionContext context, StoreMethodData method, EntityData entity, SqlBuilder sqlBuilder, out ResolvedPredicatePlan? plan, IReadOnlyList<ColumnData>? setColumns = null)
    {
        plan = null;
        var nonCancellationCount = method.Parameters.Count - 1;
        var predicates = new List<SqlPredicate>(method.Predicates.Count);
        var bindings = new List<PredicateBinding>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var paramIndex = 0;
        var hasIn = false;

        // For a set-based UPDATE the leading parameters supply the SET values (bound as
        // "@{PropertyName}"), so predicate binding starts after them and the SET property names are
        // pre-claimed: a column that is both assigned and filtered binds its filter parameter as
        // "@{PropertyName}2" via UniqueName, so SET and WHERE parameters can never collide.
        if (setColumns is not null)
        {
            paramIndex = setColumns.Count;
            foreach (var setColumn in setColumns)
            {
                usedNames.Add(setColumn.PropertyName);
            }
        }

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

            // A JSON-path criterion ([InquiryWhere.JsonPath]) filters inside a JSON text column: the field
            // must be a plain string column (no value converter — the comparison value binds as text, not
            // through the column's converter) and the path must be a well-formed dotted object path. The
            // strict path grammar keeps the value safe to embed in a single-quoted SQL literal across every
            // dialect (no quote/escape hazard) and uniformly translatable (no array indices that PostgreSQL
            // would mistranslate). The operator-specific parameter checks below then enforce string
            // parameters, since the column is string-typed.
            if (predicate.JsonPath is { } jsonPath &&
                (column.Type.NonNullableDisplayName != "string" || column.Converter is not null ||
                 !IsWellFormedJsonPath(jsonPath)))
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.JsonPathPredicateInvalid, method.Location?.ToLocation(), method.Name, predicate.Field));
                return false;
            }

            // Parameter names derive from the column property; a JSON-path criterion instead derives from
            // the path's leaf segment ("$.address.city" → "city") so the generated SQL binds @city rather
            // than @<column>, and two paths on the same column don't collide as @<column>/@<column>2.
            var paramBase = predicate.JsonPath is { } leafPath ? JsonPathParameterBase(leafPath, column.PropertyName) : column.PropertyName;

            switch (predicate.Op)
            {
                case SqlCompareOp.IsNull:
                case SqlCompareOp.IsNotNull:
                    predicates.Add(new SqlPredicate(column, predicate.Op, null, null, predicate.IsOr, predicate.JsonPath));
                    break;

                case SqlCompareOp.Between:
                {
                    if (!ParameterMatchesColumn(method.Parameters[paramIndex], column) ||
                        !ParameterMatchesColumn(method.Parameters[paramIndex + 1], column))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.PredicateParameterMismatch, method.Location?.ToLocation(), method.Name));
                        return false;
                    }

                    var lo = UniqueName(usedNames, paramBase + "_lo");
                    var hi = UniqueName(usedNames, paramBase + "_hi");
                    // SqlPredicate carries the bare logical name; SqlBuilder.RenderPredicate applies the
                    // dialect sigil (':' on Oracle, '@' elsewhere) when emitting the SQL. The runtime
                    // PredicateBinding keeps '@' — the binder is dialect-agnostic and FinalizeCommand
                    // reconciles the sigil on Oracle.
                    predicates.Add(new SqlPredicate(column, predicate.Op, lo, hi, predicate.IsOr, predicate.JsonPath));
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

                    var name = UniqueName(usedNames, paramBase);
                    predicates.Add(new SqlPredicate(column, predicate.Op, name, null, predicate.IsOr, predicate.JsonPath));
                    // Unlike scalar bindings (which keep the runtime binder's '@' form and let Oracle's
                    // FinalizeCommand reconcile the sigil), IN routes through InquiryInExpansion, which
                    // rewrites the command TEXT by locating the baked sentinel. That sentinel takes the
                    // dialect sigil (RenderIn → ParameterName), so the Expand name must match it exactly —
                    // ':name' on Oracle, '@name' elsewhere. FinalizeCommand only renames parameters, not
                    // the text, so it cannot bridge a mismatch here.
                    bindings.Add(new PredicateBinding(sqlBuilder.ParameterName(name), paramIndex, column, isCollection: true));
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

                    var name = UniqueName(usedNames, paramBase);
                    predicates.Add(new SqlPredicate(column, predicate.Op, name, null, predicate.IsOr, predicate.JsonPath));
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

                    var name = UniqueName(usedNames, paramBase);
                    predicates.Add(new SqlPredicate(column, predicate.Op, name, null, predicate.IsOr, predicate.JsonPath));
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

    /// <summary>
    /// Derives a readable parameter base name from a JSON path's leaf segment (e.g. <c>$.address.city</c>
    /// → <c>city</c>), keeping only identifier characters. Falls back to <paramref name="fallback"/> (the
    /// column property name) when the leaf yields no usable identifier.
    /// </summary>
    private static string JsonPathParameterBase(string jsonPath, string fallback)
    {
        var lastDot = jsonPath.LastIndexOf('.');
        var leaf = lastDot >= 0 ? jsonPath.Substring(lastDot + 1) : jsonPath;
        var sb = new System.Text.StringBuilder(leaf.Length);
        foreach (var ch in leaf)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                sb.Append(ch);
            }
        }

        var name = sb.ToString();
        // A parameter base must start with a letter or underscore (a digit-leading leaf falls back).
        return name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') ? name : fallback;
    }

    /// <summary>
    /// Validates an <c>[InquiryWhere.JsonPath]</c> value against the v1 grammar: a SQL/JSON dotted object
    /// path <c>$.a.b</c> with one or more segments, each a run of letters, digits, <c>_</c> or <c>-</c>.
    /// Rejecting everything else (quotes, brackets/array indices, empty or trailing segments, bare <c>$</c>)
    /// keeps the path safe to embed in a single-quoted SQL literal on every dialect and uniformly
    /// translatable to PostgreSQL's <c>#&gt;&gt;</c> text-path form. Array indices and quoted keys are out of
    /// v1 scope rather than silently mis-handled.
    /// </summary>
    private static bool IsWellFormedJsonPath(string path)
    {
        // Must start with "$." and carry at least one segment character after it.
        if (path.Length < 3 || path[0] != '$' || path[1] != '.')
        {
            return false;
        }

        var expectSegmentChar = true; // just consumed a '.', so a segment character must follow
        for (var i = 2; i < path.Length; i++)
        {
            var ch = path[i];
            if (ch == '.')
            {
                if (expectSegmentChar)
                {
                    return false; // empty segment ("..", trailing dot)
                }

                expectSegmentChar = true;
            }
            else if (IsJsonPathSegmentChar(ch))
            {
                expectSegmentChar = false;
            }
            else
            {
                return false; // quote, bracket, whitespace, etc.
            }
        }

        return !expectSegmentChar; // must not end on a dangling '.'
    }

    private static bool IsJsonPathSegmentChar(char ch)
        => (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-';

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
            StoreOperation.SelectAll or StoreOperation.SelectAllEager or StoreOperation.Count or StoreOperation.Aggregate => parameters.Count == 1,
            StoreOperation.FullTextSearch => parameters.Count == 2 && parameters[0].ComparisonDisplay == "string",
            StoreOperation.InsertAll or StoreOperation.BulkInsert or StoreOperation.UpdateAll => parameters.Count == 2 && IsEnumerableOfEntity(parameters[0], entity),
            // DeleteAll takes a collection of the single key's type; composite-key entities are unsupported.
            StoreOperation.DeleteAll => entity.Keys.Count == 1 && parameters.Count == 2 && IsEnumerableOfType(parameters[0], entity.Keys[0].Type.DisplayName),
            StoreOperation.SelectOneByKey or StoreOperation.SelectOneByKeyEager or StoreOperation.RestoreOneByKey =>
                MatchesPositionalColumns(method, nonCancellationCount, entity.Keys.AsImmutableArray()),
            // a concurrency-checked DELETE takes the whole entity (so the expected token value
            // binds, symmetric with UPDATE); a plain DELETE on a non-token entity stays key-positional.
            StoreOperation.DeleteOneByKey =>
                entity.ConcurrencyToken is not null
                    ? parameters.Count == 2 && parameters[0].ComparisonDisplay == entity.FullyQualifiedName
                    : MatchesPositionalColumns(method, nonCancellationCount, entity.Keys.AsImmutableArray()),
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

    /// <summary>
    /// Builds and emits a SQL const when <paramref name="needed"/>. Returns null when the const was
    /// emitted (supported) or not needed; returns the <see cref="System.NotSupportedException"/> message
    /// when the active dialect cannot emit the operation — the caller then degrades the affected methods
    /// to throwing stubs (INQ039) rather than aborting the whole generator.
    /// </summary>
    private static string? TryBuildDegradableConst(StringBuilder source, string fieldName, bool needed, System.Func<string> build)
    {
        if (!needed)
        {
            return null;
        }

        try
        {
            AppendConstSql(source, fieldName, build());
            return null;
        }
        catch (System.NotSupportedException ex)
        {
            return ex.Message;
        }
    }

    // Synthetic paging parameter LOGICAL names (no sigil). The SQL text takes the dialect sigil and any
    // dialect-specific safe-name transform via SqlBuilder.ParameterName; the generated paging binder emits
    // the matching '@__…' runtime parameter, which FinalizeCommand reconciles on Oracle.
    private const string OffsetLogicalName = "__offset";
    private const string LimitLogicalName = "__limit";
    private const string PageSizeLogicalName = "__pageSize";

    private static string KeysetCursorLogicalName(int index) => "__cursor" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
                cursorParams.Add(sqlBuilder.ParameterName(KeysetCursorLogicalName(i)));
            }

            // Keyset requests pageSize+1 rows via FETCH/LIMIT. SqlServer FETCH needs an OFFSET, which is
            // a literal 0 here (keyset never skips), so the same BuildPaginationClause serves all dialects.
            // Keyset terms all share one direction, so the first order column's direction is the keyset's.
            options = new SqlSelectOptions(
                orderTerms,
                offsetParameter: "0",
                limitParameter: sqlBuilder.ParameterName(PageSizeLogicalName),
                keysetColumns: keysetColumns,
                keysetCursorParameters: cursorParams,
                keysetDescending: plan.OrderColumns.Count > 0 && plan.OrderColumns[0].Descending);

            var keysetWhere = sqlBuilder.BuildKeysetPredicate(options);
            // keyset selects compose the active-row filter (soft-delete + global filters) onto the cursor
            // predicate (the keyset op has no IncludeDeleted opt-out). AppendWhere is internal to
            // SqlBuilder, so the same AND-composition is applied inline here against the precomputed fragment.
            if (ctx.ActiveRowPredicate.Length > 0)
            {
                keysetWhere += " AND " + ctx.ActiveRowPredicate;
            }
            baseSql = "SELECT " + ctx.SelectColumns + " FROM " + ctx.Table + " WHERE " + keysetWhere;
        }
        else
        {
            options = plan.Pagination == Pagination.Offset
                ? new SqlSelectOptions(orderTerms, offsetParameter: sqlBuilder.ParameterName(OffsetLogicalName), limitParameter: sqlBuilder.ParameterName(LimitLogicalName))
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

    /// <summary>
    /// The keyset <b>first-page</b> query (null cursor): the same ordered, page-sized SELECT as the seek
    /// query built by <see cref="BuildSelectPlanSql"/> but with no cursor predicate, so it returns from the
    /// start. Split out from the seek query because folding both into one <c>(@cursor IS NULL OR …)</c>
    /// predicate is non-sargable and defeats the index seek (see <see cref="SqlBuilder.BuildKeysetPredicate"/>),
    /// while binding <c>key &gt; NULL</c> on the seek query would match no rows.
    /// </summary>
    private static string BuildKeysetFirstPageSql(SqlBuilder sqlBuilder, SqlBuildContext ctx, ResolvedSelectPlan plan)
    {
        var orderTerms = new List<OrderByTerm>(plan.OrderColumns.Count);
        foreach (var (column, descending) in plan.OrderColumns)
        {
            orderTerms.Add(new OrderByTerm(sqlBuilder.QuoteIdentifier(column.ColumnName), descending));
        }

        // Same offset("0")/limit(@__pageSize) as the keyset seek query so the pagination tail matches.
        var options = new SqlSelectOptions(
            orderTerms,
            offsetParameter: "0",
            limitParameter: sqlBuilder.ParameterName(PageSizeLogicalName));

        // a soft-delete entity still filters deleted rows on the first page (no cursor predicate to AND with).
        var where = ctx.ActiveRowPredicate;
        var baseSql = "SELECT " + ctx.SelectColumns + " FROM " + ctx.Table
            + (where.Length > 0 ? " WHERE " + where : string.Empty);

        var orderByClause = sqlBuilder.BuildOrderByClause(options);
        var sql = orderByClause.Length > 0 ? baseSql + " " + orderByClause : baseSql;
        return sql + " " + sqlBuilder.BuildPaginationClause(options);
    }

    private static string StripGlobalPrefix(string fullyQualifiedName)
        => fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualifiedName.Substring("global::".Length)
            : fullyQualifiedName;
}
