using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Inquiry.Generators.Sql;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;

namespace Inquiry.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class InquiryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidateClass(node),
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.Node)
            .Collect();

        var source = context.CompilationProvider.Combine(candidates);

        context.RegisterSourceOutput(source, static (spc, pair) =>
            Execute(spc, pair.Left, pair.Right));
    }

    private static bool IsCandidateClass(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax cls &&
            (cls.AttributeLists.Count > 0 || cls.BaseList is not null);
    }

    private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<ClassDeclarationSyntax> candidates)
    {
        var entities = EntityProcessor.Discover(context, compilation, candidates);
        var entityRegistrations = ImmutableArray.CreateBuilder<EntityRegistrationModel>();
        foreach (var entity in entities.Values)
        {
            entityRegistrations.Add(EntityProcessor.EmitMaterializer(context, entity));
        }

        // Dialect is only required when there is at least one store to emit. Materializers and
        // registrations are dialect-agnostic, so we still produce them when no provider is
        // referenced (e.g. analyzer-only consumers).
        SqlBuilder? sqlBuilder = null;
        var storeRegistrations = ImmutableArray.CreateBuilder<StoreRegistrationModel>();
        foreach (var classDeclaration in candidates)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (compilation.GetSemanticModel(classDeclaration.SyntaxTree).GetDeclaredSymbol(classDeclaration, context.CancellationToken) is not INamedTypeSymbol storeSymbol)
            {
                continue;
            }

            if (!GeneratorHelpers.TryGetStoreEntityType(storeSymbol, out var entityType))
            {
                continue;
            }

            if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.StoreMustBePartial, classDeclaration.Identifier.GetLocation(), storeSymbol.Name));
                continue;
            }

            if (storeSymbol.ContainingType is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InquiryDiagnosticDescriptors.StoreCannotBeNested,
                    classDeclaration.Identifier.GetLocation(),
                    storeSymbol.Name,
                    storeSymbol.ContainingType.ToDisplayString()));
                continue;
            }

            if (storeSymbol.IsAbstract)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InquiryDiagnosticDescriptors.StoreCannotBeAbstract,
                    classDeclaration.Identifier.GetLocation(),
                    storeSymbol.Name));
                continue;
            }

            var entityKey = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!entities.TryGetValue(entityKey, out var entity))
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.StoreEntityNotMapped, classDeclaration.Identifier.GetLocation(), storeSymbol.Name, entityType.ToDisplayString()));
                continue;
            }

            var methods = StoreProcessor.Discover(context, storeSymbol, entity);
            if (methods.Length == 0)
            {
                continue;
            }

            // Resolve the dialect lazily so consumers without any stores never see the diagnostic,
            // and so the resolution diagnostic is reported at most once per compilation.
            sqlBuilder ??= DialectResolver.Resolve(context, compilation);
            if (sqlBuilder is null)
            {
                continue;
            }

            storeRegistrations.Add(StoreProcessor.Emit(context, storeSymbol, entity, methods, entities, sqlBuilder));
        }

        if (storeRegistrations.Count > 0 || entityRegistrations.Count > 0)
        {
            RegistrationEmitter.Emit(context, entityRegistrations.ToImmutable(), storeRegistrations.ToImmutable());
        }
    }
}
