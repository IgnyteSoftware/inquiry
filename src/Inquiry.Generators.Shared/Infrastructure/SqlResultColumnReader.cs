using System.Collections.Immutable;

namespace Inquiry.Generators.Infrastructure;

/// <summary>The SQL dialects whose result-column names <see cref="SqlResultColumnReader"/> knows.</summary>
internal enum SqlResultDialect
{
    SqlServer,
    PostgreSql,
    MySql,
    MariaDb,
    Oracle,
    Sqlite,
}

/// <summary>How certain the reader is of a result column's name.</summary>
internal enum ResultColumnKind
{
    /// <summary>The column has this name: an alias or a column reference.</summary>
    Named,

    /// <summary>SQL Server returns the expression with an empty name.</summary>
    Unnamed,

    /// <summary>The provider names the column with the expression text, which is never a plain identifier.</summary>
    Opaque,

    /// <summary>The name depends on provider rules that the reader does not model.</summary>
    Unknown,
}

internal readonly struct ResultColumn
{
    public ResultColumn(ResultColumnKind kind, string name, string text)
    {
        Kind = kind;
        Name = name;
        Text = text;
    }

    public ResultColumnKind Kind { get; }

    /// <summary>The column name when <see cref="Kind"/> is <see cref="ResultColumnKind.Named"/>.</summary>
    public string Name { get; }

    /// <summary>The SQL text of the result expression.</summary>
    public string Text { get; }
}

/// <summary>
/// Reads the names of the first result set that a SQL batch returns: the outer <c>SELECT</c> list after
/// any CTEs (the first branch of a set operation), or the <c>OUTPUT</c>/<c>RETURNING</c> list of an
/// <c>INSERT</c>, <c>UPDATE</c> or <c>DELETE</c>. It returns null for anything it does not recognize,
/// because a wrong name gives a false warning, and a gap only misses one.
/// </summary>
internal static class SqlResultColumnReader
{
    private static readonly HashSet<string> _listTerminators = new(StringComparer.OrdinalIgnoreCase)
    {
        "FROM", "WHERE", "GROUP", "HAVING", "ORDER", "UNION", "EXCEPT", "INTERSECT", "VALUES", "DEFAULT", "SELECT",
    };

    // These clause words are not reserved in every dialect, so each can also be an alias written
    // without AS, such as SELECT Amount Limit. A list that reaches one gives no result.
    private static readonly HashSet<string> _ambiguousListEnds = new(StringComparer.OrdinalIgnoreCase)
    {
        "LIMIT", "OFFSET", "FETCH", "WINDOW", "MINUS", "OPTION", "QUALIFY",
    };

    // T-SQL statements need no semicolon, so the next statement's first word also ends a list. Other
    // dialects use some of these words as function names, such as MySQL's IF().
    private static readonly HashSet<string> _sqlServerStatementWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "SET", "EXEC", "EXECUTE", "IF", "BEGIN", "COMMIT",
        "ROLLBACK", "SAVE", "RETURN", "PRINT", "RAISERROR", "WHILE", "WAITFOR", "GOTO", "BREAK", "CONTINUE",
        "TRUNCATE", "CREATE", "ALTER", "DROP", "GRANT", "REVOKE", "DENY", "USE", "OPEN", "CLOSE", "DEALLOCATE",
    };

    // An OUTPUT clause comes before these words. Reaching one first means the statement returns no rows.
    private static readonly HashSet<string> _outputScanStops = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "VALUES", "WHERE", "DEFAULT", "EXEC", "EXECUTE", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE",
    };

    // Reserved words that take an operand on their right, so a name after one is that operand, not an alias.
    private static readonly HashSet<string> _operatorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR", "NOT", "IS", "IN", "LIKE", "BETWEEN", "THEN", "ELSE", "WHEN", "CASE", "ESCAPE",
        "OVER", "FOR", "AS",
    };

    // Reserved T-SQL functions called without parentheses. SQL Server returns them unnamed.
    private static readonly HashSet<string> _sqlServerNiladicFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "CURRENT_TIMESTAMP", "CURRENT_USER", "SESSION_USER", "SYSTEM_USER", "USER",
    };

    private static readonly HashSet<string> _mySqlSelectModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "HIGH_PRIORITY", "STRAIGHT_JOIN", "SQL_SMALL_RESULT", "SQL_BIG_RESULT", "SQL_BUFFER_RESULT",
        "SQL_NO_CACHE", "SQL_CACHE", "SQL_CALC_FOUND_ROWS",
    };

    /// <summary>Returns the result columns, or null when the result list cannot be known.</summary>
    public static ImmutableArray<ResultColumn>? Read(string sql, SqlResultDialect dialect)
    {
        var tokens = Tokenizer.Tokenize(sql, dialect);
        return tokens is null ? null : new Parser(sql, tokens, dialect).ReadResult();
    }

    private enum TokenKind
    {
        Word,
        Quoted,
        String,
        Number,
        Parameter,
        Symbol,
    }

    private readonly struct Token
    {
        public Token(TokenKind kind, string value, int start, int end)
        {
            Kind = kind;
            Value = value;
            Start = start;
            End = end;
        }

        public TokenKind Kind { get; }

        /// <summary>The word, the unescaped quoted name or string content, or the symbol.</summary>
        public string Value { get; }

        public int Start { get; }

        public int End { get; }

        public bool Is(string keyword)
            => Kind == TokenKind.Word && string.Equals(Value, keyword, StringComparison.OrdinalIgnoreCase);

        public bool IsSymbol(string symbol) => Kind == TokenKind.Symbol && Value == symbol;

        public bool IsOpen => Kind == TokenKind.Symbol && Value is "(" or "[";

        public bool IsClose => Kind == TokenKind.Symbol && Value is ")" or "]";

        public bool IsName => Kind is TokenKind.Word or TokenKind.Quoted;
    }

    private static class Tokenizer
    {
        public static List<Token>? Tokenize(string sql, SqlResultDialect dialect)
        {
            var tokens = new List<Token>();
            var mySql = dialect is SqlResultDialect.MySql or SqlResultDialect.MariaDb;
            var i = 0;
            while (i < sql.Length)
            {
                var c = sql[i];
                var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
                if (char.IsWhiteSpace(c))
                {
                    i++;
                }
                else if (c == '-' && next == '-')
                {
                    // MySQL starts a comment only when white space follows the two dashes.
                    if (mySql && i + 2 < sql.Length && !char.IsWhiteSpace(sql[i + 2]))
                    {
                        return null;
                    }

                    i = SkipLine(sql, i);
                }
                else if (c == '#' && mySql)
                {
                    i = SkipLine(sql, i);
                }
                else if (c == '/' && next == '*')
                {
                    if (!SkipBlockComment(sql, ref i))
                    {
                        return null;
                    }
                }
                else if (!ReadToken(sql, dialect, ref i, tokens))
                {
                    return null;
                }
            }

            return tokens;
        }

        private static bool ReadToken(string sql, SqlResultDialect dialect, ref int i, List<Token> tokens)
        {
            var start = i;
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            var mySql = dialect is SqlResultDialect.MySql or SqlResultDialect.MariaDb;
            string? value;
            switch (c)
            {
                case '\'':
                    value = ReadQuoted(sql, ref i, '\'', backslashEscapes: mySql);
                    return Add(tokens, TokenKind.String, value, start, i);
                case '"':
                    value = ReadQuoted(sql, ref i, '"', backslashEscapes: mySql);
                    return Add(tokens, TokenKind.Quoted, value, start, i);
                case '`':
                    value = ReadQuoted(sql, ref i, '`', backslashEscapes: false);
                    return Add(tokens, TokenKind.Quoted, value, start, i);
                case '[' when dialect is SqlResultDialect.SqlServer or SqlResultDialect.Sqlite:
                    value = ReadQuoted(sql, ref i, ']', backslashEscapes: false);
                    return Add(tokens, TokenKind.Quoted, value, start, i);
                case '[' when dialect is SqlResultDialect.PostgreSql:
                    i++;
                    return Add(tokens, TokenKind.Symbol, "[", start, i);
                case '[' or '{' or '}':
                    // T-SQL ODBC escapes and brackets outside SQL Server, SQLite and PostgreSQL arrays.
                    return false;
                case '@':
                    return ReadParameter(sql, dialect, ref i, tokens);
                case ':' when next == ':':
                    i += 2;
                    return Add(tokens, TokenKind.Symbol, "::", start, i);
                case ':' when IsWordStart(next, dialect):
                    i++;
                    ReadWordChars(sql, dialect, ref i);
                    return Add(tokens, TokenKind.Parameter, sql.Substring(start, i - start), start, i);
                case '?' when dialect is not SqlResultDialect.PostgreSql:
                    i++;
                    return Add(tokens, TokenKind.Parameter, "?", start, i);
                case '$':
                    return dialect == SqlResultDialect.PostgreSql && ReadDollar(sql, ref i, tokens);
            }

            if (char.IsDigit(c) || (c == '.' && char.IsDigit(next)))
            {
                ReadNumber(sql, ref i);
                return Add(tokens, TokenKind.Number, sql.Substring(start, i - start), start, i);
            }

            if (IsWordStart(c, dialect))
            {
                return ReadWord(sql, dialect, ref i, tokens);
            }

            i++;
            return Add(tokens, TokenKind.Symbol, c.ToString(), start, i);
        }

        private static bool ReadWord(string sql, SqlResultDialect dialect, ref int i, List<Token> tokens)
        {
            var start = i;
            i++;
            ReadWordChars(sql, dialect, ref i);
            var word = sql.Substring(start, i - start);
            if (i >= sql.Length || sql[i] != '\'')
            {
                return Add(tokens, TokenKind.Word, word, start, i);
            }

            // Oracle's q'[...]' quoting has its own delimiters.
            if (word.Equals("q", StringComparison.OrdinalIgnoreCase) || word.Equals("nq", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // N'...', E'...', X'...', B'...' and MySQL character set introducers such as _utf8mb4'...'
            // are one string literal.
            var escapes = word.Equals("e", StringComparison.OrdinalIgnoreCase)
                || dialect is SqlResultDialect.MySql or SqlResultDialect.MariaDb;
            if ((word.Length == 1 && "nNeExXbB".IndexOf(word[0]) >= 0) || word[0] == '_')
            {
                var value = ReadQuoted(sql, ref i, '\'', escapes);
                return Add(tokens, TokenKind.String, value, start, i);
            }

            return Add(tokens, TokenKind.Word, word, start, i);
        }

        private static bool ReadParameter(string sql, SqlResultDialect dialect, ref int i, List<Token> tokens)
        {
            var start = i;
            while (i < sql.Length && sql[i] == '@')
            {
                i++;
            }

            if (i >= sql.Length || !(char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
            {
                i = start + 1;
                return Add(tokens, TokenKind.Symbol, "@", start, i);
            }

            ReadWordChars(sql, dialect, ref i);
            return Add(tokens, TokenKind.Parameter, sql.Substring(start, i - start), start, i);
        }

        private static bool ReadDollar(string sql, ref int i, List<Token> tokens)
        {
            var start = i;
            if (i + 1 < sql.Length && char.IsDigit(sql[i + 1]))
            {
                i++;
                ReadNumber(sql, ref i);
                return Add(tokens, TokenKind.Parameter, sql.Substring(start, i - start), start, i);
            }

            // A PostgreSQL dollar-quoted string: $tag$ ... $tag$.
            var tagEnd = sql.IndexOf('$', i + 1);
            if (tagEnd < 0)
            {
                return false;
            }

            for (var k = i + 1; k < tagEnd; k++)
            {
                if (!(char.IsLetterOrDigit(sql[k]) || sql[k] == '_'))
                {
                    return false;
                }
            }

            var tag = sql.Substring(i, tagEnd - i + 1);
            var close = sql.IndexOf(tag, tagEnd + 1, StringComparison.Ordinal);
            if (close < 0)
            {
                return false;
            }

            i = close + tag.Length;
            return Add(tokens, TokenKind.String, sql.Substring(tagEnd + 1, close - tagEnd - 1), start, i);
        }

        private static bool Add(List<Token> tokens, TokenKind kind, string? value, int start, int end)
        {
            if (value is null)
            {
                return false;
            }

            tokens.Add(new Token(kind, value, start, end));
            return true;
        }

        private static string? ReadQuoted(string sql, ref int i, char close, bool backslashEscapes)
        {
            var builder = new System.Text.StringBuilder();
            i++;
            while (i < sql.Length)
            {
                var c = sql[i];
                if (backslashEscapes && c == '\\' && i + 1 < sql.Length)
                {
                    builder.Append(sql[i + 1]);
                    i += 2;
                }
                else if (c == close)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == close)
                    {
                        builder.Append(close);
                        i += 2;
                    }
                    else
                    {
                        i++;
                        return builder.ToString();
                    }
                }
                else
                {
                    builder.Append(c);
                    i++;
                }
            }

            return null;
        }

        private static bool SkipBlockComment(string sql, ref int i)
        {
            // A nested comment or a MySQL executable comment (/*! ... */) changes what the server reads.
            var close = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
            if (close < 0 || (i + 2 < sql.Length && sql[i + 2] == '!') ||
                sql.IndexOf("/*", i + 2, close - i - 2, StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            i = close + 2;
            return true;
        }

        private static int SkipLine(string sql, int i)
        {
            var end = sql.IndexOf('\n', i);
            return end < 0 ? sql.Length : end + 1;
        }

        private static void ReadNumber(string sql, ref int i)
        {
            while (i < sql.Length)
            {
                var c = sql[i];
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_' ||
                    ((c == '+' || c == '-') && (sql[i - 1] == 'e' || sql[i - 1] == 'E')))
                {
                    i++;
                }
                else
                {
                    break;
                }
            }
        }

        private static void ReadWordChars(string sql, SqlResultDialect dialect, ref int i)
        {
            // In MySQL and MariaDB, # always starts a comment, even directly after a name.
            var hashInNames = dialect is not (SqlResultDialect.MySql or SqlResultDialect.MariaDb);
            while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '$' || (hashInNames && sql[i] == '#')))
            {
                i++;
            }
        }

        private static bool IsWordStart(char c, SqlResultDialect dialect)
            => char.IsLetter(c) || c == '_' || (c == '#' && dialect == SqlResultDialect.SqlServer);
    }

    private sealed class Parser
    {
        private readonly string _sql;
        private readonly List<Token> _tokens;
        private readonly SqlResultDialect _dialect;
        private const int MaxNesting = 64;

        public Parser(string sql, List<Token> tokens, SqlResultDialect dialect)
        {
            _sql = sql;
            _tokens = tokens;
            _dialect = dialect;
        }

        public ImmutableArray<ResultColumn>? ReadResult()
        {
            // Some checks rescan nested groups, so deep nesting would cost quadratic time.
            if (MaxDepth() > MaxNesting)
            {
                return null;
            }

            var i = 0;
            while (IsSymbol(i, ";") || IsSymbol(i, "("))
            {
                i++;
            }

            if (Is(i, "WITH"))
            {
                if (!SkipCommonTableExpressions(ref i))
                {
                    return null;
                }

                while (IsSymbol(i, "("))
                {
                    i++;
                }
            }

            if (Is(i, "SELECT"))
            {
                return ReadSelect(i + 1);
            }

            if (Is(i, "INSERT") || Is(i, "UPDATE") || Is(i, "DELETE"))
            {
                return ReadReturnedRows(i + 1);
            }

            return null;
        }

        private int MaxDepth()
        {
            var depth = 0;
            var max = 0;
            foreach (var token in _tokens)
            {
                depth += DepthChange(token);
                max = Math.Max(max, depth);
            }

            return max;
        }

        private bool SkipCommonTableExpressions(ref int i)
        {
            i++;
            if (Is(i, "RECURSIVE"))
            {
                i++;
            }

            while (true)
            {
                if (i >= _tokens.Count || !_tokens[i].IsName)
                {
                    return false;
                }

                i++;
                if (IsSymbol(i, "(") && !SkipGroup(ref i))
                {
                    return false;
                }

                if (!Is(i, "AS"))
                {
                    return false;
                }

                i++;
                if (Is(i, "NOT"))
                {
                    i++;
                }

                if (Is(i, "MATERIALIZED"))
                {
                    i++;
                }

                if (!IsSymbol(i, "(") || !SkipGroup(ref i))
                {
                    return false;
                }

                if (!IsSymbol(i, ","))
                {
                    return true;
                }

                i++;
            }
        }

        private ImmutableArray<ResultColumn>? ReadSelect(int i)
        {
            if (!SkipSelectModifiers(ref i))
            {
                return null;
            }

            var columns = ReadList(ref i);
            if (columns is null)
            {
                return null;
            }

            // INTO stores the rows instead of returning them, and FOR XML or FOR JSON returns one
            // generated column. A T-SQL statement after this one without a semicolon is scanned too,
            // which can only cost coverage.
            for (var depth = 0; i < _tokens.Count; i++)
            {
                var token = _tokens[i];
                depth += DepthChange(token);
                if (depth == 0 && token.IsSymbol(";"))
                {
                    break;
                }

                if (depth == 0 && (token.Is("INTO") ||
                    (token.Is("FOR") && (Is(i + 1, "XML") || Is(i + 1, "JSON") || Is(i + 1, "BROWSE")))))
                {
                    return null;
                }
            }

            return columns;
        }

        private ImmutableArray<ResultColumn>? ReadReturnedRows(int i)
        {
            var keyword = _dialect switch
            {
                SqlResultDialect.SqlServer => "OUTPUT",
                SqlResultDialect.PostgreSql or SqlResultDialect.Sqlite or SqlResultDialect.MariaDb => "RETURNING",
                _ => null,
            };
            if (keyword is null)
            {
                return null;
            }

            for (var depth = 0; i < _tokens.Count; i++)
            {
                var token = _tokens[i];
                if (token.IsOpen)
                {
                    depth++;
                }
                else if (token.IsClose)
                {
                    depth--;
                }
                else if (depth == 0)
                {
                    if (token.Is(keyword))
                    {
                        var listStart = i + 1;
                        return ReadList(ref listStart);
                    }

                    if (token.IsSymbol(";") || (_dialect == SqlResultDialect.SqlServer && _outputScanStops.Contains(token.Value) && token.Kind == TokenKind.Word))
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        private bool SkipSelectModifiers(ref int i)
        {
            while (i < _tokens.Count)
            {
                if (Is(i, "ALL") || Is(i, "DISTINCTROW") || (_dialect == SqlResultDialect.Oracle && Is(i, "UNIQUE")) ||
                    (_dialect is SqlResultDialect.MySql or SqlResultDialect.MariaDb && _tokens[i].Kind == TokenKind.Word && _mySqlSelectModifiers.Contains(_tokens[i].Value)))
                {
                    i++;
                }
                else if (Is(i, "DISTINCT"))
                {
                    i++;
                    if (_dialect == SqlResultDialect.PostgreSql && Is(i, "ON"))
                    {
                        i++;
                        if (!IsSymbol(i, "(") || !SkipGroup(ref i))
                        {
                            return false;
                        }
                    }
                }
                else if (_dialect == SqlResultDialect.SqlServer && Is(i, "TOP"))
                {
                    i++;
                    if (IsSymbol(i, "("))
                    {
                        if (!SkipGroup(ref i))
                        {
                            return false;
                        }
                    }
                    else if (i < _tokens.Count && _tokens[i].Kind is TokenKind.Number or TokenKind.Parameter)
                    {
                        i++;
                    }
                    else
                    {
                        return false;
                    }

                    if (Is(i, "PERCENT"))
                    {
                        i++;
                    }

                    if (Is(i, "WITH") && Is(i + 1, "TIES"))
                    {
                        i += 2;
                    }
                }
                else
                {
                    return true;
                }
            }

            return true;
        }

        /// <summary>Reads a comma-separated result list up to the clause or statement that ends it.</summary>
        private ImmutableArray<ResultColumn>? ReadList(ref int i)
        {
            var columns = ImmutableArray.CreateBuilder<ResultColumn>();
            var itemStart = i;
            var depth = 0;
            var caseDepth = 0;
            while (true)
            {
                var atEnd = i >= _tokens.Count;
                var token = atEnd ? default : _tokens[i];

                // A word after a dot is a name, such as t.window, even when it is also a keyword. GROUP
                // after WITHIN belongs to an aggregate, and FROM after IS [NOT] DISTINCT to a comparison.
                if (!atEnd && i > 0 && ((token.IsName && _tokens[i - 1].IsSymbol(".")) || (token.Is("GROUP") && _tokens[i - 1].Is("WITHIN")) ||
                    (token.Is("FROM") && i > 1 && _tokens[i - 1].Is("DISTINCT") && (_tokens[i - 2].Is("IS") || _tokens[i - 2].Is("NOT")))))
                {
                    i++;
                    continue;
                }

                if (atEnd)
                {
                    // Nothing to track at the end of the text.
                }
                else if (token.IsOpen)
                {
                    depth++;
                }
                else if (token.IsClose)
                {
                    depth--;
                }
                else if (depth == 0 && token.Is("CASE"))
                {
                    caseDepth++;
                }
                else if (depth == 0 && token.Is("END") && --caseDepth < 0)
                {
                    return null;
                }

                var topLevel = depth == 0 && caseDepth == 0;
                if (!atEnd && topLevel && token.Is("INTO"))
                {
                    return null;
                }

                if (topLevel && token.Kind == TokenKind.Word && _ambiguousListEnds.Contains(token.Value))
                {
                    return null;
                }

                var ends = atEnd || depth < 0 || (topLevel && (token.IsSymbol(";") || EndsList(token)));
                if (ends || (topLevel && token.IsSymbol(",")))
                {
                    if (caseDepth != 0 || i == itemStart)
                    {
                        return null;
                    }

                    var column = ReadColumn(itemStart, i);
                    if (column is null)
                    {
                        return null;
                    }

                    columns.Add(column.Value);
                    if (ends)
                    {
                        return columns.ToImmutable();
                    }

                    itemStart = i + 1;
                }

                i++;
            }
        }

        /// <summary>Names one result expression, the tokens from <paramref name="start"/> up to <paramref name="end"/>.</summary>
        private ResultColumn? ReadColumn(int start, int end)
        {
            var count = end - start;
            var asIndex = -1;
            for (int k = start, depth = 0; k < end; k++)
            {
                var token = _tokens[k];
                depth += DepthChange(token);
                if (depth != 0)
                {
                    continue;
                }

                // A star expands to columns the SQL does not name. COLLATE and the SQL Server identity
                // keywords can keep the name of the column they wrap.
                // SQLite also reads a string after a dot, as in t.'Count', as a column name.
                if ((token.IsSymbol("*") && (k == start || _tokens[k - 1].IsSymbol("."))) || token.Is("COLLATE") ||
                    (_dialect == SqlResultDialect.SqlServer && (token.Is("IDENTITYCOL") || token.Is("ROWGUIDCOL"))) ||
                    (_dialect == SqlResultDialect.Sqlite && token.Kind == TokenKind.String && k > start && _tokens[k - 1].IsSymbol(".")))
                {
                    return null;
                }

                if (token.Is("AS"))
                {
                    asIndex = k;
                }
            }

            var first = _tokens[start];
            var last = _tokens[end - 1];

            // SELECT @variable = expression assigns the variable and returns no rows.
            if (_dialect == SqlResultDialect.SqlServer && first.Kind == TokenKind.Parameter && count > 1 &&
                (IsSymbol(start + 1, "=") || IsSymbol(start + 2, "=")))
            {
                return null;
            }

            if (asIndex >= 0)
            {
                if (asIndex == start || asIndex != end - 2)
                {
                    return null;
                }

                if (last.IsName || (last.Kind == TokenKind.String && _dialect is not (SqlResultDialect.PostgreSql or SqlResultDialect.Oracle)))
                {
                    return Named(last.Value, start, asIndex);
                }

                return null;
            }

            // T-SQL also accepts "alias = expression".
            if (_dialect == SqlResultDialect.SqlServer && count > 2 && (first.IsName || first.Kind == TokenKind.String) &&
                IsSymbol(start + 1, "="))
            {
                return Named(first.Value, start + 2, end);
            }

            if (NameChainEnd(start, end) == end)
            {
                if (count == 1 && (last.Is("NULL") ||
                    (_dialect == SqlResultDialect.SqlServer && last.Kind == TokenKind.Word && _sqlServerNiladicFunctions.Contains(last.Value)) ||
                    (_dialect != SqlResultDialect.SqlServer && (last.Is("TRUE") || last.Is("FALSE")))))
                {
                    return Expression(start, end);
                }

                // SQLite names rowid, oid and _rowid_ after the table's INTEGER PRIMARY KEY column.
                if (_dialect == SqlResultDialect.Sqlite &&
                    (last.Value.Equals("ROWID", StringComparison.OrdinalIgnoreCase) || last.Value.Equals("OID", StringComparison.OrdinalIgnoreCase) ||
                     last.Value.Equals("_ROWID_", StringComparison.OrdinalIgnoreCase)))
                {
                    return new ResultColumn(ResultColumnKind.Unknown, string.Empty, Text(start, end));
                }

                return Named(last.Value, start, end);
            }

            if (count == 1)
            {
                return Expression(start, end);
            }

            var previous = _tokens[end - 2];
            if (IsAliasCandidate(last) && !previous.IsSymbol("."))
            {
                // SQL Server's x AT TIME ZONE tz ends with an operand, not an alias.
                if (HasTimeZone(start, end))
                {
                    return new ResultColumn(ResultColumnKind.Unknown, string.Empty, Text(start, end));
                }

                // Outside SQL Server, one bare word can be a prefix operator, such as MySQL's BINARY a or
                // Oracle's PRIOR x, whose column the provider names with its text.
                if (_dialect != SqlResultDialect.SqlServer && end - 1 - start == 1 && _tokens[start].Kind == TokenKind.Word)
                {
                    return new ResultColumn(ResultColumnKind.Unknown, string.Empty, Text(start, end));
                }

                if (IsPrimary(start, end - 1))
                {
                    return Named(last.Value, start, end - 1);
                }

                if (EndsOperand(previous))
                {
                    // Two operands in a row: the second is an alias. Other dialects have multi-word type
                    // names and postfix words, so only SQL Server's grammar makes that certain.
                    return _dialect == SqlResultDialect.SqlServer
                        ? Named(last.Value, start, end - 1)
                        : new ResultColumn(ResultColumnKind.Unknown, string.Empty, Text(start, end));
                }
            }

            // A string after an operand can be an alias in SQL Server and MySQL, or part of a literal.
            if (last.Kind == TokenKind.String && EndsOperand(previous))
            {
                return new ResultColumn(ResultColumnKind.Unknown, string.Empty, Text(start, end));
            }

            return Expression(start, end);
        }

        private bool HasTimeZone(int start, int end)
        {
            for (var k = start; k + 2 < end; k++)
            {
                if (_tokens[k].Is("AT") && _tokens[k + 1].Is("TIME") && _tokens[k + 2].Is("ZONE"))
                {
                    return true;
                }
            }

            return false;
        }

        private bool EndsList(Token token)
            => token.Kind == TokenKind.Word && (_listTerminators.Contains(token.Value) ||
                (_dialect == SqlResultDialect.SqlServer && _sqlServerStatementWords.Contains(token.Value)));

        private ResultColumn Named(string name, int start, int end)
        {
            // PostgreSQL truncates a longer name to 63 bytes, by default, instead of rejecting it.
            if (_dialect == SqlResultDialect.PostgreSql && System.Text.Encoding.UTF8.GetByteCount(name) > 63)
            {
                return new ResultColumn(ResultColumnKind.Unknown, string.Empty, Text(start, end));
            }

            return new ResultColumn(ResultColumnKind.Named, name, Text(start, end));
        }

        /// <summary>Names an expression that has no alias, by the rules of the dialect.</summary>
        private ResultColumn Expression(int start, int end)
        {
            var text = Text(start, end);

            // A parenthesized column reference may keep the column's name.
            var innerStart = start;
            var innerEnd = end;
            while (IsSymbol(innerStart, "(") && GroupEnd(innerStart) == innerEnd)
            {
                innerStart++;
                innerEnd--;
            }

            if (innerStart != start && innerEnd > innerStart && NameChainEnd(innerStart, innerEnd) == innerEnd)
            {
                return new ResultColumn(ResultColumnKind.Unknown, string.Empty, text);
            }

            return _dialect switch
            {
                SqlResultDialect.SqlServer => new ResultColumn(ResultColumnKind.Unnamed, string.Empty, text),

                // PostgreSQL names an expression after its function, type or column, such as count.
                SqlResultDialect.PostgreSql => new ResultColumn(ResultColumnKind.Unknown, string.Empty, text),

                // MySQL, MariaDB, Oracle and SQLite name it with its text. One token, such as a literal
                // or a parameter, can read as a plain name.
                _ => end - start == 1
                    ? new ResultColumn(ResultColumnKind.Unknown, string.Empty, text)
                    : new ResultColumn(ResultColumnKind.Opaque, string.Empty, text),
            };
        }

        /// <summary>
        /// A single operand: a literal, a parameter, a column reference, a parenthesized group, a
        /// CASE expression, or a function call with an optional OVER, FILTER or WITHIN GROUP.
        /// </summary>
        private bool IsPrimary(int start, int end)
        {
            if (end - start == 1)
            {
                var token = _tokens[start];
                return token.Kind != TokenKind.Symbol && !IsOperatorWord(token);
            }

            if (NameChainEnd(start, end) == end || (IsSymbol(start, "(") && GroupEnd(start) == end))
            {
                return true;
            }

            if (Is(start, "CASE"))
            {
                return CaseEnd(start) == end;
            }

            var k = NameChainEnd(start, end);
            if (k == start || !IsSymbol(k, "("))
            {
                return false;
            }

            k = GroupEnd(k);
            while (k > 0 && k < end)
            {
                if ((Is(k, "OVER") || Is(k, "FILTER")) && IsSymbol(k + 1, "("))
                {
                    k = GroupEnd(k + 1);
                }
                else if (Is(k, "WITHIN") && Is(k + 1, "GROUP") && IsSymbol(k + 2, "("))
                {
                    k = GroupEnd(k + 2);
                }
                else
                {
                    return false;
                }
            }

            return k == end;
        }

        private bool IsAliasCandidate(Token token)
            => token.Kind == TokenKind.Quoted ||
                (token.Kind == TokenKind.Word && !token.Is("END") && !token.Is("NULL") && !IsOperatorWord(token) &&
                 !(_dialect is SqlResultDialect.PostgreSql or SqlResultDialect.Sqlite && (token.Is("ISNULL") || token.Is("NOTNULL"))));

        private bool EndsOperand(Token token)
            => token.Kind switch
            {
                TokenKind.Quoted or TokenKind.String or TokenKind.Number or TokenKind.Parameter => true,
                TokenKind.Word => !IsOperatorWord(token),
                _ => token.IsClose,
            };

        // ISNULL and NOTNULL are postfix operators in PostgreSQL and SQLite, and ILIKE is a PostgreSQL
        // operator. Elsewhere they are ordinary names, so they can be aliases.
        private bool IsOperatorWord(Token token)
            => token.Kind == TokenKind.Word &&
                (_operatorWords.Contains(token.Value) || (_dialect == SqlResultDialect.PostgreSql && token.Is("ILIKE")));

        /// <summary>The index after a dotted name such as <c>a.[b].c</c> that starts at <paramref name="start"/>.</summary>
        private int NameChainEnd(int start, int end)
        {
            if (start >= end || !_tokens[start].IsName || IsOperatorWord(_tokens[start]))
            {
                return start;
            }

            var k = start + 1;
            while (k + 1 < end && _tokens[k].IsSymbol("."))
            {
                // T-SQL leaves out the default schema with two dots, as in master..spt_values.name.
                var next = k + 1;
                if (_dialect == SqlResultDialect.SqlServer && _tokens[next].IsSymbol(".") && next + 1 < end)
                {
                    next++;
                }

                if (!_tokens[next].IsName)
                {
                    break;
                }

                k = next + 1;
            }

            return k;
        }

        /// <summary>The index after the group that opens at <paramref name="open"/>, or -1 when it does not close.</summary>
        private int GroupEnd(int open)
        {
            var k = open;
            return SkipGroup(ref k) ? k : -1;
        }

        private bool SkipGroup(ref int i)
        {
            var depth = 0;
            while (i < _tokens.Count)
            {
                var token = _tokens[i++];
                if (token.IsOpen)
                {
                    depth++;
                }
                else if (token.IsClose && --depth == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int DepthChange(Token token)
        {
            if (token.IsOpen)
            {
                return 1;
            }

            return token.IsClose ? -1 : 0;
        }

        private int CaseEnd(int start)
        {
            var depth = 0;
            for (var k = start; k < _tokens.Count; k++)
            {
                if (_tokens[k].Is("CASE"))
                {
                    depth++;
                }
                else if (_tokens[k].Is("END") && --depth == 0)
                {
                    return k + 1;
                }
            }

            return -1;
        }

        private string Text(int start, int end)
            => end <= start ? string.Empty : _sql.Substring(_tokens[start].Start, _tokens[end - 1].End - _tokens[start].Start);

        private bool Is(int i, string keyword) => i < _tokens.Count && _tokens[i].Is(keyword);

        private bool IsSymbol(int i, string symbol) => i < _tokens.Count && _tokens[i].IsSymbol(symbol);
    }
}
