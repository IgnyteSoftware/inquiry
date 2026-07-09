using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;

namespace Inquiry.SqlServer.Parameters;

/// <summary>
/// Runtime helper for <c>Compare.In</c> predicates on SQL Server. Binds the collection as a
/// table-valued parameter (TVP): the SQL stays <c>col IN (SELECT [Value] FROM @name)</c> for
/// every list length — constant command text, prepared-statement reuse, no per-element parameter
/// cap. The SQL Server counterpart of PostgreSQL's <c>= ANY(@array)</c> via
/// <see cref="Inquiry.Parameters.InquiryArrayParameter"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class InquiryTvpParameter
{
    private static readonly ConcurrentDictionary<string, byte> EnsuredTypes = new();

    /// <summary>
    /// Binds <paramref name="values"/> as a table-valued parameter named
    /// <paramref name="parameterName"/>. A null or empty collection binds an empty TVP, which
    /// matches no rows under <c>IN (SELECT …)</c> — the same semantics as an empty IN list.
    /// Enum elements are coerced to their underlying integral type (matching the scalar binder).
    /// </summary>
    public static void Bind<T>(DbCommand command, string parameterName, IEnumerable<T>? values)
    {
        if (command is null) throw new System.ArgumentNullException(nameof(command));
        if (parameterName is null) throw new System.ArgumentNullException(nameof(parameterName));

        var elementType = System.Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var storageType = elementType.IsEnum ? System.Enum.GetUnderlyingType(elementType) : elementType;
        var (typeName, sqlDbType, metaData) = ResolveTypeInfo(storageType);

        EnsureType(command.Connection!, typeName, storageType);

        var records = new List<SqlDataRecord>();

        if (values is not null)
        {
            foreach (var value in values)
            {
                if (value is null) continue;

                object boxed = value;
                if (elementType.IsEnum)
                {
                    boxed = System.Convert.ChangeType(boxed, storageType, System.Globalization.CultureInfo.InvariantCulture);
                }

                boxed = boxed switch
                {
                    sbyte v  => (object)unchecked((byte)v),
                    ushort v => (object)unchecked((short)v),
                    uint v   => (object)unchecked((int)v),
                    ulong v  => (object)unchecked((long)v),
                    _ => boxed,
                };

                var record = new SqlDataRecord(metaData);
                record.SetValue(0, boxed);
                records.Add(record);
            }
        }

        var parameter = new SqlParameter
        {
            ParameterName = parameterName,
            SqlDbType = SqlDbType.Structured,
            TypeName = typeName,
            Value = records.Count > 0 ? records : null,
        };

        command.Parameters.Add(parameter);
    }

    private static (string TypeName, SqlDbType SqlDbType, SqlMetaData MetaData) ResolveTypeInfo(System.Type storageType)
    {
        return System.Type.GetTypeCode(storageType) switch
        {
            System.TypeCode.Boolean => ("Inquiry_BitList", SqlDbType.Bit, new SqlMetaData("Value", SqlDbType.Bit)),
            System.TypeCode.Byte    => ("Inquiry_TinyIntList", SqlDbType.TinyInt, new SqlMetaData("Value", SqlDbType.TinyInt)),
            System.TypeCode.Int16   => ("Inquiry_SmallIntList", SqlDbType.SmallInt, new SqlMetaData("Value", SqlDbType.SmallInt)),
            System.TypeCode.Int32   => ("Inquiry_IntList", SqlDbType.Int, new SqlMetaData("Value", SqlDbType.Int)),
            System.TypeCode.Int64   => ("Inquiry_BigIntList", SqlDbType.BigInt, new SqlMetaData("Value", SqlDbType.BigInt)),
            System.TypeCode.Single  => ("Inquiry_RealList", SqlDbType.Real, new SqlMetaData("Value", SqlDbType.Real)),
            System.TypeCode.Double  => ("Inquiry_FloatList", SqlDbType.Float, new SqlMetaData("Value", SqlDbType.Float)),
            System.TypeCode.Decimal => ("Inquiry_DecimalList", SqlDbType.Decimal, new SqlMetaData("Value", SqlDbType.Decimal, 18, 2)),
            System.TypeCode.String  => ("Inquiry_NVarCharList", SqlDbType.NVarChar, new SqlMetaData("Value", SqlDbType.NVarChar, SqlMetaData.Max)),
            _ when storageType == typeof(System.Guid) => ("Inquiry_UniqueIdentifierList", SqlDbType.UniqueIdentifier, new SqlMetaData("Value", SqlDbType.UniqueIdentifier)),
            _ when storageType == typeof(System.DateTime) => ("Inquiry_DateTime2List", SqlDbType.DateTime2, new SqlMetaData("Value", SqlDbType.DateTime2)),
            _ when storageType == typeof(System.DateTimeOffset) => ("Inquiry_DateTimeOffsetList", SqlDbType.DateTimeOffset, new SqlMetaData("Value", SqlDbType.DateTimeOffset)),
            _ => throw new System.NotSupportedException($"No TVP type mapping for {storageType.FullName}."),
        };
    }

    private static string ResolveSqlType(System.Type storageType)
    {
        return System.Type.GetTypeCode(storageType) switch
        {
            System.TypeCode.Boolean => "BIT",
            System.TypeCode.Byte    => "TINYINT",
            System.TypeCode.Int16   => "SMALLINT",
            System.TypeCode.Int32   => "INT",
            System.TypeCode.Int64   => "BIGINT",
            System.TypeCode.Single  => "REAL",
            System.TypeCode.Double  => "FLOAT",
            System.TypeCode.Decimal => "DECIMAL(18,2)",
            System.TypeCode.String  => "NVARCHAR(MAX)",
            _ when storageType == typeof(System.Guid) => "UNIQUEIDENTIFIER",
            _ when storageType == typeof(System.DateTime) => "DATETIME2",
            _ when storageType == typeof(System.DateTimeOffset) => "DATETIMEOFFSET",
            _ => throw new System.NotSupportedException($"No SQL type mapping for {storageType.FullName}."),
        };
    }

    private static void EnsureType(DbConnection connection, string typeName, System.Type storageType)
    {
        if (EnsuredTypes.ContainsKey(typeName)) return;

        var sqlType = ResolveSqlType(storageType);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"IF TYPE_ID(N'{typeName}') IS NULL CREATE TYPE [{typeName}] AS TABLE ([Value] {sqlType});";
        cmd.ExecuteNonQuery();

        EnsuredTypes.TryAdd(typeName, 0);
    }
}
