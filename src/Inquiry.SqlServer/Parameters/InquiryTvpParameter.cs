using Inquiry.Commands;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Inquiry.SqlServer.Parameters;

/// <summary>Binds an exact, pre-provisioned SQL Server table-valued parameter without schema I/O.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class InquiryTvpParameter
{
    /// <summary>Throws for a collection binding that cannot be represented as an exact SQL Server TVP.</summary>
    public static void BindUnsupported<T>(DbCommand command, string parameterName, IEnumerable<T>? values)
    {
        _ = command ?? throw new ArgumentNullException(nameof(command));
        _ = parameterName ?? throw new ArgumentNullException(nameof(parameterName));
        _ = values;
        throw new NotSupportedException($"No TVP type mapping for {typeof(T).FullName}.");
    }

    /// <summary>
    /// Binds one exact TVP descriptor. Nonempty sources are peeked once and then retained as a
    /// single-pass sequence; custom pipelines must call <see cref="InquiryCommandResources.Dispose"/>
    /// in a finally block when abandoning or completing the command.
    /// </summary>
    public static void Bind<T>(DbCommand command, string parameterName, IEnumerable<T>? values, string typeName, InquiryTvpDescriptor descriptor)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (parameterName is null) throw new ArgumentNullException(nameof(parameterName));
        if (typeName is null) throw new ArgumentNullException(nameof(typeName));
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
        ValidateTypeName(typeName);

        TvpRecordEnumerable<T>? owner = null;
        object? parameterValue = null;
        if (values is not null)
        {
            IEnumerator<T>? enumerator = null;
            try
            {
                enumerator = values.GetEnumerator();
                if (enumerator.MoveNext())
                {
                    owner = new TvpRecordEnumerable<T>(enumerator, descriptor);
                    enumerator = null;
                    try
                    {
                        InquiryCommandResources.Register(command, owner);
                    }
                    catch (Exception primaryException)
                    {
                        List<Exception>? cleanupExceptions = null;
                        try { owner.Dispose(); }
                        catch (Exception cleanupException) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, cleanupException); }
                        InquiryCleanup.ThrowIfCleanupFailed(primaryException, cleanupExceptions);
                        throw;
                    }
                    parameterValue = owner;
                }
            }
            catch (Exception primaryException)
            {
                List<Exception>? cleanupExceptions = null;
                if (enumerator is not null)
                {
                    try { enumerator.Dispose(); }
                    catch (Exception cleanupException) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, cleanupException); }
                }
                InquiryCleanup.ThrowIfCleanupFailed(primaryException, cleanupExceptions);
                throw;
            }
            if (enumerator is not null)
            {
                enumerator.Dispose();
            }
        }

        try
        {
            command.Parameters.Add(new SqlParameter
            {
                ParameterName = parameterName,
                SqlDbType = SqlDbType.Structured,
                TypeName = typeName,
                Value = parameterValue,
            });
        }
        catch (Exception primaryException)
        {
            List<Exception>? cleanupExceptions = null;
            if (owner is not null)
            {
                try { InquiryCommandResources.Unregister(command, owner); }
                catch (Exception cleanupException) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, cleanupException); }
                try { owner.Dispose(); }
                catch (Exception cleanupException) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, cleanupException); }
            }
            InquiryCleanup.ThrowIfCleanupFailed(primaryException, cleanupExceptions);
            throw;
        }
    }

    private static void ValidateTypeName(string typeName)
    {
        var index = 0;
        if (!ConsumeBracketedIdentifier(typeName, ref index) || index >= typeName.Length || typeName[index++] != '.' ||
            !ConsumeBracketedIdentifier(typeName, ref index) || index != typeName.Length)
            throw new ArgumentException("TVP type name must be a generated [schema].[type] name.", nameof(typeName));
    }

    private static bool ConsumeBracketedIdentifier(string value, ref int index)
    {
        if (index >= value.Length || value[index++] != '[') return false;
        var contentLength = 0;
        while (index < value.Length)
        {
            if (value[index] != ']') { index++; contentLength++; continue; }
            if (index + 1 < value.Length && value[index + 1] == ']') { index += 2; contentLength++; continue; }
            index++;
            return contentLength is > 0 and <= 128;
        }
        return false;
    }

    private sealed class TvpRecordEnumerable<T> : IEnumerable<SqlDataRecord>, IEnumerator<SqlDataRecord>, IInquiryExecutionResource
    {
        private delegate void ValueWriterDelegate(SqlDataRecord record, T value, InquiryTvpDescriptor descriptor, int index);
        private static readonly ValueWriterDelegate ValueWriter = CreateValueWriter();
        private IEnumerator<T>? _source;
        private readonly InquiryTvpDescriptor _descriptor;
        private SqlDataRecord? _current;
        private bool _first = true;
        private bool _enumerated;
        private int _index = -1;

        public TvpRecordEnumerable(IEnumerator<T> source, InquiryTvpDescriptor descriptor)
        {
            _source = source;
            _descriptor = descriptor;
        }

        public SqlDataRecord Current => _current ?? throw new InvalidOperationException();
        object IEnumerator.Current => Current;

        public IEnumerator<SqlDataRecord> GetEnumerator()
        {
            if (_enumerated) throw new InvalidOperationException("A TVP parameter source can only be enumerated once.");
            _enumerated = true;
            return this;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool MoveNext()
        {
            var source = _source;
            if (source is null) return false;
            try
            {
                var hasValue = _first || source.MoveNext();
                _first = false;
                if (!hasValue) { Dispose(); return false; }
                _index++;
                var record = _current ?? new SqlDataRecord(_descriptor.MetadataArray);
                Write(record, source.Current, _descriptor, _index);
                _current = record;
                return true;
            }
            catch (Exception primaryException)
            {
                List<Exception>? cleanupExceptions = null;
                try { Dispose(); }
                catch (Exception cleanupException) { cleanupExceptions = InquiryCleanup.Add(cleanupExceptions, cleanupException); }
                InquiryCleanup.ThrowIfCleanupFailed(primaryException, cleanupExceptions);
                throw;
            }
        }

        public void Reset() => throw new NotSupportedException("TVP sources are single-pass.");

        public void Dispose()
        {
            var source = Interlocked.Exchange(ref _source, null);
            source?.Dispose();
            _current = null;
        }

        private static void Write(SqlDataRecord record, T value, InquiryTvpDescriptor descriptor, int index)
            => ValueWriter(record, value, descriptor, index);

        private static ValueWriterDelegate CreateValueWriter()
        {
            if (typeof(T) == typeof(bool)) return static (r, v, _, _) => r.SetBoolean(0, Unsafe.As<T, bool>(ref v));
            if (typeof(T) == typeof(byte)) return static (r, v, _, _) => r.SetByte(0, Unsafe.As<T, byte>(ref v));
            if (typeof(T) == typeof(sbyte)) return static (r, v, _, _) => r.SetByte(0, unchecked((byte)Unsafe.As<T, sbyte>(ref v)));
            if (typeof(T) == typeof(short)) return static (r, v, _, _) => r.SetInt16(0, Unsafe.As<T, short>(ref v));
            if (typeof(T) == typeof(ushort)) return static (r, v, _, _) => r.SetInt16(0, unchecked((short)Unsafe.As<T, ushort>(ref v)));
            if (typeof(T) == typeof(int)) return static (r, v, _, _) => r.SetInt32(0, Unsafe.As<T, int>(ref v));
            if (typeof(T) == typeof(uint)) return static (r, v, _, _) => r.SetInt32(0, unchecked((int)Unsafe.As<T, uint>(ref v)));
            if (typeof(T) == typeof(long)) return static (r, v, _, _) => r.SetInt64(0, Unsafe.As<T, long>(ref v));
            if (typeof(T) == typeof(ulong)) return static (r, v, _, _) => r.SetInt64(0, unchecked((long)Unsafe.As<T, ulong>(ref v)));
            if (typeof(T) == typeof(float)) return static (r, v, _, _) => r.SetFloat(0, Unsafe.As<T, float>(ref v));
            if (typeof(T) == typeof(double)) return static (r, v, _, _) => r.SetDouble(0, Unsafe.As<T, double>(ref v));
            if (typeof(T) == typeof(decimal)) return static (r, v, _, _) => r.SetDecimal(0, Unsafe.As<T, decimal>(ref v));
            if (typeof(T) == typeof(Guid)) return static (r, v, _, _) => r.SetGuid(0, Unsafe.As<T, Guid>(ref v));
            if (typeof(T) == typeof(DateTime)) return static (r, v, _, _) => r.SetDateTime(0, Unsafe.As<T, DateTime>(ref v));
            if (typeof(T) == typeof(DateTimeOffset)) return static (r, v, _, _) => r.SetDateTimeOffset(0, Unsafe.As<T, DateTimeOffset>(ref v));
            if (typeof(T) == typeof(DateOnly)) return static (r, v, _, _) => r.SetDateTime(0, Unsafe.As<T, DateOnly>(ref v).ToDateTime(TimeOnly.MinValue));
            if (typeof(T) == typeof(TimeOnly)) return static (r, v, _, _) => r.SetTimeSpan(0, Unsafe.As<T, TimeOnly>(ref v).ToTimeSpan());
            if (typeof(T) == typeof(char)) return static (r, v, _, _) => r.SetString(0, Unsafe.As<T, char>(ref v).ToString());

            if (typeof(T) == typeof(bool?)) return static (r, v, d, i) => { var n = Unsafe.As<T, bool?>(ref v); if (n.HasValue) r.SetBoolean(0, n.GetValueOrDefault()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(byte?)) return static (r, v, d, i) => { var n = Unsafe.As<T, byte?>(ref v); if (n.HasValue) r.SetByte(0, n.GetValueOrDefault()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(sbyte?)) return static (r, v, d, i) => { var n = Unsafe.As<T, sbyte?>(ref v); if (n.HasValue) r.SetByte(0, unchecked((byte)n.GetValueOrDefault())); else SetNull(r, d, i); };
            if (typeof(T) == typeof(short?)) return static (r, v, d, i) => { var n = Unsafe.As<T, short?>(ref v); if (n.HasValue) r.SetInt16(0, n.GetValueOrDefault()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(ushort?)) return static (r, v, d, i) => { var n = Unsafe.As<T, ushort?>(ref v); if (n.HasValue) r.SetInt16(0, unchecked((short)n.GetValueOrDefault())); else SetNull(r, d, i); };
            if (typeof(T) == typeof(int?)) return static (r, v, d, i) => { var n = Unsafe.As<T, int?>(ref v); if (n.HasValue) r.SetInt32(0, n.GetValueOrDefault()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(uint?)) return static (r, v, d, i) => { var n = Unsafe.As<T, uint?>(ref v); if (n.HasValue) r.SetInt32(0, unchecked((int)n.GetValueOrDefault())); else SetNull(r, d, i); };
            if (typeof(T) == typeof(long?)) return static (r, v, d, i) => { var n = Unsafe.As<T, long?>(ref v); if (n.HasValue) r.SetInt64(0, n.GetValueOrDefault()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(ulong?)) return static (r, v, d, i) => { var n = Unsafe.As<T, ulong?>(ref v); if (n.HasValue) r.SetInt64(0, unchecked((long)n.GetValueOrDefault())); else SetNull(r, d, i); };
            if (typeof(T) == typeof(float?)) return static (r, v, d, i) => { var n = Unsafe.As<T, float?>(ref v); if (n.HasValue) r.SetFloat(0, n.GetValueOrDefault()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(double?)) return static (r, v, d, i) => { var n = Unsafe.As<T, double?>(ref v); if (n.HasValue) r.SetDouble(0, n.GetValueOrDefault()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(decimal?)) return static (r, v, d, i) => { var n = Unsafe.As<T, decimal?>(ref v); if (n.HasValue) r.SetDecimal(0, n.GetValueOrDefault()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(Guid?)) return static (r, v, d, i) => { var n = Unsafe.As<T, Guid?>(ref v); if (n.HasValue) r.SetGuid(0, n.GetValueOrDefault()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(DateTime?)) return static (r, v, d, i) => { var n = Unsafe.As<T, DateTime?>(ref v); if (n.HasValue) r.SetDateTime(0, n.GetValueOrDefault()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(DateTimeOffset?)) return static (r, v, d, i) => { var n = Unsafe.As<T, DateTimeOffset?>(ref v); if (n.HasValue) r.SetDateTimeOffset(0, n.GetValueOrDefault()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(DateOnly?)) return static (r, v, d, i) => { var n = Unsafe.As<T, DateOnly?>(ref v); if (n.HasValue) r.SetDateTime(0, n.GetValueOrDefault().ToDateTime(TimeOnly.MinValue)); else SetNull(r, d, i); };
            if (typeof(T) == typeof(TimeOnly?)) return static (r, v, d, i) => { var n = Unsafe.As<T, TimeOnly?>(ref v); if (n.HasValue) r.SetTimeSpan(0, n.GetValueOrDefault().ToTimeSpan()); else SetNull(r, d, i); };
            if (typeof(T) == typeof(char?)) return static (r, v, d, i) => { var n = Unsafe.As<T, char?>(ref v); if (n.HasValue) r.SetString(0, n.GetValueOrDefault().ToString()); else SetNull(r, d, i); };

            if (typeof(T) == typeof(string)) return static (r, v, d, i) => { var value = Unsafe.As<T, string?>(ref v); if (value is null) SetNull(r, d, i); else r.SetString(0, value); };
            if (typeof(T) == typeof(byte[])) return static (r, v, d, i) =>
            {
                var bytes = Unsafe.As<T, byte[]?>(ref v);
                if (bytes is null) SetNull(r, d, i);
                else r.SetSqlBinary(0, new System.Data.SqlTypes.SqlBinary(bytes));
            };

            var nullableType = Nullable.GetUnderlyingType(typeof(T));
            if (nullableType?.IsEnum == true) return CreateNullableEnumWriter(Enum.GetUnderlyingType(nullableType));
            if (typeof(T).IsEnum) return CreateEnumWriter(Enum.GetUnderlyingType(typeof(T)));
            throw new NotSupportedException($"No TVP value writer for {typeof(T).FullName}.");
        }

        private static ValueWriterDelegate CreateEnumWriter(Type underlying) => Type.GetTypeCode(underlying) switch
        {
            TypeCode.SByte => static (r, v, _, _) => r.SetByte(0, unchecked((byte)Unsafe.As<T, sbyte>(ref v))),
            TypeCode.Byte => static (r, v, _, _) => r.SetByte(0, Unsafe.As<T, byte>(ref v)),
            TypeCode.Int16 => static (r, v, _, _) => r.SetInt16(0, Unsafe.As<T, short>(ref v)),
            TypeCode.UInt16 => static (r, v, _, _) => r.SetInt16(0, unchecked((short)Unsafe.As<T, ushort>(ref v))),
            TypeCode.Int32 => static (r, v, _, _) => r.SetInt32(0, Unsafe.As<T, int>(ref v)),
            TypeCode.UInt32 => static (r, v, _, _) => r.SetInt32(0, unchecked((int)Unsafe.As<T, uint>(ref v))),
            TypeCode.Int64 => static (r, v, _, _) => r.SetInt64(0, Unsafe.As<T, long>(ref v)),
            TypeCode.UInt64 => static (r, v, _, _) => r.SetInt64(0, unchecked((long)Unsafe.As<T, ulong>(ref v))),
            _ => throw new NotSupportedException($"No TVP enum writer for {typeof(T).FullName}."),
        };

        private static ValueWriterDelegate CreateNullableEnumWriter(Type underlying) => Type.GetTypeCode(underlying) switch
        {
            TypeCode.SByte => static (r, v, d, i) => { var n = Unsafe.As<T, sbyte?>(ref v); if (n.HasValue) r.SetByte(0, unchecked((byte)n.GetValueOrDefault())); else SetNull(r, d, i); },
            TypeCode.Byte => static (r, v, d, i) => { var n = Unsafe.As<T, byte?>(ref v); if (n.HasValue) r.SetByte(0, n.GetValueOrDefault()); else SetNull(r, d, i); },
            TypeCode.Int16 => static (r, v, d, i) => { var n = Unsafe.As<T, short?>(ref v); if (n.HasValue) r.SetInt16(0, n.GetValueOrDefault()); else SetNull(r, d, i); },
            TypeCode.UInt16 => static (r, v, d, i) => { var n = Unsafe.As<T, ushort?>(ref v); if (n.HasValue) r.SetInt16(0, unchecked((short)n.GetValueOrDefault())); else SetNull(r, d, i); },
            TypeCode.Int32 => static (r, v, d, i) => { var n = Unsafe.As<T, int?>(ref v); if (n.HasValue) r.SetInt32(0, n.GetValueOrDefault()); else SetNull(r, d, i); },
            TypeCode.UInt32 => static (r, v, d, i) => { var n = Unsafe.As<T, uint?>(ref v); if (n.HasValue) r.SetInt32(0, unchecked((int)n.GetValueOrDefault())); else SetNull(r, d, i); },
            TypeCode.Int64 => static (r, v, d, i) => { var n = Unsafe.As<T, long?>(ref v); if (n.HasValue) r.SetInt64(0, n.GetValueOrDefault()); else SetNull(r, d, i); },
            TypeCode.UInt64 => static (r, v, d, i) => { var n = Unsafe.As<T, ulong?>(ref v); if (n.HasValue) r.SetInt64(0, unchecked((long)n.GetValueOrDefault())); else SetNull(r, d, i); },
            _ => throw new NotSupportedException($"No TVP nullable enum writer for {typeof(T).FullName}."),
        };

        private static void SetNull(SqlDataRecord record, InquiryTvpDescriptor descriptor, int index)
        {
            if (!descriptor.IsNullable) throw new InvalidOperationException($"TVP element at index {index} is null but the resolved Value column is NOT NULL.");
            record.SetDBNull(0);
        }
    }

}
