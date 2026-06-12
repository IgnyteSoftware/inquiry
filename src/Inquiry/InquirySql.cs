using Inquiry.Commands;
using Inquiry.Parameters;
using System.Data;
using System.Globalization;

namespace Inquiry;

/// <summary>
/// Factory helpers for building parameterized ad-hoc SQL commands.
/// </summary>
public static class InquirySql
{
    // Generated parameter names for the common interpolation arities, so converting a
    // FormattableString doesn't allocate a fresh name string per hole.
    private static readonly string[] CachedNames =
    {
        "@p0", "@p1", "@p2", "@p3", "@p4", "@p5", "@p6", "@p7",
        "@p8", "@p9", "@p10", "@p11", "@p12", "@p13", "@p14", "@p15",
    };

    /// <summary>
    /// Converts an interpolated SQL string into an <see cref="InquiryCommand"/> by replacing every
    /// interpolation hole with a generated parameter name (<c>@p0</c>, <c>@p1</c>, ...).
    /// </summary>
    public static InquiryCommand Sql(
        FormattableString commandText,
        CommandType? commandType = null,
        int? commandTimeout = null)
    {
        if (commandText is null)
        {
            throw new ArgumentNullException(nameof(commandText));
        }

        var args = commandText.GetArguments();
        if (args.Length == 0)
        {
            return new InquiryCommand(commandText.Format, commandType, commandTimeout);
        }

        var placeholders = new object[args.Length];
        var parameters = new InquiryParameter[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            var name = i < CachedNames.Length ? CachedNames[i] : "@p" + i.ToString(CultureInfo.InvariantCulture);
            placeholders[i] = name;
            parameters[i] = new InquiryParameter(name, args[i]);
        }

        var sql = string.Format(CultureInfo.InvariantCulture, commandText.Format, placeholders);
        return new InquiryCommand(sql, parameters, commandType, commandTimeout);
    }
}
