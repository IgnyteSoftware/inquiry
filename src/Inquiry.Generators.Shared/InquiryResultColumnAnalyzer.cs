using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Inquiry.Generators;

/// <summary>
/// Checks handwritten SQL that <c>IInquiry</c> or <c>IInquiryTransaction</c> reads into a type with a
/// generated result shape (INQ120, warning). The generated materializer maps result columns by name and
/// throws on the first read when a column is missing or matched twice, so this reports the same mistakes
/// at build time. Ships in each provider's analyzer assembly alongside the source generator.
/// </summary>
/// <remarks>
/// Only SQL text that is fixed at compile time is checked: interpolated and raw string literals,
/// <c>InquirySql.Sql</c>, <c>new InquiryCommand(constant)</c>, <c>FormattableStringFactory.Create</c>
/// with a constant format, and a method in the same file whose body only returns one of these. Any
/// result list that <see cref="SqlResultColumnReader"/> does not recognize reports nothing.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InquiryResultColumnAnalyzer : DiagnosticAnalyzer
{
    private const string DialectAttributeFullName = "Inquiry.InquiryDialectAttribute";
    private const int MaxHelperDepth = 2;
    private const int CommandTypeText = 1;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(InquiryDiagnosticDescriptors.ResultColumnMismatch);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var dialect = ResolveDialect(start.Compilation);
            var symbols = KnownTypes.Create(start.Compilation);
            if (dialect is null || symbols is null)
            {
                return;
            }

            var shapes = new ConcurrentDictionary<INamedTypeSymbol, ImmutableArray<ColumnData>?>(SymbolEqualityComparer.Default);
            start.RegisterOperationAction(
                operation => AnalyzeInvocation(operation, dialect.Value, symbols, shapes),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        SqlResultDialect dialect,
        KnownTypes symbols,
        ConcurrentDictionary<INamedTypeSymbol, ImmutableArray<ColumnData>?> shapes)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name is not ("QueryAsync" or "QueryListAsync" or "QuerySingleOrDefaultAsync") ||
            method.TypeArguments.Length != 1 ||
            !(SymbolEqualityComparer.Default.Equals(method.ContainingType, symbols.Inquiry) ||
              SymbolEqualityComparer.Default.Equals(method.ContainingType, symbols.Transaction)) ||
            method.TypeArguments[0] is not INamedTypeSymbol resultType)
        {
            return;
        }

        var argument = FirstArgument(invocation.Arguments);
        if (argument is null)
        {
            return;
        }

        var sql = ReadSqlText(argument.Value, symbols, 0, context.CancellationToken);
        if (sql is null)
        {
            return;
        }

        var compilation = context.Compilation;
        var cancellationToken = context.CancellationToken;
        var columns = shapes.GetOrAdd(resultType, type => ResultShape(type, compilation, cancellationToken));
        if (columns is null)
        {
            return;
        }

        var returned = SqlResultColumnReader.Read(sql, dialect);
        if (returned is null)
        {
            return;
        }

        Report(context, argument.Syntax.GetLocation(), resultType, columns.Value, returned.Value);
    }

    private static void Report(
        OperationAnalysisContext context,
        Location location,
        INamedTypeSymbol resultType,
        ImmutableArray<ColumnData> columns,
        ImmutableArray<ResultColumn> returned)
    {
        var anyUnknown = returned.Any(static column => column.Kind == ResultColumnKind.Unknown);
        var anyOpaque = returned.Any(static column => column.Kind == ResultColumnKind.Opaque);
        string? description = null;
        foreach (var column in columns)
        {
            var matches = returned.Count(returnedColumn => returnedColumn.Kind == ResultColumnKind.Named &&
                string.Equals(returnedColumn.Name, column.ColumnName, StringComparison.OrdinalIgnoreCase));
            string detail;
            if (matches > 1)
            {
                detail = string.Format(CultureInfo.InvariantCulture,
                    "has {0} columns named '{1}' for property '{2}'", matches, column.ColumnName, column.PropertyName);
            }
            else if (matches == 0 && !anyUnknown && !(anyOpaque && !IsPlainIdentifier(column.ColumnName)))
            {
                // An opaque name is expression text, which always holds a character that a plain
                // identifier cannot, so it can only match a column name that is not one.
                detail = string.Format(CultureInfo.InvariantCulture,
                    "has no column named '{0}' for property '{1}'", column.ColumnName, column.PropertyName);
            }
            else
            {
                continue;
            }

            description ??= Describe(returned);
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.ResultColumnMismatch,
                location,
                resultType.ToDisplayString(),
                detail,
                description));
        }
    }

    private static IArgumentOperation? FirstArgument(ImmutableArray<IArgumentOperation> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter?.Ordinal == 0)
            {
                return argument;
            }
        }

        return null;
    }

    /// <summary>Returns the SQL text an operation evaluates to, with each bound value as a parameter, or null.</summary>
    private static string? ReadSqlText(IOperation operation, KnownTypes symbols, int depth, CancellationToken cancellationToken)
    {
        while (operation is IConversionOperation { OperatorMethod: null } conversion)
        {
            operation = conversion.Operand;
        }

        switch (operation)
        {
            case IInterpolatedStringOperation interpolated:
                return ReadInterpolated(interpolated);

            case IObjectCreationOperation creation when SymbolEqualityComparer.Default.Equals(creation.Type, symbols.Command):
                if (!IsTextCommand(creation.Arguments))
                {
                    return null;
                }

                // The command text is raw SQL: braces in it are literal, not format items.
                return FirstArgument(creation.Arguments)?.Value.ConstantValue is { HasValue: true, Value: string text } ? text : null;

            case IInvocationOperation invocation:
                return ReadInvocation(invocation, symbols, depth, cancellationToken);

            default:
                return null;
        }
    }

    private static string? ReadInvocation(IInvocationOperation invocation, KnownTypes symbols, int depth, CancellationToken cancellationToken)
    {
        var method = invocation.TargetMethod;
        var first = FirstArgument(invocation.Arguments);
        if (method.Name == "Create" && SymbolEqualityComparer.Default.Equals(method.ContainingType, symbols.FormattableStringFactory))
        {
            return first?.Value.ConstantValue is { HasValue: true, Value: string format } ? ReadCompositeFormat(format) : null;
        }

        if (method.Name == "Sql" && SymbolEqualityComparer.Default.Equals(method.ContainingType, symbols.InquirySql))
        {
            return first is null || !IsTextCommand(invocation.Arguments)
                ? null
                : ReadSqlText(first.Value, symbols, depth, cancellationToken);
        }

        var body = HelperBody(method, invocation, symbols, depth);
        var returned = body is null ? null : invocation.SemanticModel?.GetOperation(body, cancellationToken);
        return returned is null ? null : ReadSqlText(returned, symbols, depth + 1, cancellationToken);
    }

    /// <summary>
    /// The returned expression of a helper method, such as <c>static FormattableString Sql(int id) =&gt; $"…";</c>.
    /// The method must be declared once, in the same file, and must not be overridable, so the body that
    /// runs is the one read here.
    /// </summary>
    private static ExpressionSyntax? HelperBody(IMethodSymbol method, IInvocationOperation invocation, KnownTypes symbols, int depth)
    {
        if (depth >= MaxHelperDepth || method.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction) ||
            method.IsVirtual || method.IsAbstract || method.IsOverride || method.IsExtern || method.IsPartialDefinition ||
            !(SymbolEqualityComparer.Default.Equals(method.ReturnType, symbols.FormattableString) ||
              SymbolEqualityComparer.Default.Equals(method.ReturnType, symbols.Command)) ||
            method.DeclaringSyntaxReferences.Length != 1 ||
            method.DeclaringSyntaxReferences[0].SyntaxTree != invocation.Syntax.SyntaxTree)
        {
            return null;
        }

        var declaration = method.DeclaringSyntaxReferences[0].GetSyntax();
        var (expressionBody, block) = declaration switch
        {
            MethodDeclarationSyntax declared => (declared.ExpressionBody, declared.Body),
            LocalFunctionStatementSyntax local => (local.ExpressionBody, local.Body),
            _ => (null, null),
        };

        if (expressionBody is not null)
        {
            return expressionBody.Expression;
        }

        return block is { Statements.Count: 1 } && block.Statements[0] is ReturnStatementSyntax { Expression: { } returned }
            ? returned
            : null;
    }

    /// <summary>A command runs as text unless a command type other than <c>Text</c> is passed.</summary>
    private static bool IsTextCommand(ImmutableArray<IArgumentOperation> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter?.Name != "commandType" || argument.ArgumentKind == ArgumentKind.DefaultValue)
            {
                continue;
            }

            var value = argument.Value;
            while (value is IConversionOperation { OperatorMethod: null } conversion && !value.ConstantValue.HasValue)
            {
                value = conversion.Operand;
            }

            if (value.ConstantValue is not { HasValue: true, Value: null or CommandTypeText })
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadInterpolated(IInterpolatedStringOperation interpolated)
    {
        var builder = new StringBuilder();
        var hole = 0;
        foreach (var part in interpolated.Parts)
        {
            switch (part)
            {
                case IInterpolatedStringTextOperation { Text.ConstantValue: { HasValue: true, Value: string text } }:
                    builder.Append(text);
                    break;
                case IInterpolationOperation:
                    // InquirySql.Sql binds every hole as a parameter, so a hole never adds SQL text.
                    builder.Append("@p").Append(hole++.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    return null;
            }
        }

        return builder.ToString();
    }

    /// <summary>Replaces each composite-format item such as <c>{0}</c> with a parameter, as InquirySql.Sql does.</summary>
    private static string? ReadCompositeFormat(string format)
    {
        var builder = new StringBuilder(format.Length);
        var i = 0;
        while (i < format.Length)
        {
            var c = format[i];
            var next = i + 1 < format.Length ? format[i + 1] : '\0';
            if ((c == '{' && next == '{') || (c == '}' && next == '}'))
            {
                builder.Append(c);
                i += 2;
            }
            else if (c == '{')
            {
                var close = format.IndexOf('}', i);
                var digits = i + 1;
                while (digits < format.Length && char.IsDigit(format[digits]))
                {
                    digits++;
                }

                if (close < 0 || digits == i + 1 || (digits != close && format[digits] is not (',' or ':')))
                {
                    return null;
                }

                builder.Append("@p").Append(format, i + 1, digits - i - 1);
                i = close + 1;
            }
            else if (c == '}')
            {
                return null;
            }
            else
            {
                builder.Append(c);
                i++;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The columns of the generated result shape of <paramref name="type"/>, or null when the generator does
    /// not give it one. Only types declared in this compilation are checked: a referenced assembly's shape
    /// came from the generator that built it.
    /// </summary>
    private static ImmutableArray<ColumnData>? ResultShape(INamedTypeSymbol type, Compilation compilation, CancellationToken cancellationToken)
    {
        if (type.IsGenericType || type.TypeKind != TypeKind.Class || type.DeclaringSyntaxReferences.IsEmpty)
        {
            return null;
        }

        // The generator reads [InquiryTable] on classes only; the other attributes on classes and records.
        var table = !type.IsRecord && GeneratorHelpers.GetEntityAttribute(type, "InquiryTableAttribute") is not null;
        var view = GeneratorHelpers.GetEntityAttribute(type, "InquiryViewAttribute") is not null;
        var projection = GeneratorHelpers.GetEntityAttribute(type, "InquiryProjectionAttribute") is not null;
        var adHoc = GeneratorHelpers.GetEntityAttribute(type, "InquiryAdHocAttribute") is not null;
        if ((table ? 1 : 0) + (view ? 1 : 0) + (projection ? 1 : 0) + (adHoc ? 1 : 0) != 1)
        {
            return null;
        }

        if (adHoc)
        {
            var data = AdHocProcessor.Extract(type, cancellationToken);
            return data.IsMapped ? data.Columns.AsImmutableArray() : null;
        }

        if (projection)
        {
            var data = ProjectionProcessor.Extract(type, compilation, cancellationToken);
            return data.IsMapped ? data.Columns.AsImmutableArray() : null;
        }

        var entity = table
            ? EntityProcessor.Extract(type, compilation, cancellationToken)
            : EntityProcessor.ExtractView(type, compilation, cancellationToken);
        return entity.IsMapped ? entity.Columns.AsImmutableArray() : null;
    }

    /// <summary>
    /// The dialect the generator emits for: the compilation's own <c>[InquiryDialect]</c>, or else the one
    /// referenced provider. Null when that is unknown or ambiguous, because unnamed columns differ by dialect.
    /// </summary>
    private static SqlResultDialect? ResolveDialect(Compilation compilation)
    {
        var own = DialectNames(compilation.Assembly).Distinct(StringComparer.Ordinal).ToArray();
        var names = own.Length > 0
            ? own
            : compilation.SourceModule.ReferencedAssemblySymbols
                .SelectMany(DialectNames)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        if (names.Length != 1)
        {
            return null;
        }

        return names[0] switch
        {
            "SqlServer" => SqlResultDialect.SqlServer,
            "PostgreSql" => SqlResultDialect.PostgreSql,
            "MySql" => SqlResultDialect.MySql,
            "MariaDb" => SqlResultDialect.MariaDb,
            "Oracle" => SqlResultDialect.Oracle,
            "Sqlite" => SqlResultDialect.Sqlite,
            _ => null,
        };
    }

    private static IEnumerable<string> DialectNames(IAssemblySymbol assembly)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == DialectAttributeFullName && attribute.ConstructorArguments.Length > 0)
            {
                yield return attribute.ConstructorArguments[0].Value as string ?? string.Empty;
            }
        }
    }

    private static string Describe(ImmutableArray<ResultColumn> returned)
    {
        var builder = new StringBuilder();
        foreach (var column in returned)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            switch (column.Kind)
            {
                case ResultColumnKind.Named:
                    builder.Append(column.Name);
                    break;
                case ResultColumnKind.Unnamed:
                    builder.Append("(unnamed) ").Append(Abbreviate(column.Text));
                    break;
                default:
                    builder.Append(Abbreviate(column.Text));
                    break;
            }
        }

        return builder.ToString();
    }

    private static string Abbreviate(string text)
    {
        const int maxLength = 40;
        var collapsed = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= maxLength ? collapsed : collapsed.Substring(0, maxLength) + "...";
    }

    private static bool IsPlainIdentifier(string name)
    {
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class KnownTypes
    {
        private KnownTypes(
            INamedTypeSymbol? inquiry,
            INamedTypeSymbol? transaction,
            INamedTypeSymbol? command,
            INamedTypeSymbol? inquirySql,
            INamedTypeSymbol? formattableString,
            INamedTypeSymbol? formattableStringFactory)
        {
            Inquiry = inquiry;
            Transaction = transaction;
            Command = command;
            InquirySql = inquirySql;
            FormattableString = formattableString;
            FormattableStringFactory = formattableStringFactory;
        }

        public INamedTypeSymbol? Inquiry { get; }

        public INamedTypeSymbol? Transaction { get; }

        public INamedTypeSymbol? Command { get; }

        public INamedTypeSymbol? InquirySql { get; }

        public INamedTypeSymbol? FormattableString { get; }

        public INamedTypeSymbol? FormattableStringFactory { get; }

        public static KnownTypes? Create(Compilation compilation)
        {
            var inquiry = compilation.GetTypeByMetadataName("Inquiry.IInquiry");
            var transaction = compilation.GetTypeByMetadataName("Inquiry.Transactions.IInquiryTransaction");
            if (inquiry is null && transaction is null)
            {
                return null;
            }

            return new KnownTypes(
                inquiry,
                transaction,
                compilation.GetTypeByMetadataName("Inquiry.Commands.InquiryCommand"),
                compilation.GetTypeByMetadataName("Inquiry.InquirySql"),
                compilation.GetTypeByMetadataName("System.FormattableString"),
                compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.FormattableStringFactory"));
        }
    }
}
