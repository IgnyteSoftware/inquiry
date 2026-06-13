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
            dbParameter.Value = CoerceValue(parameter.Value);

            if (parameter.DbType is not null)
            {
                dbParameter.DbType = parameter.DbType.Value;
            }

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
}
