using Inquiry.Generators.Diagnostics;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace Inquiry.Generators.Sql;

/// <summary>
/// Resolves which <see cref="SqlBuilder"/> to use for store SQL generation by inspecting
/// the compilation for <c>[assembly: InquiryDialect("...")]</c> markers.
/// </summary>
/// <remarks>
/// Lookup order:
/// <list type="number">
///   <item>The consuming compilation's own assembly attributes (explicit override).</item>
///   <item>Every referenced assembly's attributes (the official provider packages set this).</item>
/// </list>
/// Resolution rules: zero hits emits <c>INQ013</c>, two-or-more conflicting hits emits <c>INQ014</c>,
/// an unrecognised dialect name emits <c>INQ015</c>. In all error cases the generator skips store
/// SQL emission for this compilation but still emits materializers and registrations.
/// </remarks>
internal static class DialectResolver
{
    private const string AttributeFullName = "Inquiry.InquiryDialectAttribute";

    public static SqlBuilder? Resolve(SourceProductionContext context, Compilation compilation)
    {
        // Explicit override on the consuming compilation wins outright — that's how a project
        // referencing multiple provider packages (or testing harness pulling them all in)
        // disambiguates.
        var ownNames = ReadDialectName(compilation.Assembly).Distinct().ToArray();
        if (ownNames.Length == 1)
        {
            return CreateBuilder(ownNames[0]) ?? ReportUnknown(context, ownNames[0]);
        }
        if (ownNames.Length > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.DialectAmbiguous,
                location: null,
                string.Join(", ", ownNames)));
            return null;
        }

        var referencedNames = compilation.SourceModule.ReferencedAssemblySymbols
            .SelectMany(ReadDialectName)
            .Distinct()
            .ToArray();

        if (referencedNames.Length == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.DialectMissing, location: null));
            return null;
        }

        if (referencedNames.Length > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.DialectAmbiguous,
                location: null,
                string.Join(", ", referencedNames)));
            return null;
        }

        return CreateBuilder(referencedNames[0]) ?? ReportUnknown(context, referencedNames[0]);
    }

    private static IEnumerable<string> ReadDialectName(IAssemblySymbol assembly)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != AttributeFullName)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string name &&
                !string.IsNullOrEmpty(name))
            {
                yield return name;
            }
        }
    }

    private static SqlBuilder? CreateBuilder(string name)
    {
        return name switch
        {
            "Sqlite" => new SqliteSqlBuilder(),
            "PostgreSql" => new PostgreSqlSqlBuilder(),
            "SqlServer" => new SqlServerSqlBuilder(),
            _ => null,
        };
    }

    private static SqlBuilder? ReportUnknown(SourceProductionContext context, string name)
    {
        context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.DialectUnknown, location: null, name));
        return null;
    }
}
