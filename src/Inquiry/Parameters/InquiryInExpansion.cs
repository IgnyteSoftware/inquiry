using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;
using System.Text;

namespace Inquiry.Parameters;

/// <summary>
/// Runtime helper for <c>Compare.In</c> predicates. The generator bakes a single-placeholder sentinel
/// (<c>col IN (@name)</c>) into the const SQL because the statement text must be constant at compile
/// time, but an <c>IN</c> list's length is only known at run time. This helper rewrites that sentinel
/// into <c>(@name0, @name1, …)</c> and adds one <see cref="DbParameter"/> per element. An empty
/// collection rewrites to <c>(NULL)</c>, which matches no rows.
/// <para>
/// The expanded list is padded up to the next power-of-two length (1, 2, 4, 8, …) by repeating an
/// existing element, so every list length within a bucket renders identical SQL text. This caps the
/// number of distinct statements — and therefore cached plans on text-keyed engines (SQL Server's
/// <c>sp_executesql</c> cache, SQLite/MySQL/Oracle statement caches) — at ~log2 of the parameter limit
/// instead of one per cardinality. A duplicate value is a no-op for both <c>IN</c> and <c>NOT IN</c>, so
/// results are unchanged. (PostgreSQL never reaches this helper — it binds the whole collection as one
/// <c>= ANY(@ids)</c> array, already constant across list sizes.)
/// </para>
/// </summary>
/// <remarks>
/// Inherently allocating (it builds a new command text and N parameters), so it is confined to the
/// <c>IN</c> path; scalar predicates keep the allocation-free fast binder.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class InquiryInExpansion
{
    // Padding never grows a list past this many IN-list entries. Oracle caps a parenthesized IN list at
    // 1000 expressions (ORA-01795) — the most restrictive of the sentinel-path dialects — so a list of
    // 501–1000 real elements, legal today, must NOT be padded up to the 1024 bucket and turned into a
    // runtime error. Lists whose next bucket would exceed this are left at their exact length (the
    // pre-bucketing behavior). Kept dialect-agnostic on purpose: large IN lists are rare and the
    // plan-cache win is concentrated in small/medium lists, so a single conservative ceiling beats
    // threading a per-dialect limit through every generated call site.
    private const int MaxBucketableInListLength = 1000;

    /// <summary>
    /// Expands the <c>IN</c> sentinel <c>(<paramref name="parameterName"/>)</c> in
    /// <paramref name="command"/>'s text into one placeholder per value in <paramref name="values"/>,
    /// adding a matching parameter for each. An empty or null collection rewrites the sentinel to
    /// <c>(NULL)</c> so the predicate matches no rows.
    /// </summary>
    /// <typeparam name="T">The element type of the IN collection.</typeparam>
    public static void Expand<T>(DbCommand command, string parameterName, IEnumerable<T>? values)
        => Expand(command, parameterName, values, InquiryOptions.DefaultMaxParametersPerCommand);

    /// <summary>
    /// Expands the <c>IN</c> sentinel with an explicit maximum total parameter count.
    /// </summary>
    /// <typeparam name="T">The element type of the IN collection.</typeparam>
    public static void Expand<T>(DbCommand command, string parameterName, IEnumerable<T>? values, int maxParameterCount)
        => ExpandCore(command, parameterName, values, maxParameterCount, emptyReplacement: "(NULL)", dbType: null);

    /// <summary>
    /// Expands the <c>IN</c> sentinel, stamping each element parameter with <paramref name="dbType"/> so it
    /// binds with the same type the scalar binder uses for that column (e.g. <c>DateTime2</c> on SQL
    /// Server, not legacy <c>datetime</c>).
    /// </summary>
    /// <typeparam name="T">The element type of the IN collection.</typeparam>
    public static void Expand<T>(DbCommand command, string parameterName, IEnumerable<T>? values, int maxParameterCount, System.Data.DbType? dbType)
        => ExpandCore(command, parameterName, values, maxParameterCount, emptyReplacement: "(NULL)", dbType);

    /// <summary>
    /// Expands a <c>NOT IN</c> sentinel — the negated counterpart of <see cref="Expand{T}(DbCommand, string, IEnumerable{T}?)"/>.
    /// A non-empty collection expands identically to <c>(@name0, @name1, …)</c>; an empty (or null)
    /// collection rewrites the sentinel to <c>(NULL) OR 1=1</c> so the predicate matches <em>every</em> row
    /// (an empty <c>NOT IN</c> excludes nothing — the opposite of an empty <c>IN</c>). The generated SQL
    /// wraps the criterion in parentheses (<c>(col NOT IN (sentinel))</c>) so the <c>OR 1=1</c> tautology
    /// stays self-contained when AND/OR-composed with other criteria.
    /// </summary>
    /// <typeparam name="T">The element type of the NOT IN collection.</typeparam>
    public static void ExpandNotIn<T>(DbCommand command, string parameterName, IEnumerable<T>? values)
        => ExpandNotIn(command, parameterName, values, InquiryOptions.DefaultMaxParametersPerCommand);

    /// <summary>Expands a <c>NOT IN</c> sentinel with an explicit maximum total parameter count.</summary>
    /// <typeparam name="T">The element type of the NOT IN collection.</typeparam>
    public static void ExpandNotIn<T>(DbCommand command, string parameterName, IEnumerable<T>? values, int maxParameterCount)
        => ExpandCore(command, parameterName, values, maxParameterCount, emptyReplacement: "(NULL) OR 1=1", dbType: null);

    /// <summary>Expands a <c>NOT IN</c> sentinel, stamping each element parameter with
    /// <paramref name="dbType"/> (the negated counterpart of the <c>IN</c> overload).</summary>
    /// <typeparam name="T">The element type of the NOT IN collection.</typeparam>
    public static void ExpandNotIn<T>(DbCommand command, string parameterName, IEnumerable<T>? values, int maxParameterCount, System.Data.DbType? dbType)
        => ExpandCore(command, parameterName, values, maxParameterCount, emptyReplacement: "(NULL) OR 1=1", dbType);

    private static void ExpandCore<T>(DbCommand command, string parameterName, IEnumerable<T>? values, int maxParameterCount, string emptyReplacement, System.Data.DbType? dbType = null)
    {
        if (command is null) throw new System.ArgumentNullException(nameof(command));
        if (parameterName is null) throw new System.ArgumentNullException(nameof(parameterName));
        if (maxParameterCount < 1) throw new System.ArgumentOutOfRangeException(nameof(maxParameterCount));

        var sentinel = "(" + parameterName + ")";

        if (values is null)
        {
            command.CommandText = ReplaceFirst(command.CommandText, sentinel, emptyReplacement);
            return;
        }

        // Coerce enum elements to their underlying integral type, matching the scalar binder's
        // enum handling. Without this, enum-strict providers (e.g. Npgsql) reject a boxed enum
        // bound against an integer column. Handles both T = MyEnum and T = MyEnum?.
        var elementType = System.Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var enumUnderlyingType = elementType.IsEnum ? System.Enum.GetUnderlyingType(elementType) : null;

        // Parameters already on the command before this expansion (SET/predicate params). The cap and the
        // bucket-padding budget are measured against the command's total, not just the IN elements.
        var baseParameterCount = command.Parameters.Count;

        var placeholders = new StringBuilder("(");
        var count = 0;
        object? lastNonNullBoxed = null;

        void AddElement(int index, object? boxedValue)
        {
            if (index > 0)
            {
                placeholders.Append(", ");
            }

            var elementName = parameterName + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            placeholders.Append(elementName);

            var parameter = command.CreateParameter();
            parameter.ParameterName = elementName;
            if (dbType is not null)
            {
                parameter.DbType = dbType.Value;
            }
            parameter.Value = boxedValue ?? System.DBNull.Value;
            command.Parameters.Add(parameter);
        }

        foreach (var value in values)
        {
            if (command.Parameters.Count >= maxParameterCount)
            {
                throw new System.InvalidOperationException(
                    "Inquiry IN expansion would exceed the configured parameter limit of "
                    + maxParameterCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " parameters for one command. Reduce the collection size, chunk the operation, or raise InquiryOptions.MaxParametersPerCommand if your provider supports it.");
            }

            object? boxed = value;
            if (boxed is not null && enumUnderlyingType is not null)
            {
                boxed = System.Convert.ChangeType(boxed, enumUnderlyingType, System.Globalization.CultureInfo.InvariantCulture);
            }

            // Unsigned/sbyte values are persisted via the same-width storage type the provider accepts
            // (sbyte->byte, ushort->short, uint->int, ulong->long), matching DbTypeMapper and the scalar
            // binder: providers reject DbType.SByte/UInt16/UInt32/UInt64, and the column stores that
            // reinterpreted bit pattern, so an IN element must reinterpret to the same pattern to compare
            // equal — and so the value matches the dbType stamped below. This runs AFTER the enum-unwrap
            // above, so an unsigned-backed enum (T = MyEnum : uint) arrives here as a plain uint and
            // reinterprets correctly. Unlike InquiryParameterBinder this is not gated on dbType: ExpandCore
            // is only reached from generated calls and the [EditorBrowsable(Never)] overloads, where the
            // partner-type representation is always the correct one.
            boxed = boxed switch
            {
                sbyte v  => (object)unchecked((byte)v),
                ushort v => (object)unchecked((short)v),
                uint v   => (object)unchecked((int)v),
                ulong v  => (object)unchecked((long)v),
                _ => boxed,
            };

            AddElement(count, boxed);
            if (boxed is not null)
            {
                lastNonNullBoxed = boxed;
            }

            count++;
        }

        if (count == 0)
        {
            command.CommandText = ReplaceFirst(command.CommandText, sentinel, emptyReplacement);
            return;
        }

        // Plan-cache-stable bucketing: pad the list up to the next power of two (1,2,4,8,…) so every list
        // length within a bucket renders identical SQL text — capping the number of distinct statements (and
        // therefore cached plans) at ~log2(maxParameterCount) instead of one per cardinality. The padding
        // repeats an existing non-null element; a duplicate value is a no-op for both IN (col=v OR col=v) and
        // NOT IN (col<>v AND col<>v), so results are unchanged. NULL is never used to pad: a NULL in a NOT IN
        // list makes the whole predicate UNKNOWN. If every element was NULL there is no safe pad value, so the
        // (degenerate) list is left at its exact length. Padding is skipped when the target bucket would push
        // the command past the parameter cap, or past the dialect IN-list ceiling (see MaxBucketableInListLength).
        if (lastNonNullBoxed is not null)
        {
            var bucket = NextPowerOfTwo(count);
            if (bucket <= MaxBucketableInListLength && baseParameterCount + bucket <= maxParameterCount)
            {
                for (; count < bucket; count++)
                {
                    AddElement(count, lastNonNullBoxed);
                }
            }
        }

        placeholders.Append(')');
        command.CommandText = ReplaceFirst(command.CommandText, sentinel, placeholders.ToString());
    }

    // Smallest power of two >= n (n >= 1). Bounded by the parameter cap at the call site, so no overflow.
    private static int NextPowerOfTwo(int n)
    {
        var power = 1;
        while (power < n)
        {
            power <<= 1;
        }

        return power;
    }

    private static string ReplaceFirst(string text, string search, string replacement)
    {
        var index = text.IndexOf(search, System.StringComparison.Ordinal);
        return index < 0
            ? text
            : text.Substring(0, index) + replacement + text.Substring(index + search.Length);
    }
}
