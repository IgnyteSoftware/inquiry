using System.Data.Common;

namespace Inquiry.Parameters;

internal static class InquiryParameterBinder
{
    public static void Bind(DbCommand command, InquiryParameter[] parameters)
    {
        // Index-based loop on a typed array — no IEnumerator<T> box allocation, and `ref` on the
        // array element avoids copying the struct on each iteration.
        for (var i = 0; i < parameters.Length; i++)
        {
            ref readonly var parameter = ref parameters[i];

            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = NormalizeName(parameter.Name);

            var coerced = CoerceValue(parameter.Value);
            if (parameter.DbType is not null)
            {
                var dbType = parameter.DbType.Value;
                // Unsigned/sbyte values carried as a raw boxed primitive (eager-load key/FK, stored-proc,
                // keyset paths) reach here paired with their SIGNED storage DbType. Without this gate,
                // SqlClient does a CHECKED Convert.ToInt32(uint) that overflows for values > int.MaxValue.
                // Reinterpret to the signed partner — but ONLY when the DbType is exactly that partner, so
                // the generated binder-lambda path (already reinterpreted) and ad-hoc params with an
                // unsigned/absent DbType are untouched. Enum keys are covered: CoerceValue already unwraps
                // them to the underlying uint/ushort/ulong/sbyte before this runs.
                coerced = ReinterpretUnsignedForSignedDbType(coerced, dbType);
                dbParameter.DbType = dbType;
            }

            dbParameter.Value = coerced;

            if (parameter.Direction is not null)
            {
                dbParameter.Direction = parameter.Direction.Value;
            }

            if (parameter.Size is not null)
            {
                dbParameter.Size = parameter.Size.Value;
            }

            if (parameter.Precision is not null)
            {
                dbParameter.Precision = parameter.Precision.Value;
            }

            if (parameter.Scale is not null)
            {
                dbParameter.Scale = parameter.Scale.Value;
            }

            command.Parameters.Add(dbParameter);
        }
    }

    private static object CoerceValue(object? value)
    {
        if (value is null)
        {
            return DBNull.Value;
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            return Convert.ChangeType(value, Enum.GetUnderlyingType(type), System.Globalization.CultureInfo.InvariantCulture);
        }

        return value;
    }

    /// <summary>
    /// Reinterprets a boxed unsigned/sbyte value to its same-width SIGNED partner when the parameter's
    /// DbType is exactly that partner. This mirrors the compile-time reinterpretation the generated
    /// binders apply (sbyte→byte, ushort→short, uint→int, ulong→long), keeping high/negative values
    /// lossless instead of letting SqlClient's checked Convert.ToIntNN throw OverflowException. The
    /// exact-DbType guard ensures only the reinterpret-mapped paths are affected.
    /// </summary>
    private static object ReinterpretUnsignedForSignedDbType(object value, System.Data.DbType dbType) => (value, dbType) switch
    {
        (uint u,   System.Data.DbType.Int32) => unchecked((int)u),
        (ushort u, System.Data.DbType.Int16) => unchecked((short)u),
        (ulong u,  System.Data.DbType.Int64) => unchecked((long)u),
        (sbyte s,  System.Data.DbType.Byte)  => unchecked((byte)s),
        _ => value,
    };

    /// <summary>
    /// Adds an <c>@</c> prefix to a parameter name that carries no provider sigil. Shared so the
    /// stored-procedure read-back lookup matches the name a bound parameter ends up with.
    /// </summary>
    internal static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));
        }

        // Generated stores emit names already prefixed with '@' so we take this fast path on
        // every hot-path bind. The branch stays for hand-crafted InquiryParameter callers whose
        // names lack the prefix.
        return name[0] is '@' or ':' or '$' or '?'
            ? name
            : "@" + name;
    }

    /// <summary>
    /// Resolves a bound parameter after provider finalization. Some providers retain Inquiry's
    /// transport sigil while Oracle removes it to preserve stored-procedure formal names.
    /// </summary>
    internal static DbParameter FindByLogicalName(DbParameterCollection parameters, string name)
    {
        var normalized = NormalizeName(name);
        var logical = normalized.Substring(1);
        foreach (DbParameter parameter in parameters)
        {
            var candidate = parameter.ParameterName;
            if (candidate.Length > 0 && candidate[0] is '@' or ':' or '$' or '?')
            {
                candidate = candidate.Substring(1);
            }
            if (string.Equals(candidate, logical, StringComparison.OrdinalIgnoreCase)) return parameter;
        }

        throw new IndexOutOfRangeException($"The command does not contain a read-back parameter named '{name}'.");
    }
}
