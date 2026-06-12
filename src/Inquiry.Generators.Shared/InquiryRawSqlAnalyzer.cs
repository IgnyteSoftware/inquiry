using Inquiry.Generators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;

namespace Inquiry.Generators;

/// <summary>
/// Flags <c>new InquiryCommand(text, …)</c> where <c>text</c> is not a compile-time constant
/// (INQ048, warning). The raw-string constructor is the documented advanced escape hatch — the
/// safe paths are the <c>FormattableString</c> overloads / <c>InquirySql.Sql($"…")</c>, which turn
/// every interpolation hole into a bound parameter. Ships in each provider's analyzer assembly
/// alongside the source generator.
/// </summary>
/// <remarks>
/// Generated code is excluded (<see cref="GeneratedCodeAnalysisFlags.None"/>): generated batch
/// helpers legitimately build command text at runtime from compile-time fragments, and every
/// value still binds through parameters there.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InquiryRawSqlAnalyzer : DiagnosticAnalyzer
{
    private const string InquiryCommandFullName = "Inquiry.Commands.InquiryCommand";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(InquiryDiagnosticDescriptors.NonConstantRawSql);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type?.ToDisplayString() != InquiryCommandFullName)
        {
            return;
        }

        foreach (var argument in creation.Arguments)
        {
            // Every InquiryCommand constructor takes the command text as its single string
            // parameter ("commandText"); other parameters are command type/timeout/binders.
            if (argument.Parameter is not { Type.SpecialType: SpecialType.System_String })
            {
                continue;
            }

            // Constants cover literals, const fields/locals, nameof, and concatenations of
            // constants — everything whose text is fixed at compile time.
            if (!argument.Value.ConstantValue.HasValue)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InquiryDiagnosticDescriptors.NonConstantRawSql,
                    argument.Value.Syntax.GetLocation()));
            }

            return;
        }
    }
}
