using System.Text;

namespace Inquiry.Generators;

/// <summary>
/// The quoting, comment and bind-marker rules of one dialect, used to find the named parameters
/// in <c>[InquiryQuery]</c> SQL.
/// </summary>
internal sealed class HandwrittenSqlSyntax
{
    public static readonly HandwrittenSqlSyntax Standard = new();

    /// <summary>The characters that start a named parameter, such as <c>@</c> or <c>:</c>.</summary>
    public string BindPrefixes { get; init; } = "@";

    /// <summary><c>[name]</c> quotes an identifier.</summary>
    public bool BracketIdentifiers { get; init; }

    /// <summary><c>`name`</c> quotes an identifier.</summary>
    public bool BacktickIdentifiers { get; init; }

    /// <summary>A backslash escapes the next character inside a quoted string.</summary>
    public bool BackslashEscapes { get; init; }

    /// <summary><c>#</c> starts a line comment.</summary>
    public bool HashComments { get; init; }

    /// <summary><c>--</c> starts a comment only before whitespace, as in MySQL, where <c>10--@x</c> subtracts.</summary>
    public bool DashCommentNeedsSpace { get; init; }

    /// <summary>A <c>/*</c> inside a block comment opens a nested comment.</summary>
    public bool NestedBlockComments { get; init; }

    /// <summary>PostgreSQL <c>$tag$…$tag$</c> and <c>E'…'</c> strings.</summary>
    public bool PostgreSqlLiterals { get; init; }

    /// <summary>Oracle <c>q'[…]'</c> and <c>nq'[…]'</c> alternative-quote strings.</summary>
    public bool OracleQuotedStrings { get; init; }
}

/// <summary>One named parameter in handwritten SQL. <see cref="Start"/> is the index of its prefix.</summary>
internal readonly record struct SqlBindToken(int Start, int Length, string Name);

/// <summary>
/// Finds and rewrites the named parameters of <c>[InquiryQuery]</c> SQL. The scan skips string
/// literals, quoted identifiers and comments, so text such as <c>'@x'</c> or <c>-- @x</c> is not a
/// parameter. Names follow C# identifier characters, because each one must match a method parameter.
/// </summary>
internal static class HandwrittenSql
{
    public static List<SqlBindToken> FindBindTokens(string sql, HandwrittenSqlSyntax syntax)
    {
        var tokens = new List<SqlBindToken>();
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if ((c == '-' && next == '-' && (!syntax.DashCommentNeedsSpace || i + 2 >= sql.Length || char.IsWhiteSpace(sql[i + 2]) || char.IsControl(sql[i + 2])))
                || (c == '#' && syntax.HashComments))
            {
                i = SkipLine(sql, i);
            }
            else if (c == '/' && next == '*')
            {
                i = SkipBlockComment(sql, i, syntax.NestedBlockComments);
            }
            else if (c == '\'')
            {
                i = SkipQuoted(sql, i, '\'', syntax.BackslashEscapes);
            }
            else if (c == '"')
            {
                i = SkipQuoted(sql, i, '"', syntax.BackslashEscapes);
            }
            else if (c == '[' && syntax.BracketIdentifiers)
            {
                i = SkipQuoted(sql, i, ']', backslashEscapes: false);
            }
            else if (c == '`' && syntax.BacktickIdentifiers)
            {
                i = SkipQuoted(sql, i, '`', backslashEscapes: false);
            }
            else if (syntax.PostgreSqlLiterals && (c is 'E' or 'e') && next == '\'' && !PrecededByWord(sql, i))
            {
                i = SkipQuoted(sql, i + 1, '\'', backslashEscapes: true);
            }
            else if (syntax.PostgreSqlLiterals && c == '$' && !PrecededByWord(sql, i) && TrySkipDollarQuoted(sql, i, out var afterDollar))
            {
                i = afterDollar;
            }
            else if (syntax.OracleQuotedStrings && (c is 'q' or 'Q') && next == '\'' && !PrecededByWord(sql, i) && i + 2 < sql.Length)
            {
                i = SkipOracleQuoted(sql, i);
            }
            else if (syntax.OracleQuotedStrings && (c is 'n' or 'N') && (next is 'q' or 'Q') && i + 3 < sql.Length && sql[i + 2] == '\'' && !PrecededByWord(sql, i))
            {
                i = SkipOracleQuoted(sql, i + 1);
            }
            else if (syntax.BindPrefixes.IndexOf(c) >= 0 && IsBindStart(sql, i, c))
            {
                var end = i + 1;
                while (end < sql.Length && IsNamePart(sql[end]))
                {
                    end++;
                }

                tokens.Add(new SqlBindToken(i, end - i, sql.Substring(i + 1, end - i - 1)));
                i = end;
            }
            else
            {
                i++;
            }
        }

        return tokens;
    }

    /// <summary>Replaces each token with the dialect's parameter text for its name.</summary>
    public static string Rewrite(string sql, IReadOnlyList<SqlBindToken> tokens, Func<string, string> parameterText)
    {
        var builder = new StringBuilder(sql.Length + tokens.Count * 8);
        var copied = 0;
        foreach (var token in tokens)
        {
            builder.Append(sql, copied, token.Start - copied);
            builder.Append(parameterText(token.Name));
            copied = token.Start + token.Length;
        }

        builder.Append(sql, copied, sql.Length - copied);
        return builder.ToString();
    }

    // A prefix after a word, a closing identifier quote or a second prefix is not a parameter: SQL
    // Server @@ROWCOUNT, PostgreSQL x::int and Oracle table@dblink or "Order Details"@dblink stay as written.
    private static bool IsBindStart(string sql, int index, char prefix)
        => index + 1 < sql.Length
            && IsNameStart(sql[index + 1])
            && !PrecededByWord(sql, index)
            && (index == 0 || (sql[index - 1] != prefix && sql[index - 1] is not ('"' or ']' or '`')));

    private static bool PrecededByWord(string sql, int index)
        => index > 0 && (IsNamePart(sql[index - 1]) || sql[index - 1] is '$' or '#');

    private static bool IsNameStart(char c) => c == '_' || char.IsLetter(c);

    // C# identifier-part characters, so a name matches the method parameter it binds to.
    private static bool IsNamePart(char c) => c == '_' || char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) is
        System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark
        or System.Globalization.UnicodeCategory.ConnectorPunctuation or System.Globalization.UnicodeCategory.Format;

    private static int SkipLine(string sql, int index)
    {
        var end = sql.IndexOfAny(['\r', '\n'], index);
        return end < 0 ? sql.Length : end + 1;
    }

    private static int SkipBlockComment(string sql, int index, bool nested)
    {
        var depth = 1;
        var i = index + 2;
        while (i < sql.Length)
        {
            if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
            {
                i += 2;
                if (--depth == 0)
                {
                    return i;
                }
            }
            else if (nested && sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                depth++;
                i += 2;
            }
            else
            {
                i++;
            }
        }

        return sql.Length;
    }

    // A doubled closing character is an escaped one, as in 'it''s' and [a]]b].
    private static int SkipQuoted(string sql, int index, char close, bool backslashEscapes)
    {
        var i = index + 1;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (backslashEscapes && c == '\\')
            {
                i += 2;
            }
            else if (c == close)
            {
                if (i + 1 < sql.Length && sql[i + 1] == close)
                {
                    i += 2;
                }
                else
                {
                    return i + 1;
                }
            }
            else
            {
                i++;
            }
        }

        return sql.Length;
    }

    private static bool TrySkipDollarQuoted(string sql, int index, out int end)
    {
        end = index;
        var tagEnd = index + 1;
        if (tagEnd < sql.Length && IsNameStart(sql[tagEnd]))
        {
            while (tagEnd < sql.Length && IsNamePart(sql[tagEnd]))
            {
                tagEnd++;
            }
        }

        if (tagEnd >= sql.Length || sql[tagEnd] != '$')
        {
            return false;
        }

        var tag = sql.Substring(index, tagEnd - index + 1);
        var close = sql.IndexOf(tag, tagEnd + 1, StringComparison.Ordinal);
        end = close < 0 ? sql.Length : close + tag.Length;
        return true;
    }

    private static int SkipOracleQuoted(string sql, int index)
    {
        var open = sql[index + 2];
        var close = open switch
        {
            '[' => ']',
            '{' => '}',
            '(' => ')',
            '<' => '>',
            _ => open,
        };

        for (var i = index + 3; i + 1 < sql.Length; i++)
        {
            if (sql[i] == close && sql[i + 1] == '\'')
            {
                return i + 2;
            }
        }

        return sql.Length;
    }
}
