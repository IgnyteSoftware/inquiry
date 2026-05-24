using System.Collections.Immutable;
using System.Linq;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Features.EntityMetadata;
using Inquiry.Generators.Features.ServiceRegistration;
using Inquiry.Generators.Features.StoreOperations;
using Inquiry.Generators.Features.Stores;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        var storeOperationFeatures = StoreOperationFeatures.All;
        var entities = EntityDiscoverer.Discover(context, compilation, candidates);
        var entityRegistrations = ImmutableArray.CreateBuilder<EntityRegistrationModel>();
        foreach (var entity in entities.Values)
        {
            entityRegistrations.Add(EntityMetadataFeature.Generate(context, entity));
        }

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

            var entityKey = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!entities.TryGetValue(entityKey, out var entity))
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.StoreEntityNotMapped, classDeclaration.Identifier.GetLocation(), storeSymbol.Name, entityType.ToDisplayString()));
                continue;
            }

            var methods = StoreMethodDiscoverer.Discover(context, storeSymbol, entity, storeOperationFeatures);
            if (methods.Length == 0)
            {
                continue;
            }

            storeRegistrations.Add(StoreImplementationGenerator.Generate(context, storeSymbol, entity, methods));
        }

        if (storeRegistrations.Count > 0 || entityRegistrations.Count > 0)
        {
            ServiceRegistrationGenerator.Generate(context, entityRegistrations.ToImmutable(), storeRegistrations.ToImmutable());
        }
    }
}
