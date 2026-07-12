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
    /// <summary>
    /// Compatibility path for collection element categories that do not yet have a generated
    /// SQL Server TVP artifact. Supported generated calls use the explicit-type-name overload.
    /// </summary>
    public static void BindUnsupported<T>(DbCommand command, string parameterName, IEnumerable<T>? values)
    {
        _ = command ?? throw new System.ArgumentNullException(nameof(command));
        _ = parameterName ?? throw new System.ArgumentNullException(nameof(parameterName));
        _ = values;
        throw new System.NotSupportedException($"No TVP type mapping for {typeof(T).FullName}.");
    }

    /// <summary>
    /// Binds <paramref name="values"/> as a table-valued parameter named
    /// <paramref name="parameterName"/>. A null or empty collection binds an empty TVP, which
    /// matches no rows under <c>IN (SELECT …)</c> — the same semantics as an empty IN list.
    /// Enum elements are coerced to their underlying integral type (matching the scalar binder).
    /// </summary>
    public static void Bind<T>(DbCommand command, string parameterName, IEnumerable<T>? values, string typeName)
    {
        if (command is null) throw new System.ArgumentNullException(nameof(command));
        if (parameterName is null) throw new System.ArgumentNullException(nameof(parameterName));
        if (typeName is null) throw new System.ArgumentNullException(nameof(typeName));
        ValidateTypeName(typeName);

        var elementType = System.Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var storageType = elementType.IsEnum ? System.Enum.GetUnderlyingType(elementType) : elementType;
        var metaData = ResolveTypeInfo(storageType);

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

    private static SqlMetaData ResolveTypeInfo(System.Type storageType)
    {
        return System.Type.GetTypeCode(storageType) switch
        {
            System.TypeCode.Boolean => new SqlMetaData("Value", SqlDbType.Bit),
            System.TypeCode.SByte   => new SqlMetaData("Value", SqlDbType.TinyInt),
            System.TypeCode.Byte    => new SqlMetaData("Value", SqlDbType.TinyInt),
            System.TypeCode.Int16   => new SqlMetaData("Value", SqlDbType.SmallInt),
            System.TypeCode.UInt16  => new SqlMetaData("Value", SqlDbType.SmallInt),
            System.TypeCode.Int32   => new SqlMetaData("Value", SqlDbType.Int),
            System.TypeCode.UInt32  => new SqlMetaData("Value", SqlDbType.Int),
            System.TypeCode.Int64   => new SqlMetaData("Value", SqlDbType.BigInt),
            System.TypeCode.UInt64  => new SqlMetaData("Value", SqlDbType.BigInt),
            System.TypeCode.Single  => new SqlMetaData("Value", SqlDbType.Real),
            System.TypeCode.Double  => new SqlMetaData("Value", SqlDbType.Float),
            System.TypeCode.Decimal => new SqlMetaData("Value", SqlDbType.Decimal, 18, 2),
            System.TypeCode.String  => new SqlMetaData("Value", SqlDbType.NVarChar, SqlMetaData.Max),
            _ when storageType == typeof(System.Guid) => new SqlMetaData("Value", SqlDbType.UniqueIdentifier),
            _ when storageType == typeof(System.DateTime) => new SqlMetaData("Value", SqlDbType.DateTime2),
            _ when storageType == typeof(System.DateTimeOffset) => new SqlMetaData("Value", SqlDbType.DateTimeOffset),
            _ => throw new System.NotSupportedException($"No TVP type mapping for {storageType.FullName}."),
        };
    }

    private static void ValidateTypeName(string typeName)
    {
        var index = 0;
        if (!ConsumeBracketedIdentifier(typeName, ref index)
            || index >= typeName.Length || typeName[index++] != '.'
            || !ConsumeBracketedIdentifier(typeName, ref index)
            || index != typeName.Length)
            throw new System.ArgumentException("TVP type name must be a generated [schema].[type] name.", nameof(typeName));
    }

    private static bool ConsumeBracketedIdentifier(string value, ref int index)
    {
        if (index >= value.Length || value[index++] != '[') return false;
        var contentLength = 0;
        while (index < value.Length)
        {
            if (value[index] != ']')
            {
                index++;
                contentLength++;
                continue;
            }

            if (index + 1 < value.Length && value[index + 1] == ']')
            {
                index += 2;
                contentLength++;
                continue;
            }

            index++;
            return contentLength is > 0 and <= 128;
        }

        return false;
    }
}
