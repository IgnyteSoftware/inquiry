using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Inquiry.Generators;

/// <summary>
/// Shared implementation of the Inquiry source generator. Each provider's analyzer assembly ships
/// a concrete subclass marked with <c>[Generator]</c> that supplies its <see cref="SqlBuilder"/>
/// via <see cref="CreateSqlBuilder"/> and declares its <see cref="Dialect"/> name. The base handles
/// candidate discovery, dialect arbitration, materializer emission, and store emission.
/// </summary>
/// <remarks>
/// Roslyn loads each provider's analyzer assembly into its own AssemblyLoadContext, so no
/// in-process state is shared across providers. When a consuming project references multiple
/// provider packages, every loaded generator runs; each runs the same arbitration and the one
/// whose <see cref="Dialect"/> matches the resolved attribute does the emission. The remaining
/// generators stay silent. If the dialect is ambiguous, only the generator whose dialect sorts
/// first ordinally emits materializers and the INQ014 diagnostic, so multi-provider builds
/// produce one collision-free output rather than N.
/// </remarks>
public abstract class InquiryGeneratorBase : IIncrementalGenerator
{
    private const string DialectAttributeFullName = "Inquiry.InquiryDialectAttribute";

    /// <summary>The dialect this generator emits SQL for (e.g. "Sqlite", "SqlServer", "PostgreSql").</summary>
    protected abstract string Dialect { get; }

    /// <summary>Factory for the dialect-specific SQL builder.</summary>
    protected abstract SqlBuilder CreateSqlBuilder();

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Entities are found through the attribute index (ForAttributeWithMetadataName) so only
        // [InquiryTable] classes invoke the transform, rather than every class that happens to have
        // an attribute or a base list. Stores carry no class-level attribute (they are identified by
        // the InquiryStore<T> base), so they keep a syntactic predicate — but narrowed to classes
        // whose base list actually names InquiryStore.
        var entityClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownSymbols.EntityAttributeNamespace + ".InquiryTableAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.TargetNode)
            .Collect();

        var storeClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsStoreCandidateClass(node),
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.Node)
            .Collect();

        var source = context.CompilationProvider.Combine(entityClasses.Combine(storeClasses));

        context.RegisterSourceOutput(source, (spc, pair) =>
        {
            // Merge the two candidate streams (deduped by reference — a class is only ever in both
            // if it is somehow [InquiryTable] *and* : InquiryStore<T>) and hand the combined set to
            // the existing semantic pipeline unchanged.
            var candidates = pair.Right.Left.Concat(pair.Right.Right).Distinct().ToImmutableArray();
            Execute(spc, pair.Left, candidates);
        });
    }

    private static bool IsStoreCandidateClass(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax cls || cls.BaseList is null)
        {
            return false;
        }

        foreach (var baseType in cls.BaseList.Types)
        {
            if (baseType.Type.ToString().Contains("InquiryStore"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStoreCandidate(ImmutableArray<ClassDeclarationSyntax> candidates)
    {
        // Cheap syntactic check: a store inherits from InquiryStore<T>. We don't need semantic
        // accuracy here — false positives are fine since the worst case is we proceed and find
        // nothing to emit.
        foreach (var candidate in candidates)
        {
            if (candidate.BaseList is null) continue;
            foreach (var baseType in candidate.BaseList.Types)
            {
                var text = baseType.Type.ToString();
                if (text.Contains("InquiryStore")) return true;
            }
        }
        return false;
    }

    private void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<ClassDeclarationSyntax> candidates)
    {
        var entities = EntityProcessor.Discover(context, compilation, candidates);

        // Nothing in this compilation needs codegen — skip everything (including dialect arbitration)
        // so consumer projects that just reference the runtime don't get spurious diagnostics about
        // referenced-package dialect collisions. We still proceed if there are candidate store
        // classes even when no entity is mapped (those scenarios need their own diagnostics, e.g.
        // INQ008 for an unmapped entity).
        if (entities.Count == 0 && !HasStoreCandidate(candidates))
        {
            return;
        }

        var ownership = ResolveOwnership(compilation);
        if (ownership.Kind == DialectOwnershipKind.NotMine ||
            ownership.Kind == DialectOwnershipKind.AmbiguousFollower)
        {
            return;
        }

        // From here we are either Owned (this dialect matches) or AmbiguousLeader (responsible for
        // the once-per-compilation INQ014 even though we won't emit store SQL).
        var entityRegistrations = ImmutableArray.CreateBuilder<EntityRegistrationModel>();
        foreach (var entity in entities.Values)
        {
            entityRegistrations.Add(EntityProcessor.EmitMaterializer(context, entity));
        }

        var storeRegistrations = ImmutableArray.CreateBuilder<StoreRegistrationModel>();

        if (ownership.Kind == DialectOwnershipKind.AmbiguousLeader)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.DialectAmbiguous,
                location: null,
                ownership.AmbiguousDialects));
        }
        else
        {
            var sqlBuilder = CreateSqlBuilder();
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

                storeRegistrations.Add(StoreProcessor.Emit(context, storeSymbol, entity, methods, entities, sqlBuilder));
            }
        }

        if (storeRegistrations.Count > 0 || entityRegistrations.Count > 0)
        {
            RegistrationEmitter.Emit(context, entityRegistrations.ToImmutable(), storeRegistrations.ToImmutable());
        }
    }

    private DialectOwnership ResolveOwnership(Compilation compilation)
    {
        var ownNames = ReadDialectName(compilation.Assembly).Distinct().ToArray();
        if (ownNames.Length > 1)
        {
            return ArbitrateAmbiguous(ownNames);
        }
        if (ownNames.Length == 1)
        {
            return ownNames[0] == Dialect
                ? new DialectOwnership(DialectOwnershipKind.Owned)
                : new DialectOwnership(DialectOwnershipKind.NotMine);
        }

        var referencedNames = compilation.SourceModule.ReferencedAssemblySymbols
            .SelectMany(ReadDialectName)
            .Distinct()
            .ToArray();

        if (referencedNames.Length > 1)
        {
            return ArbitrateAmbiguous(referencedNames);
        }
        if (referencedNames.Length == 1)
        {
            return referencedNames[0] == Dialect
                ? new DialectOwnership(DialectOwnershipKind.Owned)
                : new DialectOwnership(DialectOwnershipKind.NotMine);
        }

        // No [InquiryDialect] attribute anywhere. The user dropped this provider's analyzer into the
        // build (otherwise we wouldn't be running), so treat that as an implicit opt-in.
        return new DialectOwnership(DialectOwnershipKind.Owned);
    }

    private DialectOwnership ArbitrateAmbiguous(string[] dialects)
    {
        var ordered = dialects.OrderBy(d => d, System.StringComparer.Ordinal).ToArray();
        var joined = string.Join(", ", ordered);
        // Only the alphabetically-first provider in the ambiguous set is responsible for
        // diagnostics and materializer emission, so multi-provider builds produce one diagnostic
        // (not N) and one set of materializer files (not N colliding copies). If our dialect
        // isn't in the set at all, stay silent.
        var kind = ordered.Contains(Dialect) && Dialect == ordered[0]
            ? DialectOwnershipKind.AmbiguousLeader
            : DialectOwnershipKind.AmbiguousFollower;
        return new DialectOwnership(kind, joined);
    }

    private static IEnumerable<string> ReadDialectName(IAssemblySymbol assembly)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != DialectAttributeFullName)
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

    private enum DialectOwnershipKind { Owned, NotMine, AmbiguousLeader, AmbiguousFollower }

    private readonly struct DialectOwnership
    {
        public DialectOwnership(DialectOwnershipKind kind, string ambiguousDialects = "")
        {
            Kind = kind;
            AmbiguousDialects = ambiguousDialects;
        }

        public DialectOwnershipKind Kind { get; }
        public string AmbiguousDialects { get; }
    }
}
