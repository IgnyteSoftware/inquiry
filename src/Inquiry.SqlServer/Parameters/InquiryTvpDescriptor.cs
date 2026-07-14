using Microsoft.Data.SqlClient.Server;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Data;

namespace Inquiry.SqlServer.Parameters;

/// <summary>Immutable generated-support metadata for one exact SQL Server TVP value column.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InquiryTvpDescriptor
{
    private static readonly ConcurrentDictionary<Key, InquiryTvpDescriptor> Cache = new();

    private InquiryTvpDescriptor(string kind, long length, byte precision, byte scale, bool nullable)
    {
        Kind = kind;
        Length = length;
        Precision = precision;
        Scale = scale;
        IsNullable = nullable;
        Metadata = CreateMetadata(kind, length, precision, scale);
        MetadataArray = new[] { Metadata };
    }

    internal string Kind { get; }
    internal long Length { get; }
    internal byte Precision { get; }
    internal byte Scale { get; }
    internal bool IsNullable { get; }
    internal SqlMetaData Metadata { get; }
    internal SqlMetaData[] MetadataArray { get; }

    /// <summary>Returns the cached descriptor for one generator-resolved physical signature.</summary>
    public static InquiryTvpDescriptor Get(string kind, long length, int precision, int scale, bool nullable)
    {
        if (kind is null) throw new ArgumentNullException(nameof(kind));
        if (precision is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(precision));
        if (scale is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(scale));
        var key = new Key(kind, length, (byte)precision, (byte)scale, nullable);
        return Cache.GetOrAdd(key, static value => new InquiryTvpDescriptor(value.Kind, value.Length, value.Precision, value.Scale, value.Nullable));
    }

    internal static InquiryTvpDescriptor Compatibility(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type) is not null || !type.IsValueType;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum) type = Enum.GetUnderlyingType(type);
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => Get("bit", 0, 1, 0, nullable),
            TypeCode.SByte or TypeCode.Byte => Get("tinyint", 0, 3, 0, nullable),
            TypeCode.Int16 or TypeCode.UInt16 => Get("smallint", 0, 5, 0, nullable),
            TypeCode.Int32 or TypeCode.UInt32 => Get("int", 0, 10, 0, nullable),
            TypeCode.Int64 or TypeCode.UInt64 => Get("bigint", 0, 19, 0, nullable),
            TypeCode.Single => Get("real", 0, 24, 0, nullable),
            TypeCode.Double => Get("float", 0, 53, 0, nullable),
            TypeCode.Decimal => Get("decimal", 0, 18, 2, nullable),
            TypeCode.Char or TypeCode.String => Get("nvarchar", SqlMetaData.Max, 0, 0, nullable),
            TypeCode.DateTime => Get("datetime2", 0, 0, 7, nullable),
            _ when type == typeof(Guid) => Get("uniqueidentifier", 0, 0, 0, nullable),
            _ when type == typeof(DateTimeOffset) => Get("datetimeoffset", 0, 0, 7, nullable),
            _ when type == typeof(DateOnly) => Get("date", 0, 0, 0, nullable),
            _ when type == typeof(TimeOnly) => Get("time", 0, 0, 7, nullable),
            _ when type == typeof(byte[]) => Get("varbinary", SqlMetaData.Max, 0, 0, true),
            _ => throw new NotSupportedException($"No TVP type mapping for {type.FullName}."),
        };
    }

    private static SqlMetaData CreateMetadata(string kind, long length, byte precision, byte scale) => kind switch
    {
        "bit" => new("Value", SqlDbType.Bit),
        "tinyint" => new("Value", SqlDbType.TinyInt),
        "smallint" => new("Value", SqlDbType.SmallInt),
        "int" => new("Value", SqlDbType.Int),
        "bigint" => new("Value", SqlDbType.BigInt),
        "real" => new("Value", SqlDbType.Real),
        "float" => new("Value", SqlDbType.Float),
        "decimal" => new("Value", SqlDbType.Decimal, precision, scale),
        "char" => new("Value", SqlDbType.Char, length),
        "varchar" => new("Value", SqlDbType.VarChar, length),
        "nchar" => new("Value", SqlDbType.NChar, length),
        "nvarchar" => new("Value", SqlDbType.NVarChar, length),
        "binary" => new("Value", SqlDbType.Binary, length),
        "varbinary" => new("Value", SqlDbType.VarBinary, length),
        "uniqueidentifier" => new("Value", SqlDbType.UniqueIdentifier),
        "date" => new("Value", SqlDbType.Date),
        "datetime" => new("Value", SqlDbType.DateTime),
        "smalldatetime" => new("Value", SqlDbType.SmallDateTime),
        "datetime2" => new("Value", SqlDbType.DateTime2, 0, scale),
        "datetimeoffset" => new("Value", SqlDbType.DateTimeOffset, 0, scale),
        "time" => new("Value", SqlDbType.Time, 0, scale),
        _ => throw new NotSupportedException($"No TVP metadata mapping for '{kind}'."),
    };

    private readonly record struct Key(string Kind, long Length, byte Precision, byte Scale, bool Nullable);
}
