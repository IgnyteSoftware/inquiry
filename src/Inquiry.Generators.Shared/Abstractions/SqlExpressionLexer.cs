using System.Collections.Generic;
using System.Text;

namespace Inquiry.Generators.Abstractions;

public enum SqlExpressionCommentPolicy { Standard, MySql, MariaDb }

public sealed record SqlExpressionAnalysis(string RenderedExpression, IReadOnlyList<string> Failures, bool HasConcatenationOperator)
{
    public bool HasComment { get; init; }
    public IReadOnlyList<string> IdentifierTokens { get; init; } = System.Array.Empty<string>();
    public IReadOnlyList<string> ValueIdentifierTokens { get; init; } = System.Array.Empty<string>();
    public bool HasTopLevelComma { get; init; }
    public bool HasUserVariableAssignment { get; init; }
    public bool HasUserVariableReference { get; init; }
}

public static class SqlExpressionLexer
{
    public static SqlExpressionAnalysis Analyze(string expression, SqlExpressionCommentPolicy policy, bool translateConcatenation)
    {
        var failures = new List<string>();
        var output = translateConcatenation ? new StringBuilder(expression.Length) : null;
        var state = State.Normal;
        var depth = 0;
        var hasPipes = false;
        var hasComment = false;
        var identifierTokens = new List<string>();
        var valueIdentifierTokens = new List<string>();
        var hasTopLevelComma = false;
        var hasUserVariableAssignment = false;
        var hasUserVariableReference = false;
        StringBuilder? quotedIdentifier = null;
        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];
            var next = i + 1 < expression.Length ? expression[i + 1] : '\0';
            if (c == '\0' || (char.IsControl(c) && c is not '\r' and not '\n' and not '\t')) Add("contains a NUL or disallowed control character");
            switch (state)
            {
                case State.Normal:
                    if (c == '\'' || c == '"' || c == '`' || c == '[')
                    {
                        state = c == '\'' ? State.Single : c == '"' ? State.Double : c == '`' ? State.Backtick : State.Bracket;
                        if (state is State.Backtick or State.Bracket) quotedIdentifier = new StringBuilder();
                        Append(c);
                    }
                    else if (c == '/' && next == '*')
                    {
                        hasComment = true;
                        if ((i + 2 < expression.Length && expression[i + 2] == '!')
                            || (i + 3 < expression.Length && expression[i + 2] is 'M' or 'm' && expression[i + 3] == '!'))
                            Add("contains an executable comment");
                        state = State.Block; Append(c); Append(next); i++;
                    }
                    else if (IsLineComment(expression, i, policy, out var markerLength))
                    {
                        hasComment = true;
                        state = State.Line; for (var n = 0; n < markerLength; n++) Append(expression[i + n]); i += markerLength - 1;
                    }
                    else if (c == '(') { depth++; Append(c); }
                    else if (c == ')') { if (depth == 0) Add("contains an unmatched closing parenthesis"); else depth--; Append(c); }
                    else if (c == ';') { Add("contains a top-level statement separator"); Append(c); }
                    else if (c == ',' && depth == 0) { hasTopLevelComma = true; Append(c); }
                    else if (c == ':' && next == '=')
                    {
                        hasUserVariableAssignment = true; Append(c); Append(next); i++;
                    }
                    else if (c == '@')
                    {
                        hasUserVariableReference = true; Append(c);
                    }
                    else if (c == '|' && next == '|')
                    {
                        hasPipes = true; if (translateConcatenation) output!.Append('+'); else { Append(c); Append(next); } i++;
                    }
                    else if (IsIdentifierStart(c))
                    {
                        var start = i;
                        while (i + 1 < expression.Length && IsIdentifierPart(expression[i + 1])) i++;
                        var token = expression.Substring(start, i - start + 1);
                        identifierTokens.Add(token);
                        if (!IsCallableIdentifier(expression, i + 1)) valueIdentifierTokens.Add(token);
                        if (token.Equals("select", System.StringComparison.OrdinalIgnoreCase)
                            || token.Equals("with", System.StringComparison.OrdinalIgnoreCase)) Add("contains a subquery token ('" + token.ToUpperInvariant() + "')");
                        else if (token.Equals("over", System.StringComparison.OrdinalIgnoreCase)) Add("contains a window-function token ('OVER')");
                        if (output is not null) output.Append(token);
                    }
                    else Append(c);
                    break;
                case State.Single: Quoted('\'', State.Single); break;
                case State.Double: Quoted('"', State.Double); break;
                case State.Backtick:
                    Append(c);
                    if (c == '`' && next == '`') { quotedIdentifier!.Append('`'); Append(next); i++; }
                    else if (c == '`')
                    {
                        var token = quotedIdentifier!.ToString();
                        identifierTokens.Add(token);
                        if (!IsCallableIdentifier(expression, i + 1)) valueIdentifierTokens.Add(token);
                        quotedIdentifier = null; state = State.Normal;
                    }
                    else quotedIdentifier!.Append(c);
                    break;
                case State.Bracket:
                    Append(c);
                    if (c == ']' && next == ']') { quotedIdentifier!.Append(']'); Append(next); i++; }
                    else if (c == ']')
                    {
                        var token = quotedIdentifier!.ToString();
                        identifierTokens.Add(token);
                        if (!IsCallableIdentifier(expression, i + 1)) valueIdentifierTokens.Add(token);
                        quotedIdentifier = null; state = State.Normal;
                    }
                    else quotedIdentifier!.Append(c);
                    break;
                case State.Line:
                    Append(c); if (c is '\r' or '\n') state = State.Normal; break;
                case State.Block:
                    if (c == '/' && next == '*') Add("contains a nested block-comment opener");
                    Append(c); if (c == '*' && next == '/') { Append(next); i++; state = State.Normal; } break;
            }

            void Quoted(char quote, State quotedState)
            {
                Append(c);
                if (c == quote && next == quote) { Append(next); i++; }
                else if (c == quote) state = State.Normal;
            }
        }
        if (depth != 0) Add("contains unmatched parentheses");
        if (state == State.Line) Add("ends inside a line comment that would consume generated wrapper SQL");
        else if (state != State.Normal) Add(state == State.Block ? "contains an unterminated block comment" : "contains an unterminated quoted string or identifier");
        return new SqlExpressionAnalysis(output?.ToString() ?? expression, failures, hasPipes)
        {
            HasComment = hasComment,
            IdentifierTokens = identifierTokens,
            ValueIdentifierTokens = valueIdentifierTokens,
            HasTopLevelComma = hasTopLevelComma,
            HasUserVariableAssignment = hasUserVariableAssignment,
            HasUserVariableReference = hasUserVariableReference,
        };

        void Append(char value) => output?.Append(value);
        void Add(string failure) { if (!failures.Contains(failure)) failures.Add(failure); }
    }

    /// <summary>
    /// Validates an expression that will be embedded after <c>SET @variable =</c>. This is narrower
    /// than general DDL default validation: capture expressions must be standalone scalars and may not
    /// depend on a row that does not exist yet.
    /// </summary>
    public static IReadOnlyList<string> ValidateStandaloneScalar(
        string expression,
        SqlExpressionCommentPolicy policy,
        IReadOnlyList<string> mappedIdentifiers)
    {
        var analysis = Analyze(expression, policy, false);
        var failures = new List<string>(analysis.Failures);
        var mapped = new HashSet<string>(mappedIdentifiers, System.StringComparer.OrdinalIgnoreCase);

        if (analysis.HasComment) Add("contains a comment, which is not allowed in a captured scalar expression");
        if (analysis.HasTopLevelComma) Add("contains a top-level comma that would create another SET assignment");
        if (analysis.HasUserVariableAssignment) Add("contains the side-effecting user-variable assignment operator ':='");
        if (analysis.HasUserVariableReference) Add("contains a user-variable reference, which is not allowed in a captured scalar expression");

        for (var i = 0; i < analysis.IdentifierTokens.Count; i++)
        {
            var token = analysis.IdentifierTokens[i];
            if (token.Equals("default", System.StringComparison.OrdinalIgnoreCase))
                Add("contains the DDL-only DEFAULT token");
            if (token.Equals("on", System.StringComparison.OrdinalIgnoreCase) &&
                i + 1 < analysis.IdentifierTokens.Count &&
                analysis.IdentifierTokens[i + 1].Equals("update", System.StringComparison.OrdinalIgnoreCase))
                Add("contains the DDL-only ON UPDATE clause");
            if (token.Equals("auto_increment", System.StringComparison.OrdinalIgnoreCase) ||
                token.Equals("generated", System.StringComparison.OrdinalIgnoreCase))
                Add("contains a DDL-only generation clause");
        }

        foreach (var token in analysis.ValueIdentifierTokens)
        {
            if (mapped.Contains(token)) Add("references mapped column '" + token + "'");
        }

        return failures;

        void Add(string failure) { if (!failures.Contains(failure)) failures.Add(failure); }
    }

    private static bool IsCallableIdentifier(string expression, int index)
    {
        while (index < expression.Length && char.IsWhiteSpace(expression[index])) index++;
        return index < expression.Length && expression[index] == '(';
    }

    private static bool IsLineComment(string value, int index, SqlExpressionCommentPolicy policy, out int length)
    {
        length = 0;
        if ((policy is SqlExpressionCommentPolicy.MySql or SqlExpressionCommentPolicy.MariaDb) && value[index] == '#') { length = 1; return true; }
        if (value[index] != '-' || index + 1 >= value.Length || value[index + 1] != '-') return false;
        if (policy is SqlExpressionCommentPolicy.MySql or SqlExpressionCommentPolicy.MariaDb)
        {
            if (index + 2 >= value.Length || !char.IsWhiteSpace(value[index + 2])) return false;
        }
        length = 2; return true;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
    private enum State { Normal, Single, Double, Backtick, Bracket, Line, Block }
}
