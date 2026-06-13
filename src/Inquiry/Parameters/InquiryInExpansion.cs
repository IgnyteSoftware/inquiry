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
/// </summary>
/// <remarks>
/// Inherently allocating (it builds a new command text and N parameters), so it is confined to the
/// <c>IN</c> path; scalar predicates keep the allocation-free fast binder.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class InquiryInExpansion
{
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
        => ExpandCore(command, parameterName, values, maxParameterCount, emptyReplacement: "(NULL)");

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
        => ExpandCore(command, parameterName, values, maxParameterCount, emptyReplacement: "(NULL) OR 1=1");

    private static void ExpandCore<T>(DbCommand command, string parameterName, IEnumerable<T>? values, int maxParameterCount, string emptyReplacement)
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

        var placeholders = new StringBuilder("(");
        var count = 0;
        foreach (var value in values)
        {
            if (command.Parameters.Count >= maxParameterCount)
            {
                throw new System.InvalidOperationException(
                    "Inquiry IN expansion would exceed the configured parameter limit of "
                    + maxParameterCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " parameters for one command. Reduce the collection size, chunk the operation, or raise InquiryOptions.MaxParametersPerCommand if your provider supports it.");
            }

            if (count > 0)
            {
                placeholders.Append(", ");
            }

            var elementName = parameterName + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            placeholders.Append(elementName);

            object? boxed = value;
            if (boxed is not null && enumUnderlyingType is not null)
            {
                boxed = System.Convert.ChangeType(boxed, enumUnderlyingType, System.Globalization.CultureInfo.InvariantCulture);
            }

            var parameter = command.CreateParameter();
            parameter.ParameterName = elementName;
            parameter.Value = boxed ?? System.DBNull.Value;
            command.Parameters.Add(parameter);

            count++;
        }

        placeholders.Append(')');

        command.CommandText = ReplaceFirst(
            command.CommandText,
            sentinel,
            count == 0 ? emptyReplacement : placeholders.ToString());
    }

    private static string ReplaceFirst(string text, string search, string replacement)
    {
        var index = text.IndexOf(search, System.StringComparison.Ordinal);
        return index < 0
            ? text
            : text.Substring(0, index) + replacement + text.Substring(index + search.Length);
    }
}
