using Inquiry.Generators.Abstractions;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Inquiry.SqlServer.Analyzer;

internal static class SqlServerTvpResolver
{
    private static readonly DiagnosticDescriptor InvalidMapping = new(
        "INQ076", "SQL Server TVP collection mapping is invalid",
        "Collection method '{0}' cannot use the SQL Server TVP mapping for column '{1}': {2}",
        "Inquiry", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly Regex TypePattern = new(
        @"^\s*(?<name>[A-Za-z][A-Za-z0-9_]*)(?:\s*\(\s*(?<a>MAX|[0-9]+)\s*(?:,\s*(?<b>[0-9]+)\s*)?\))?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static CollectionParameterResolution Resolve(SqlServerSqlBuilder builder, CollectionParameterContext context)
    {
        var column = context.Column;
        PhysicalType? physical;
        if (column.SqlType is not null)
        {
            if (string.IsNullOrWhiteSpace(column.SqlType))
                return Failure(context, "SqlType cannot be empty or whitespace", "SqlType");
            var conflict = ExplicitFacetConflict(column);
            if (conflict is not null) return Failure(context, conflict.Value.Message, conflict.Value.Facet);
            if (!TryParseExplicit(column.SqlType!, out physical, out var parseError))
                return Failure(context, parseError, "SqlType");
            if (!IsCompatible(column.TypeClass, physical!))
                return Failure(context, $"provider CLR type '{column.ProviderClrTypeName}' is incompatible with SQL type '{physical!.Ddl}'", "SqlType");
        }
        else
        {
            if (!TryInfer(column, out physical, out var inferError, out var inferFacet))
                return Failure(context, inferError ?? $"provider CLR type '{column.ProviderClrTypeName}' has no exact TVP mapping", inferFacet);
        }

        var resolved = physical!;
        var nullable = context.ElementIsNullable || column.ProviderValueIsNullable;
        var signature = resolved.Canonical + "|nullable=" + (nullable ? "1" : "0");
        var schema = string.IsNullOrWhiteSpace(context.OwningSchema) ? "dbo" : context.OwningSchema!;
        var hash = Hash("sqlserver-tvp-v2|element=" + signature);
        var name = "Inquiry_Tvp_" + hash;
        var quotedSchema = builder.QuoteIdentifier(schema);
        var quotedName = builder.QuoteIdentifier(name);
        var qualified = quotedSchema + "." + quotedName;
        var nullSql = nullable ? "NULL" : "NOT NULL";
        var createType = $"CREATE TYPE {qualified} AS TABLE ([Value] {resolved.Ddl} {nullSql})";
        var schemaDdl = string.IsNullOrWhiteSpace(context.OwningSchema)
            ? string.Empty
            : $"IF SCHEMA_ID(N'{Sql(schema)}') IS NULL EXEC(N'CREATE SCHEMA {Sql(quotedSchema)}');\n";
        var ddl = $"IF TYPE_ID(N'{Sql(qualified)}') IS NULL EXEC(N'{Sql(createType)}');";
        var descriptor = "global::Inquiry.SqlServer.Parameters.InquiryTvpDescriptor.Get(\"" + resolved.DescriptorKind + "\", " +
            resolved.Length.ToString(CultureInfo.InvariantCulture) + "L, " +
            resolved.Precision.ToString(CultureInfo.InvariantCulture) + ", " +
            resolved.Scale.ToString(CultureInfo.InvariantCulture) + ", " + (nullable ? "true" : "false") +
            ") ?? throw new global::System.InvalidOperationException(\"SQL Server TVP descriptor resolution returned null.\")";
        var descriptorField = "_inquiryTvpDescriptor_" + hash;

        return new(new CollectionParameterArtifact(
            "sqlserver-tvp-v2|schema=" + schema + "|element=" + signature,
            schema,
            name,
            qualified,
            schemaDdl,
            ddl,
            qualified,
            signature,
            "global::Inquiry.SqlServer.Parameters.InquiryTvpDescriptor",
            descriptorField,
            descriptor,
            BuildValidationSql(schema, name, qualified, resolved, nullable)), null);
    }

    private static CollectionParameterResolution Failure(CollectionParameterContext context, string message, string? facet)
        => new(null, new CollectionParameterDiagnostic(
            InvalidMapping,
            facet,
            message,
            ImmutableArray.Create(context.OperationName, context.Column.PropertyName, message)));

    private static (string Facet, string Message)? ExplicitFacetConflict(IColumn column)
    {
        if (column.IsUnicodeSpecified) return ("IsUnicode", "SqlType owns ANSI/Unicode; remove the explicit IsUnicode facet");
        if (column.IsLengthSpecified) return ("Length", "SqlType owns length; remove the explicit Length facet");
        if (column.IsPrecisionSpecified) return ("Precision", "SqlType owns precision; remove the explicit Precision facet");
        if (column.IsScaleSpecified) return ("Scale", "SqlType owns scale; remove the explicit Scale facet");
        return null;
    }

    internal static string InferredColumnDdl(IColumn column)
    {
        if (TryInfer(column, out var physical, out _, out _)) return physical!.Ddl;

        // EntityProcessor reports invalid column metadata separately. Keep schema emission valid while
        // the affected collection method is replaced by its INQ076 throwing stub.
        return column.TypeClass switch
        {
            DbTypeClass.Decimal => "DECIMAL(18,2)",
            DbTypeClass.DateTime => "DATETIME2(7)",
            DbTypeClass.DateTimeOffset => "DATETIMEOFFSET(7)",
            DbTypeClass.TimeOnly => "TIME(7)",
            _ => throw new NotSupportedException($"No SQL Server mapping for '{column.ProviderClrTypeName}'."),
        };
    }

    private static bool TryInfer(IColumn column, out PhysicalType? physical, out string? error, out string? facet)
    {
        error = null;
        facet = null;
        physical = column.TypeClass switch
        {
            DbTypeClass.Boolean => Simple("bit", "BIT", "bit"),
            DbTypeClass.Byte => Simple("tinyint", "TINYINT", "tinyint", precision: 3),
            DbTypeClass.Int16 => Simple("smallint", "SMALLINT", "smallint", precision: 5),
            DbTypeClass.Int32 => Simple("int", "INT", "int", precision: 10),
            DbTypeClass.Int64 => Simple("bigint", "BIGINT", "bigint", precision: 19),
            DbTypeClass.Single => Simple("real", "REAL", "real", precision: 24),
            DbTypeClass.Double => Simple("float(53)", "FLOAT", "float", precision: 53),
            DbTypeClass.String => Text(column.IsUnicode, column.Length),
            DbTypeClass.Guid => Simple("uniqueidentifier", "UNIQUEIDENTIFIER", "uniqueidentifier"),
            DbTypeClass.DateOnly => Simple("date", "DATE", "date"),
            DbTypeClass.ByteArray => Binary(column.Length),
            _ => null,
        };
        if (physical is not null) return true;

        if (column.TypeClass == DbTypeClass.Decimal)
        {
            if (column.IsPrecisionSpecified && column.Precision is < 1 or > 38)
            {
                error = "decimal precision must be 1..38";
                facet = "Precision";
                return false;
            }
            if (!column.IsPrecisionSpecified && column.IsScaleSpecified)
            {
                error = "decimal Scale requires an explicit Precision";
                facet = "Scale";
                return false;
            }
            var precision = column.IsPrecisionSpecified ? column.Precision : 18;
            var scale = column.IsPrecisionSpecified ? column.Scale : 2;
            if (scale < 0 || scale > precision)
            {
                error = $"decimal scale must be 0..{precision}";
                facet = "Scale";
                return false;
            }
            physical = Decimal(precision, scale);
            return true;
        }

        if (column.TypeClass is DbTypeClass.DateTime or DbTypeClass.DateTimeOffset or DbTypeClass.TimeOnly)
        {
            var scale = column.IsScaleSpecified ? column.Scale : 7;
            if (scale is < 0 or > 7)
            {
                error = "temporal scale must be 0..7";
                facet = "Scale";
                return false;
            }
            var name = column.TypeClass switch
            {
                DbTypeClass.DateTime => "datetime2",
                DbTypeClass.DateTimeOffset => "datetimeoffset",
                _ => "time",
            };
            physical = Temporal(name, scale);
            return true;
        }

        error = $"provider CLR type '{column.ProviderClrTypeName}' has no exact TVP mapping";
        return false;
    }

    private static bool TryParseExplicit(string value, out PhysicalType? physical, out string error)
    {
        physical = null;
        error = "SqlType is outside the supported primitive TVP grammar";
        var match = TypePattern.Match(value);
        if (!match.Success) return false;
        var name = match.Groups["name"].Value.ToLowerInvariant();
        var a = match.Groups["a"].Success ? match.Groups["a"].Value : null;
        var b = match.Groups["b"].Success ? match.Groups["b"].Value : null;
        if (name is "xml" or "sql_variant" or "text" or "ntext" or "image" or "timestamp" or "rowversion")
        {
            error = $"SQL type '{name}' is not supported for generated TVP artifacts";
            return false;
        }

        if (a is null)
        {
            physical = name switch
            {
                "bit" => Simple("bit", "BIT", "bit"),
                "tinyint" => Simple("tinyint", "TINYINT", "tinyint", precision: 3),
                "smallint" => Simple("smallint", "SMALLINT", "smallint", precision: 5),
                "int" => Simple("int", "INT", "int", precision: 10),
                "bigint" => Simple("bigint", "BIGINT", "bigint", precision: 19),
                "real" => Simple("real", "REAL", "real", precision: 24),
                "float" => Simple("float(53)", "FLOAT", "float", precision: 53),
                "uniqueidentifier" => Simple("uniqueidentifier", "UNIQUEIDENTIFIER", "uniqueidentifier"),
                "date" => Simple("date", "DATE", "date"),
                "datetime" => Simple("datetime", "DATETIME", "datetime"),
                "smalldatetime" => Simple("smalldatetime", "SMALLDATETIME", "smalldatetime"),
                "datetime2" => Temporal("datetime2", 7),
                "datetimeoffset" => Temporal("datetimeoffset", 7),
                "time" => Temporal("time", 7),
                _ => null,
            };
            return physical is not null;
        }

        if (b is not null)
        {
            if (name is not "decimal" and not "numeric" || !TryInt(a, out var precision) || !TryInt(b, out var scale) || precision is < 1 or > 38 || scale < 0 || scale > precision)
            {
                error = "decimal/numeric requires precision 1..38 and scale 0..precision";
                return false;
            }
            physical = Decimal(precision, scale);
            return true;
        }

        if (name is "decimal" or "numeric")
        {
            error = "decimal/numeric requires both precision and scale";
            return false;
        }
        if (name == "float")
        {
            if (!TryInt(a, out var bits) || bits is < 1 or > 53) { error = "FLOAT precision must be 1..53"; return false; }
            physical = bits <= 24 ? Simple("real", "REAL", "real", precision: 24) : Simple("float(53)", "FLOAT", "float", precision: 53);
            return true;
        }
        if (name is "datetime2" or "datetimeoffset" or "time")
        {
            if (!TryInt(a, out var scale) || scale is < 0 or > 7) { error = "temporal scale must be 0..7"; return false; }
            physical = Temporal(name, scale);
            return true;
        }
        if (name is "char" or "varchar" or "nchar" or "nvarchar" or "binary" or "varbinary")
        {
            var isMax = a.Equals("max", StringComparison.OrdinalIgnoreCase);
            if (isMax && name is not "varchar" and not "nvarchar" and not "varbinary") { error = $"{name} does not support MAX"; return false; }
            var max = name[0] == 'n' ? 4000 : 8000;
            if (!isMax && (!TryInt(a, out var length) || length < 1 || length > max)) { error = $"{name} length must be 1..{max}"; return false; }
            var size = isMax ? -1 : int.Parse(a, CultureInfo.InvariantCulture);
            var ddlSize = isMax ? "MAX" : size.ToString(CultureInfo.InvariantCulture);
            var canonical = name + "(" + ddlSize.ToLowerInvariant() + ")";
            physical = new PhysicalType(canonical, name.ToUpperInvariant() + "(" + ddlSize + ")", name, name, size,
                0, 0, name is "nvarchar" or "nchar", name is "varchar" or "nvarchar" or "char" or "nchar");
            return true;
        }

        error = $"SQL type '{name}' has unsupported facets";
        return false;
    }

    private static bool IsCompatible(DbTypeClass type, PhysicalType physical) => type switch
    {
        DbTypeClass.Boolean => physical.SystemType == "bit",
        DbTypeClass.Byte => physical.SystemType == "tinyint",
        DbTypeClass.Int16 => physical.SystemType == "smallint",
        DbTypeClass.Int32 => physical.SystemType == "int",
        DbTypeClass.Int64 => physical.SystemType == "bigint",
        DbTypeClass.Single => physical.SystemType == "real",
        DbTypeClass.Double => physical.SystemType == "float",
        DbTypeClass.Decimal => physical.SystemType == "decimal",
        DbTypeClass.String => physical.SystemType is "char" or "varchar" or "nchar" or "nvarchar",
        DbTypeClass.Guid => physical.SystemType == "uniqueidentifier",
        DbTypeClass.DateTime => physical.SystemType is "datetime" or "smalldatetime" or "datetime2",
        DbTypeClass.DateTimeOffset => physical.SystemType == "datetimeoffset",
        DbTypeClass.DateOnly => physical.SystemType == "date",
        DbTypeClass.TimeOnly => physical.SystemType == "time",
        DbTypeClass.ByteArray => physical.SystemType is "binary" or "varbinary",
        _ => false,
    };

    private static PhysicalType Text(bool unicode, int length)
    {
        var bounded = length > 0 && length <= (unicode ? 4000 : 8000);
        var name = unicode ? "nvarchar" : "varchar";
        var size = bounded ? length : -1;
        var token = bounded ? length.ToString(CultureInfo.InvariantCulture) : "MAX";
        return new(name + "(" + token.ToLowerInvariant() + ")", name.ToUpperInvariant() + "(" + token + ")", name, name, size, 0, 0, unicode, true);
    }

    private static PhysicalType Binary(int length)
    {
        var bounded = length > 0 && length <= 8000;
        var size = bounded ? length : -1;
        var token = bounded ? length.ToString(CultureInfo.InvariantCulture) : "MAX";
        return new("varbinary(" + token.ToLowerInvariant() + ")", "VARBINARY(" + token + ")", "varbinary", "varbinary", size, 0, 0, false, false);
    }

    private static PhysicalType Decimal(int precision, int scale)
        => new($"decimal({precision},{scale})", $"DECIMAL({precision},{scale})", "decimal", "decimal", 0, precision, scale, false, false);

    private static PhysicalType Temporal(string name, int configuredScale)
    {
        var scale = configuredScale;
        return new($"{name}({scale})", $"{name.ToUpperInvariant()}({scale})", name, name, 0, 0, scale, false, false);
    }

    private static PhysicalType Simple(string canonical, string ddl, string kind, int precision = 0)
        => new(canonical, ddl, kind, kind, 0, precision, 0, false, false);

    private static string BuildValidationSql(string schema, string name, string qualified, PhysicalType p, bool nullable)
    {
        var expectedMaxLength = ExpectedMaxLength(p);
        var expectedPrecision = ExpectedPrecision(p);
        var collation = p.IsText ? " AND c.collation_name = CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), 'Collation'))" : " AND c.collation_name IS NULL";
        var exact = "EXISTS (SELECT 1 FROM sys.table_types tt " +
            "JOIN sys.schemas ss ON ss.schema_id = tt.schema_id " +
            "JOIN sys.columns c ON c.object_id = tt.type_table_object_id AND c.column_id = 1 " +
            "JOIN sys.types st ON st.user_type_id = c.user_type_id " +
            $"WHERE ss.name = N'{Sql(schema)}' AND tt.name = N'{Sql(name)}' AND tt.is_memory_optimized = 0 " +
            "AND (SELECT COUNT(*) FROM sys.columns ac WHERE ac.object_id = tt.type_table_object_id) = 1 " +
            $"AND c.name = N'Value' AND st.name = N'{Sql(p.SystemType)}' AND st.is_user_defined = 0 AND st.is_assembly_type = 0 " +
            $"AND c.max_length = {expectedMaxLength} AND c.precision = {expectedPrecision} AND c.scale = {p.Scale} " +
            $"AND c.is_nullable = {(nullable ? 1 : 0)} AND c.is_identity = 0 AND c.is_computed = 0{collation} " +
            "AND NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id = tt.type_table_object_id AND i.index_id > 0) " +
            "AND NOT EXISTS (SELECT 1 FROM sys.key_constraints k WHERE k.parent_object_id = tt.type_table_object_id) " +
            "AND NOT EXISTS (SELECT 1 FROM sys.check_constraints ck WHERE ck.parent_object_id = tt.type_table_object_id) " +
            "AND NOT EXISTS (SELECT 1 FROM sys.default_constraints d WHERE d.parent_object_id = tt.type_table_object_id))";
        var status = $"CASE WHEN TYPE_ID(N'{Sql(qualified)}') IS NULL AND COALESCE(HAS_PERMS_BY_NAME(N'{Sql(schema)}', 'SCHEMA', 'VIEW DEFINITION'), 0) = 0 AND COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION'), 0) = 0 THEN N'metadata-invisible' " +
            $"WHEN TYPE_ID(N'{Sql(qualified)}') IS NULL THEN N'missing' WHEN {exact} THEN N'valid' ELSE N'mismatched' END";
        return $"SELECT N'{Sql(qualified)}' AS [ArtifactName], N'{Sql(p.Canonical + "|nullable=" + (nullable ? "1" : "0"))}' AS [ExpectedElementSignature], v.[Status], " +
            "CASE v.[Status] WHEN N'metadata-invisible' THEN N'catalog metadata is not visible' WHEN N'missing' THEN N'type is missing' ELSE N'type metadata does not exactly match' END AS [Details] " +
            $"FROM (SELECT {status} AS [Status]) v WHERE v.[Status] <> N'valid'";
    }

    private static int ExpectedMaxLength(PhysicalType p) => p.SystemType switch
    {
        "bit" or "tinyint" => 1,
        "smallint" => 2,
        "int" or "real" or "smalldatetime" => 4,
        "bigint" or "float" or "datetime" => 8,
        "uniqueidentifier" => 16,
        "date" => 3,
        "decimal" => p.Precision <= 9 ? 5 : p.Precision <= 19 ? 9 : p.Precision <= 28 ? 13 : 17,
        "time" => p.Scale <= 2 ? 3 : p.Scale <= 4 ? 4 : 5,
        "datetime2" => p.Scale <= 2 ? 6 : p.Scale <= 4 ? 7 : 8,
        "datetimeoffset" => p.Scale <= 2 ? 8 : p.Scale <= 4 ? 9 : 10,
        _ => p.Length < 0 ? -1 : p.Unicode ? p.Length * 2 : p.Length,
    };

    private static int ExpectedPrecision(PhysicalType p) => p.SystemType switch
    {
        "bit" => 1,
        "date" => 10,
        "datetime" => 23,
        "smalldatetime" => 16,
        "time" => p.Scale == 0 ? 8 : 9 + p.Scale,
        "datetime2" => p.Scale == 0 ? 19 : 20 + p.Scale,
        "datetimeoffset" => p.Scale == 0 ? 26 : 27 + p.Scale,
        _ => p.Precision,
    };

    private static bool TryInt(string value, out int result)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static string Hash(string value)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
        var hex = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return hex.ToString();
    }

    private static string Sql(string value) => value.Replace("'", "''");

    private sealed class PhysicalType
    {
        public PhysicalType(string canonical, string ddl, string descriptorKind, string systemType, int length, int precision, int scale, bool unicode, bool isText)
        {
            Canonical = canonical;
            Ddl = ddl;
            DescriptorKind = descriptorKind;
            SystemType = systemType;
            Length = length;
            Precision = precision;
            Scale = scale;
            Unicode = unicode;
            IsText = isText;
        }

        public string Canonical { get; }
        public string Ddl { get; }
        public string DescriptorKind { get; }
        public string SystemType { get; }
        public int Length { get; }
        public int Precision { get; }
        public int Scale { get; }
        public bool Unicode { get; }
        public bool IsText { get; }
    }
}
