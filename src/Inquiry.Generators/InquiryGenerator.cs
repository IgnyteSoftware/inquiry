using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Inquiry.Generators.Sql;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Inquiry.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class InquiryGenerator : ISourceGenerator
{
    private const string AttributeNamespace = "Inquiry";

    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor EntityKeyCount = new(
        "INQ001",
        "Entity must have exactly one InquiryKey property",
        "Entity '{0}' must have exactly one InquiryKey property.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateColumn = new(
        "INQ002",
        "Entity contains duplicate mapped column names",
        "Entity '{0}' maps multiple properties to column '{1}'.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedPropertyType = new(
        "INQ003",
        "Entity property type is not supported",
        "Entity property '{0}.{1}' has unsupported type '{2}'.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor StoreMustBePartial = new(
        "INQ004",
        "Store class must be partial",
        "Store class '{0}' must be partial.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedReturnType = new(
        "INQ005",
        "Query method return type is not supported",
        "Query method '{0}' has unsupported return type '{1}'.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidParameters = new(
        "INQ006",
        "Query method parameter list is invalid",
        "Query method '{0}' has an invalid parameter list.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnknownField = new(
        "INQ007",
        "SelectByField references an unmapped property or column",
        "Query method '{0}' references unmapped field '{1}'.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor StoreEntityNotMapped = new(
        "INQ008",
        "Store entity type is not mapped with InquiryTable",
        "Store class '{0}' uses entity '{1}', which is not mapped with InquiryTable.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MethodMustBeAbstract = new(
        "INQ010",
        "Query method must be abstract",
        "Query method '{0}' must be abstract.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(static () => new SyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not SyntaxReceiver receiver)
        {
            return;
        }

        var entities = DiscoverEntities(context, receiver.CandidateClasses);
        var entityRegistrations = ImmutableArray.CreateBuilder<EntityRegistrationModel>();
        foreach (var entity in entities.Values)
        {
            entityRegistrations.Add(GenerateEntityMetadata(context, entity));
        }

        var storeRegistrations = ImmutableArray.CreateBuilder<StoreRegistrationModel>();
        foreach (var classDeclaration in receiver.CandidateClasses)
        {
            if (context.Compilation.GetSemanticModel(classDeclaration.SyntaxTree).GetDeclaredSymbol(classDeclaration, context.CancellationToken) is not INamedTypeSymbol storeSymbol)
            {
                continue;
            }

            if (!TryGetStoreEntityType(storeSymbol, out var entityType))
            {
                continue;
            }

            if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                context.ReportDiagnostic(Diagnostic.Create(StoreMustBePartial, classDeclaration.Identifier.GetLocation(), storeSymbol.Name));
                continue;
            }

            var entityKey = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!entities.TryGetValue(entityKey, out var entity))
            {
                context.ReportDiagnostic(Diagnostic.Create(StoreEntityNotMapped, classDeclaration.Identifier.GetLocation(), storeSymbol.Name, entityType.ToDisplayString()));
                continue;
            }

            var methods = DiscoverStoreMethods(context, storeSymbol, entity);
            if (methods.Length == 0)
            {
                continue;
            }

            var registration = GenerateStore(context, storeSymbol, entity, methods);
            storeRegistrations.Add(registration);
        }

        if (storeRegistrations.Count > 0 || entityRegistrations.Count > 0)
        {
            GenerateServiceRegistration(context, entityRegistrations.ToImmutable(), storeRegistrations.ToImmutable());
        }
    }

    private static Dictionary<string, EntityModel> DiscoverEntities(GeneratorExecutionContext context, IReadOnlyList<ClassDeclarationSyntax> candidates)
    {
        var entities = new Dictionary<string, EntityModel>();

        foreach (var classDeclaration in candidates)
        {
            var model = context.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);
            if (model.GetDeclaredSymbol(classDeclaration, context.CancellationToken) is not INamedTypeSymbol entitySymbol)
            {
                continue;
            }

            var tableAttribute = GetAttribute(entitySymbol, "InquiryTableAttribute");
            if (tableAttribute is null)
            {
                continue;
            }

            var tableName = GetConstructorString(tableAttribute) ?? entitySymbol.Name;
            var schema = GetNamedString(tableAttribute, "Schema");
            var columns = new List<ColumnModel>();

            foreach (var property in entitySymbol.GetMembers().OfType<IPropertySymbol>())
            {
                var keyAttribute = GetAttribute(property, "InquiryKeyAttribute");
                var columnAttribute = keyAttribute ?? GetAttribute(property, "InquiryColumnAttribute") ?? GetAttribute(property, "InquiryForeignKeyAttribute");
                if (columnAttribute is null)
                {
                    continue;
                }

                var columnName = GetConstructorString(columnAttribute) ?? property.Name;
                var typeInfo = TypeInfo.Create(property.Type, property.NullableAnnotation);
                var column = new ColumnModel(property, property.Name, columnName, typeInfo, keyAttribute is not null);
                columns.Add(column);

                if (!typeInfo.IsSupported)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedPropertyType,
                        property.Locations.FirstOrDefault(),
                        entitySymbol.Name,
                        property.Name,
                        property.Type.ToDisplayString()));
                }

                if (property.SetMethod is null || property.SetMethod.DeclaredAccessibility == Accessibility.Private)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedPropertyType,
                        property.Locations.FirstOrDefault(),
                        entitySymbol.Name,
                        property.Name,
                        property.Type.ToDisplayString()));
                }
            }

            var keyColumns = columns.Where(static c => c.IsKey).ToArray();
            if (keyColumns.Length != 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(EntityKeyCount, classDeclaration.Identifier.GetLocation(), entitySymbol.Name));
                continue;
            }

            foreach (var duplicate in columns.GroupBy(static c => c.ColumnName, StringComparer.OrdinalIgnoreCase).Where(static g => g.Count() > 1))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateColumn, classDeclaration.Identifier.GetLocation(), entitySymbol.Name, duplicate.Key));
            }

            var entity = new EntityModel(entitySymbol, tableName, schema, columns, keyColumns[0]);
            entities[entitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = entity;
        }

        return entities;
    }

    private static ImmutableArray<StoreMethodModel> DiscoverStoreMethods(GeneratorExecutionContext context, INamedTypeSymbol storeSymbol, EntityModel entity)
    {
        var methods = ImmutableArray.CreateBuilder<StoreMethodModel>();

        foreach (var method in storeSymbol.GetMembers().OfType<IMethodSymbol>().Where(static m => m.MethodKind == MethodKind.Ordinary))
        {
            var operation = GetOperation(method, out var operationAttribute);
            if (operation == StoreOperation.None)
            {
                continue;
            }

            if (!method.IsAbstract)
            {
                context.ReportDiagnostic(Diagnostic.Create(MethodMustBeAbstract, method.Locations.FirstOrDefault(), method.Name));
                continue;
            }

            var selectedField = operation == StoreOperation.SelectByField ? GetConstructorString(operationAttribute!) : null;
            var fieldColumn = selectedField is null ? null : entity.Columns.FirstOrDefault(c => string.Equals(c.PropertyName, selectedField, StringComparison.OrdinalIgnoreCase) || string.Equals(c.ColumnName, selectedField, StringComparison.OrdinalIgnoreCase));
            if (operation == StoreOperation.SelectByField && fieldColumn is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(UnknownField, method.Locations.FirstOrDefault(), method.Name, selectedField));
                continue;
            }

            if (!IsSupportedReturnType(method.ReturnType, operation, entity))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedReturnType, method.Locations.FirstOrDefault(), method.Name, method.ReturnType.ToDisplayString()));
                continue;
            }

            if (!HasSupportedParameters(method, operation, entity, fieldColumn))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidParameters, method.Locations.FirstOrDefault(), method.Name));
                continue;
            }

            methods.Add(new StoreMethodModel(method, operation, fieldColumn));
        }

        return methods.ToImmutable();
    }

    private static bool IsSupportedReturnType(ITypeSymbol returnType, StoreOperation operation, EntityModel entity)
    {
        return operation switch
        {
            StoreOperation.SelectAll or StoreOperation.SelectByField => IsGenericType(returnType, "System.Collections.Generic.IAsyncEnumerable<T>", entity.Symbol),
            StoreOperation.SelectByKey => IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", entity.Symbol),
            StoreOperation.Insert => IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Int32),
            StoreOperation.Update or StoreOperation.DeleteByKey => IsGenericType(returnType, "System.Threading.Tasks.Task<TResult>", SpecialType.System_Boolean),
            _ => false,
        };
    }

    private static bool HasSupportedParameters(IMethodSymbol method, StoreOperation operation, EntityModel entity, ColumnModel? fieldColumn)
    {
        var parameters = method.Parameters;
        if (parameters.Length == 0)
        {
            return false;
        }

        var last = parameters[parameters.Length - 1];
        if (!IsCancellationToken(last.Type))
        {
            return false;
        }

        return operation switch
        {
            StoreOperation.SelectAll => parameters.Length == 1,
            StoreOperation.SelectByKey => parameters.Length == 2 && SymbolEqualityComparer.Default.Equals(parameters[0].Type, entity.Key.Type.Symbol),
            StoreOperation.SelectByField => parameters.Length == 2 && fieldColumn is not null && SymbolEqualityComparer.Default.Equals(parameters[0].Type, fieldColumn.Type.Symbol),
            StoreOperation.Insert or StoreOperation.Update => parameters.Length == 2 && SymbolEqualityComparer.Default.Equals(parameters[0].Type, entity.Symbol),
            StoreOperation.DeleteByKey => parameters.Length == 2 && SymbolEqualityComparer.Default.Equals(parameters[0].Type, entity.Key.Type.Symbol),
            _ => false,
        };
    }

    private static EntityRegistrationModel GenerateEntityMetadata(GeneratorExecutionContext context, EntityModel entity)
    {
        var materializerName = $"{entity.Symbol.Name}InquiryEntityMaterializer";
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        AppendNamespaceStart(source, entity.Symbol);
        source.AppendLine($"internal static class {entity.Symbol.Name}InquiryEntityMetadata");
        source.AppendLine("{");
        source.AppendLine($"    public const string TableName = \"{Escape(entity.TableName)}\";");
        if (entity.Schema is not null)
        {
            source.AppendLine($"    public const string Schema = \"{Escape(entity.Schema)}\";");
        }

        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine($"internal sealed class {materializerName} : global::Inquiry.IInquiryEntityMaterializer<{entity.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>");
        source.AppendLine("{");
        source.AppendLine($"    public {entity.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} Materialize(global::System.Data.Common.DbDataReader reader)");
        source.AppendLine("    {");
        source.AppendLine($"        return new {entity.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
        source.AppendLine("        {");
        for (var i = 0; i < entity.Columns.Count; i++)
        {
            var column = entity.Columns[i];
            source.AppendLine($"            {column.PropertyName} = {ReadExpression(column.Type, i)},");
        }

        source.AppendLine("        };");
        source.AppendLine("    }");
        source.AppendLine("}");
        AppendNamespaceEnd(source, entity.Symbol);

        context.AddSource($"{entity.Symbol.Name}.InquiryEntity.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        return new EntityRegistrationModel(entity.Symbol, materializerName);
    }

    private static StoreRegistrationModel GenerateStore(GeneratorExecutionContext context, INamedTypeSymbol storeSymbol, EntityModel entity, ImmutableArray<StoreMethodModel> methods)
    {
        var generatedName = $"Generated{storeSymbol.Name}";
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        AppendNamespaceStart(source, storeSymbol);
        source.AppendLine($"{GetAccessibility(storeSymbol.DeclaredAccessibility)} sealed class {generatedName} : {storeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
        source.AppendLine("{");
        source.AppendLine($"    public {generatedName}(global::Inquiry.IInquiry inquiry)");
        source.AppendLine("        : base(inquiry)");
        source.AppendLine("    {");
        source.AppendLine("    }");
        source.AppendLine();

        var sqlStatements = new SqlStatementBuilder(SqlServerSqlDialect.Instance)
            .Build(entity.Schema, entity.TableName, entity.Columns.Select(ToSqlColumn).ToArray());
        source.AppendLine($"    private const string SelectAllSql = \"{Escape(sqlStatements.SelectAll)}\";");
        source.AppendLine($"    private const string SelectByKeySql = \"{Escape(sqlStatements.SelectByKey)}\";");
        source.AppendLine($"    private const string DeleteByKeySql = \"{Escape(sqlStatements.DeleteByKey)}\";");
        source.AppendLine($"    private const string InsertSql = \"{Escape(sqlStatements.Insert)}\";");
        source.AppendLine($"    private const string UpdateSql = \"{Escape(sqlStatements.Update)}\";");
        source.AppendLine();

        foreach (var method in methods)
        {
            GenerateMethod(source, method, entity, sqlStatements);
            source.AppendLine();
        }

        GenerateParameterHelper(source);
        source.AppendLine("}");
        AppendNamespaceEnd(source, storeSymbol);

        context.AddSource($"{storeSymbol.Name}.InquiryStore.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        return new StoreRegistrationModel(storeSymbol, generatedName);
    }

    private static void GenerateServiceRegistration(
        GeneratorExecutionContext context,
        ImmutableArray<EntityRegistrationModel> entityRegistrations,
        ImmutableArray<StoreRegistrationModel> storeRegistrations)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        source.AppendLine("namespace Inquiry.Generated");
        source.AppendLine("{");
        source.AppendLine("    internal sealed class InquiryGeneratedServiceRegistration : global::Inquiry.IInquiryServiceRegistration");
        source.AppendLine("    {");
        source.AppendLine("        public void AddServices(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        source.AppendLine("        {");
        source.AppendLine("            if (services is null)");
        source.AppendLine("            {");
        source.AppendLine("                throw new global::System.ArgumentNullException(nameof(services));");
        source.AppendLine("            }");
        source.AppendLine();

        foreach (var registration in entityRegistrations)
        {
            source.AppendLine(
                $"            global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<global::Inquiry.IInquiryEntityMaterializer<{registration.EntityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>, {GetGeneratedEntityMaterializerTypeName(registration)}>(services);");
        }

        foreach (var registration in storeRegistrations)
        {
            source.AppendLine(
                $"            global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddTransient<{registration.StoreType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {GetGeneratedTypeName(registration)}>(services);");
        }

        source.AppendLine();
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("}");

        context.AddSource("InquiryGeneratedServiceRegistration.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static void GenerateMethod(StringBuilder source, StoreMethodModel method, EntityModel entity, SqlStatementSet sqlStatements)
    {
        var returnType = method.Symbol.ReturnType.ToDisplayString(FullyQualifiedNullableFormat);
        var methodName = method.Symbol.Name;
        var parameters = GetParameterDeclaration(method.Symbol, asyncIterator: false);
        var cancellation = method.Symbol.Parameters[method.Symbol.Parameters.Length - 1].Name;
        var entityParameter = method.Symbol.Parameters.Length > 1 ? method.Symbol.Parameters[0].Name : "entity";

        switch (method.Operation)
        {
            case StoreOperation.SelectAll:
                source.AppendLine($"    public override {returnType} {methodName}({parameters})");
                source.AppendLine("    {");
                source.AppendLine($"        return _inquiry.QueryAsync<{entity.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>(new global::Inquiry.InquiryCommandDefinition(SelectAllSql), {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectByField:
                var selectByFieldSql = sqlStatements.SelectByField(ToSqlColumn(method.FieldColumn!));
                source.AppendLine($"    public override {returnType} {methodName}({parameters})");
                source.AppendLine("    {");
                source.AppendLine($"        return _inquiry.QueryAsync<{entity.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>(");
                source.AppendLine($"            new global::Inquiry.InquiryCommandDefinition(\"{Escape(selectByFieldSql)}\", command => AddParameter(command, \"@value\", {entityParameter})),");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.SelectByKey:
                source.AppendLine($"    public override async {returnType} {methodName}({parameters})");
                source.AppendLine("    {");
                source.AppendLine($"        return await _inquiry.QuerySingleOrDefaultAsync<{entity.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>(");
                source.AppendLine($"            new global::Inquiry.InquiryCommandDefinition(SelectByKeySql, command => AddParameter(command, \"@key\", {entityParameter})),");
                source.AppendLine($"            {cancellation}).ConfigureAwait(false);");
                source.AppendLine("    }");
                break;

            case StoreOperation.Insert:
                source.AppendLine($"    public override {returnType} {methodName}({parameters})");
                source.AppendLine("    {");
                source.AppendLine("        return _inquiry.ExecuteAsync(");
                source.AppendLine("            new global::Inquiry.InquiryCommandDefinition(InsertSql, command =>");
                source.AppendLine("            {");
                foreach (var column in entity.Columns)
                {
                    source.AppendLine($"                AddParameter(command, \"@{column.PropertyName}\", {entityParameter}.{column.PropertyName});");
                }
                source.AppendLine("            }),");
                source.AppendLine($"            {cancellation});");
                source.AppendLine("    }");
                break;

            case StoreOperation.Update:
                source.AppendLine($"    public override async {returnType} {methodName}({parameters})");
                source.AppendLine("    {");
                source.AppendLine("        return await _inquiry.ExecuteAsync(");
                source.AppendLine("            new global::Inquiry.InquiryCommandDefinition(UpdateSql, command =>");
                source.AppendLine("            {");
                foreach (var column in entity.Columns)
                {
                    source.AppendLine($"                AddParameter(command, \"@{column.PropertyName}\", {entityParameter}.{column.PropertyName});");
                }
                source.AppendLine("            }),");
                source.AppendLine($"            {cancellation}).ConfigureAwait(false) > 0;");
                source.AppendLine("    }");
                break;

            case StoreOperation.DeleteByKey:
                source.AppendLine($"    public override async {returnType} {methodName}({parameters})");
                source.AppendLine("    {");
                source.AppendLine("        return await _inquiry.ExecuteAsync(");
                source.AppendLine($"            new global::Inquiry.InquiryCommandDefinition(DeleteByKeySql, command => AddParameter(command, \"@key\", {entityParameter})),");
                source.AppendLine($"            {cancellation}).ConfigureAwait(false) > 0;");
                source.AppendLine("    }");
                break;
        }
    }

    private static void GenerateParameterHelper(StringBuilder source)
    {
        source.AppendLine("    private static void AddParameter(global::System.Data.Common.DbCommand command, string name, object? value)");
        source.AppendLine("    {");
        source.AppendLine("        var parameter = command.CreateParameter();");
        source.AppendLine("        parameter.ParameterName = name;");
        source.AppendLine("        parameter.Value = value ?? global::System.DBNull.Value;");
        source.AppendLine("        command.Parameters.Add(parameter);");
        source.AppendLine("    }");
    }

    private static string ReadExpression(TypeInfo type, int index)
    {
        var nonNullable = type.NonNullableDisplayName;
        var read = type.SpecialType switch
        {
            SpecialType.System_String => $"reader.GetString({index})",
            SpecialType.System_Boolean => $"reader.GetBoolean({index})",
            SpecialType.System_Int16 => $"reader.GetInt16({index})",
            SpecialType.System_Int32 => $"reader.GetInt32({index})",
            SpecialType.System_Int64 => $"reader.GetInt64({index})",
            SpecialType.System_Single => $"reader.GetFloat({index})",
            SpecialType.System_Double => $"reader.GetDouble({index})",
            SpecialType.System_Decimal => $"reader.GetDecimal({index})",
            SpecialType.System_DateTime => $"reader.GetDateTime({index})",
            _ when type.IsGuid => $"reader.GetGuid({index})",
            _ => $"reader.GetFieldValue<{nonNullable}>({index})",
        };

        if (!type.IsNullable)
        {
            return read;
        }

        if (type.Symbol.IsValueType)
        {
            return $"reader.IsDBNull({index}) ? ({type.DisplayName})null : {read}";
        }

        return $"reader.IsDBNull({index}) ? null : {read}";
    }

    private static string GetParameterDeclaration(IMethodSymbol method, bool asyncIterator)
    {
        var parts = new List<string>();
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            var declaration = $"{parameter.Type.ToDisplayString(FullyQualifiedNullableFormat)} {parameter.Name}";
            if (asyncIterator && i == method.Parameters.Length - 1 && IsCancellationToken(parameter.Type))
            {
                declaration = $"[EnumeratorCancellation] {declaration}";
            }

            if (i == method.Parameters.Length - 1 && IsCancellationToken(parameter.Type))
            {
                declaration += " = default";
            }

            parts.Add(declaration);
        }

        return string.Join(", ", parts);
    }

    private static void AppendNamespaceStart(StringBuilder source, INamedTypeSymbol symbol)
    {
        if (!symbol.ContainingNamespace.IsGlobalNamespace)
        {
            source.AppendLine($"namespace {symbol.ContainingNamespace.ToDisplayString()}");
            source.AppendLine("{");
        }
    }

    private static void AppendNamespaceEnd(StringBuilder source, INamedTypeSymbol symbol)
    {
        if (!symbol.ContainingNamespace.IsGlobalNamespace)
        {
            source.AppendLine("}");
        }
    }

    private static StoreOperation GetOperation(IMethodSymbol method, out AttributeData? attribute)
    {
        foreach (var candidate in method.GetAttributes())
        {
            var name = candidate.AttributeClass?.Name;
            switch (name)
            {
                case "InquirySelectAttribute":
                    attribute = candidate;
                    return StoreOperation.SelectAll;
                case "InquirySelectByKeyAttribute":
                    attribute = candidate;
                    return StoreOperation.SelectByKey;
                case "InquirySelectByFieldAttribute":
                    attribute = candidate;
                    return StoreOperation.SelectByField;
                case "InquiryInsertAttribute":
                    attribute = candidate;
                    return StoreOperation.Insert;
                case "InquiryUpdateAttribute":
                    attribute = candidate;
                    return StoreOperation.Update;
                case "InquiryDeleteByKeyAttribute":
                    attribute = candidate;
                    return StoreOperation.DeleteByKey;
            }
        }

        attribute = null;
        return StoreOperation.None;
    }

    private static bool TryGetStoreEntityType(INamedTypeSymbol symbol, out ITypeSymbol entityType)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { IsGenericType: true } &&
                current.ContainingNamespace.ToDisplayString() == AttributeNamespace &&
                current.Name == "InquiryStore" &&
                current.TypeArguments.Length is 1 or 2)
            {
                entityType = current.TypeArguments[0];
                return true;
            }
        }

        entityType = symbol;
        return false;
    }

    private static bool IsGenericType(ITypeSymbol type, string metadataName, ITypeSymbol typeArgument)
    {
        return type is INamedTypeSymbol named &&
            named.TypeArguments.Length == 1 &&
            named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).TrimStart("global::".ToCharArray()) == metadataName &&
            SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], typeArgument);
    }

    private static bool IsGenericType(ITypeSymbol type, string metadataName, SpecialType specialType)
    {
        return type is INamedTypeSymbol named &&
            named.TypeArguments.Length == 1 &&
            named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).TrimStart("global::".ToCharArray()) == metadataName &&
            named.TypeArguments[0].SpecialType == specialType;
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Threading.CancellationToken";
    }

    private static AttributeData? GetAttribute(ISymbol symbol, string shortName)
    {
        return symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == shortName &&
            a.AttributeClass.ContainingNamespace.ToDisplayString() == AttributeNamespace);
    }

    private static string? GetConstructorString(AttributeData attribute)
    {
        return attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
    }

    private static string? GetNamedString(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name)
            {
                return argument.Value.Value as string;
            }
        }

        return null;
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static SqlColumn ToSqlColumn(ColumnModel column)
    {
        return new SqlColumn(column.PropertyName, column.ColumnName, column.IsKey);
    }

    private static string GetAccessibility(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            _ => "internal",
        };
    }

    private static string GetGeneratedTypeName(StoreRegistrationModel registration)
    {
        if (registration.StoreType.ContainingNamespace.IsGlobalNamespace)
        {
            return "global::" + registration.GeneratedTypeName;
        }

        return "global::" + registration.StoreType.ContainingNamespace.ToDisplayString() + "." + registration.GeneratedTypeName;
    }

    private static string GetGeneratedEntityMaterializerTypeName(EntityRegistrationModel registration)
    {
        if (registration.EntityType.ContainingNamespace.IsGlobalNamespace)
        {
            return "global::" + registration.MaterializerTypeName;
        }

        return "global::" + registration.EntityType.ContainingNamespace.ToDisplayString() + "." + registration.MaterializerTypeName;
    }

    private sealed class SyntaxReceiver : ISyntaxReceiver
    {
        public List<ClassDeclarationSyntax> CandidateClasses { get; } = new();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is ClassDeclarationSyntax classDeclaration &&
                (classDeclaration.AttributeLists.Count > 0 || classDeclaration.BaseList is not null))
            {
                CandidateClasses.Add(classDeclaration);
            }
        }
    }

    private sealed class EntityModel
    {
        public EntityModel(INamedTypeSymbol symbol, string tableName, string? schema, List<ColumnModel> columns, ColumnModel key)
        {
            Symbol = symbol;
            TableName = tableName;
            Schema = schema;
            Columns = columns;
            Key = key;
        }

        public INamedTypeSymbol Symbol { get; }

        public string TableName { get; }

        public string? Schema { get; }

        public List<ColumnModel> Columns { get; }

        public ColumnModel Key { get; }
    }

    private sealed class ColumnModel
    {
        public ColumnModel(IPropertySymbol symbol, string propertyName, string columnName, TypeInfo type, bool isKey)
        {
            Symbol = symbol;
            PropertyName = propertyName;
            ColumnName = columnName;
            Type = type;
            IsKey = isKey;
        }

        public IPropertySymbol Symbol { get; }

        public string PropertyName { get; }

        public string ColumnName { get; }

        public TypeInfo Type { get; }

        public bool IsKey { get; }
    }

    private sealed class TypeInfo
    {
        private TypeInfo(ITypeSymbol symbol, SpecialType specialType, bool isNullable, bool isGuid, bool isDateTimeOffset, bool isByteArray)
        {
            Symbol = symbol;
            SpecialType = specialType;
            IsNullable = isNullable;
            IsGuid = isGuid;
            IsDateTimeOffset = isDateTimeOffset;
            IsByteArray = isByteArray;
            IsSupported = IsSupportedType(specialType, isGuid, isDateTimeOffset, isByteArray);
            DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            NonNullableDisplayName = GetNonNullableSymbol(symbol).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        public ITypeSymbol Symbol { get; }

        public SpecialType SpecialType { get; }

        public bool IsNullable { get; }

        public bool IsGuid { get; }

        public bool IsDateTimeOffset { get; }

        public bool IsByteArray { get; }

        public bool IsSupported { get; }

        public string DisplayName { get; }

        public string NonNullableDisplayName { get; }

        public static TypeInfo Create(ITypeSymbol symbol, NullableAnnotation nullableAnnotation)
        {
            var nonNullable = GetNonNullableSymbol(symbol);
            return new TypeInfo(
                symbol,
                nonNullable.SpecialType,
                DetermineIsNullable(symbol, nullableAnnotation),
                nonNullable.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid",
                nonNullable.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.DateTimeOffset",
                nonNullable is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte });
        }

        private static bool DetermineIsNullable(ITypeSymbol symbol, NullableAnnotation nullableAnnotation)
        {
            return nullableAnnotation == NullableAnnotation.Annotated ||
                symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        }

        private static ITypeSymbol GetNonNullableSymbol(ITypeSymbol symbol)
        {
            if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named)
            {
                return named.TypeArguments[0];
            }

            return symbol;
        }

        private static bool IsSupportedType(SpecialType specialType, bool isGuid, bool isDateTimeOffset, bool isByteArray)
        {
            return specialType is SpecialType.System_String
                or SpecialType.System_Int16
                or SpecialType.System_Int32
                or SpecialType.System_Int64
                or SpecialType.System_Boolean
                or SpecialType.System_Decimal
                or SpecialType.System_Double
                or SpecialType.System_Single
                or SpecialType.System_DateTime
                || isGuid
                || isDateTimeOffset
                || isByteArray;
        }
    }

    private sealed class StoreMethodModel
    {
        public StoreMethodModel(IMethodSymbol symbol, StoreOperation operation, ColumnModel? fieldColumn)
        {
            Symbol = symbol;
            Operation = operation;
            FieldColumn = fieldColumn;
        }

        public IMethodSymbol Symbol { get; }

        public StoreOperation Operation { get; }

        public ColumnModel? FieldColumn { get; }
    }

    private sealed class StoreRegistrationModel
    {
        public StoreRegistrationModel(INamedTypeSymbol storeType, string generatedTypeName)
        {
            StoreType = storeType;
            GeneratedTypeName = generatedTypeName;
        }

        public INamedTypeSymbol StoreType { get; }

        public string GeneratedTypeName { get; }
    }

    private sealed class EntityRegistrationModel
    {
        public EntityRegistrationModel(INamedTypeSymbol entityType, string materializerTypeName)
        {
            EntityType = entityType;
            MaterializerTypeName = materializerTypeName;
        }

        public INamedTypeSymbol EntityType { get; }

        public string MaterializerTypeName { get; }
    }

    private enum StoreOperation
    {
        None,
        SelectAll,
        SelectByKey,
        SelectByField,
        Insert,
        Update,
        DeleteByKey,
    }
}
