using System.Globalization;
using System.Text;
using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators;

/// <summary>Emits allocation-neutral, compile-time provider-specific row materializers.</summary>
internal static class MaterializerEmitter
{
    public static void EmitMaterializeBody(StringBuilder source, EquatableArray<ColumnData> columns, string targetType, SqlBuilder sqlBuilder, string indent)
    {
        source.AppendLine($"{indent}return new {targetType}");
        source.AppendLine($"{indent}{{");
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            source.AppendLine($"{indent}    {column.PropertyName} = {ReadExpression(column.Type, i, sqlBuilder, column.EnumAsString, column.Converter)},");
        }
        source.AppendLine($"{indent}}};");
    }

    /// <summary>
    /// Emits the class materializer that DI registers for handwritten SQL. The pipeline binds it to each
    /// result set, which returns the name-mapped struct. The boxed struct is cached with its column map,
    /// so a result shape that repeats allocates nothing per read.
    /// </summary>
    public static void EmitClassMaterializer(StringBuilder source, string className, string nameMappedName, string targetType)
    {
        var materializer = $"global::Inquiry.Materialization.IInquiryEntityMaterializer<{targetType}>";
        var boundType = $"global::System.Tuple<global::Inquiry.Materialization.InquiryColumnMap, {materializer}>";
        source.AppendLine($"internal sealed class {className} : {materializer}, global::Inquiry.Materialization.IInquiryResultSetBinder<{targetType}, {materializer}>");
        source.AppendLine("{");
        source.AppendLine($"    private static {boundType}? s_bound;");
        source.AppendLine();
        source.AppendLine("    public bool IsInquirySequentialAccessSafe => true;");
        source.AppendLine();
        source.AppendLine($"    public {materializer} Bind(global::System.Data.Common.DbDataReader reader)");
        source.AppendLine("    {");
        source.AppendLine($"        var map = {nameMappedName}.Shape.Map(reader);");
        source.AppendLine("        var bound = s_bound;");
        source.AppendLine("        if (bound is null || !ReferenceEquals(bound.Item1, map))");
        source.AppendLine("        {");
        source.AppendLine($"            bound = new {boundType}(map, new {nameMappedName}(map));");
        source.AppendLine("            s_bound = bound;");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        return bound.Item2;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    public {targetType} Materialize(global::System.Data.Common.DbDataReader reader)");
        source.AppendLine($"        => default({nameMappedName}).Materialize(reader);");
        source.AppendLine("}");
    }

    /// <summary>
    /// Emits a struct materializer that maps result columns to properties by name. The pipeline binds it
    /// once per result set. An unbound instance maps the columns on each row, which keeps direct callers
    /// such as test doubles correct. Reads stay in ascending ordinal order, so the struct is safe under
    /// <c>SequentialAccess</c> for every column order.
    /// </summary>
    public static void EmitNameMappedMaterializer(StringBuilder source, string structName, EquatableArray<ColumnData> columns, string targetType, SqlBuilder sqlBuilder)
    {
        var properties = string.Join(", ", columns.AsImmutableArray().Select(static column => $"\"{GeneratorHelpers.Escape(column.PropertyName)}\""));
        var columnNames = string.Join(", ", columns.AsImmutableArray().Select(static column => $"\"{GeneratorHelpers.Escape(column.ColumnName)}\""));

        source.AppendLine($"internal readonly struct {structName} : global::Inquiry.Materialization.IInquiryEntityMaterializer<{targetType}>, global::Inquiry.Materialization.IInquiryResultSetBinder<{targetType}, {structName}>");
        source.AppendLine("{");
        source.AppendLine("    internal static readonly global::Inquiry.Materialization.InquiryResultShape Shape = new(");
        source.AppendLine($"        typeof({targetType}),");
        source.AppendLine($"        new string[] {{ {properties} }},");
        source.AppendLine($"        new string[] {{ {columnNames} }});");
        source.AppendLine();
        source.AppendLine("    private readonly global::Inquiry.Materialization.InquiryColumnMap? _map;");
        source.AppendLine();
        source.AppendLine($"    internal {structName}(global::Inquiry.Materialization.InquiryColumnMap map) => _map = map;");
        source.AppendLine();
        source.AppendLine("    public bool IsInquirySequentialAccessSafe => true;");
        source.AppendLine();
        source.AppendLine($"    public {structName} Bind(global::System.Data.Common.DbDataReader reader) => new(Shape.Map(reader));");
        source.AppendLine();
        source.AppendLine($"    public {targetType} Materialize(global::System.Data.Common.DbDataReader reader)");
        source.AppendLine("    {");
        source.AppendLine("        var map = _map ?? Shape.Map(reader);");
        for (var i = 0; i < columns.Count; i++)
        {
            var type = columns[i].Type;
            var localType = type.IsNullable ? type.NonNullableDisplayName + "?" : type.DisplayName;
            source.AppendLine($"        {localType} value{i} = default!;");
        }

        source.AppendLine("        var property = 0;");
        source.AppendLine("        try");
        source.AppendLine("        {");
        source.AppendLine("            if (map.IsInOrder)");
        source.AppendLine("            {");
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            if (i > 0)
            {
                source.AppendLine($"                property = {i};");
            }

            source.AppendLine($"                value{i} = {ReadExpression(column.Type, i, sqlBuilder, column.EnumAsString, column.Converter)};");
        }

        source.AppendLine("            }");
        source.AppendLine("            else");
        source.AppendLine("            {");
        source.AppendLine("                var ordinals = map.Ordinals;");
        source.AppendLine("                foreach (var next in map.ReadOrder)");
        source.AppendLine("                {");
        source.AppendLine("                    property = next;");
        source.AppendLine("                    switch (next)");
        source.AppendLine("                    {");
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var ordinal = $"ordinals[{i.ToString(CultureInfo.InvariantCulture)}]";
            source.AppendLine($"                        case {i}:");
            source.AppendLine($"                            value{i} = {ReadExpression(column.Type, ordinal, sqlBuilder, column.EnumAsString, column.Converter, ReaderResultRole.Column)};");
            source.AppendLine("                            break;");
        }

        source.AppendLine("                    }");
        source.AppendLine("                }");
        source.AppendLine("            }");
        source.AppendLine("        }");
        source.AppendLine("        catch (global::System.Exception exception) when (global::Inquiry.Materialization.InquiryColumnMap.IsReadFailure(exception))");
        source.AppendLine("        {");
        source.AppendLine("            throw map.ReadFailed(reader, property, exception);");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine($"        return new {targetType}");
        source.AppendLine("        {");
        for (var i = 0; i < columns.Count; i++)
        {
            source.AppendLine($"            {columns[i].PropertyName} = value{i},");
        }

        source.AppendLine("        };");
        source.AppendLine("    }");
        source.AppendLine("}");
    }

    public static string ReadExpression(TypeData type, int index, SqlBuilder sqlBuilder, bool enumAsString = false, ConverterData? converter = null, ReaderResultRole role = ReaderResultRole.Column)
        => ReadExpression(type, index.ToString(CultureInfo.InvariantCulture), sqlBuilder, enumAsString, converter, role);

    private static string ReadExpression(TypeData type, string index, SqlBuilder sqlBuilder, bool enumAsString, ConverterData? converter, ReaderResultRole role)
    {
        var nonNullable = type.NonNullableDisplayName;
        string read;
        if (converter is not null)
        {
            read = ConverterInvocationEmitter.FromProvider(converter, PlainReadExpression(converter.ProviderType, converter.ProviderSpecialType, index, nonNullable, converter.ProviderTypeDisplay, sqlBuilder, role));
        }
        else if (enumAsString)
        {
            read = $"global::System.Enum.Parse<{nonNullable}>({sqlBuilder.BuildReaderExpression(new ReaderExpressionContext(index, nonNullable, "global::System.String", SpecialType.System_String, role))})";
        }
        else
        {
            read = type.IsEnum
                ? EnumReadExpression(type.EnumUnderlyingSpecialType, nonNullable, index, sqlBuilder, role)
                : PlainReadExpression(type, type.SpecialType, index, nonNullable, nonNullable, sqlBuilder, role);
        }

        if (!type.IsNullable)
        {
            return read;
        }

        return type.IsValueType
            ? $"reader.IsDBNull({index}) ? ({type.DisplayName})null : {read}"
            : $"reader.IsDBNull({index}) ? null : {read}";
    }

    private static string EnumReadExpression(SpecialType underlying, string enumTypeName, string index, SqlBuilder sqlBuilder, ReaderResultRole role)
    {
        var storageType = StorageSpecialType(underlying);
        var call = sqlBuilder.BuildReaderExpression(new ReaderExpressionContext(index, enumTypeName, StorageTypeName(storageType), storageType, role));
        return $"unchecked(({enumTypeName}){call})";
    }

    private static string PlainReadExpression(TypeData? type, SpecialType specialType, string index, string logicalTypeName, string providerTypeName, SqlBuilder sqlBuilder, ReaderResultRole role)
    {
        var primitive = sqlBuilder.BuildReaderExpression(new ReaderExpressionContext(
            index,
            logicalTypeName,
            providerTypeName,
            StorageSpecialType(specialType),
            role,
            ProviderIsGuid: type?.IsGuid == true,
            ProviderIsByteArray: type?.IsByteArray == true,
            ProviderIsDateOnly: type?.IsDateOnly == true,
            ProviderIsTimeOnly: type?.IsTimeOnly == true));

        return specialType switch
        {
            SpecialType.System_SByte => $"unchecked((sbyte){primitive})",
            SpecialType.System_UInt16 => $"unchecked((ushort){primitive})",
            SpecialType.System_UInt32 => $"unchecked((uint){primitive})",
            SpecialType.System_UInt64 => $"unchecked((ulong){primitive})",
            _ => primitive,
        };
    }

    private static SpecialType StorageSpecialType(SpecialType type) => type switch
    {
        SpecialType.System_SByte => SpecialType.System_Byte,
        SpecialType.System_UInt16 => SpecialType.System_Int16,
        SpecialType.System_UInt32 => SpecialType.System_Int32,
        SpecialType.System_UInt64 => SpecialType.System_Int64,
        _ => type,
    };

    private static string StorageTypeName(SpecialType type) => type switch
    {
        SpecialType.System_Byte => "global::System.Byte",
        SpecialType.System_Int16 => "global::System.Int16",
        SpecialType.System_Int32 => "global::System.Int32",
        SpecialType.System_Int64 => "global::System.Int64",
        _ => "global::System.Int32",
    };
}
