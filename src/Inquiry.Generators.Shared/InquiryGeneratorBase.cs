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
using System.Threading;

namespace Inquiry.Generators;

/// <summary>
/// Shared implementation of the Inquiry source generator. Each provider's analyzer assembly ships
/// a concrete subclass marked with <c>[Generator]</c> that supplies its <see cref="SqlBuilder"/>
/// via <see cref="CreateSqlBuilder"/> and declares its <see cref="Dialect"/> name.
/// </summary>
/// <remarks>
/// The pipeline is fully incremental: entities and stores are projected into value-equatable models
/// in their syntax-provider transforms, dialect ownership is projected from the compilation into a
/// small equatable value, and only those models flow into the output stage. An edit that does not
/// change any entity, store, or the dialect set re-runs nothing downstream.
///
/// Roslyn loads each provider's analyzer assembly into its own AssemblyLoadContext, so no in-process
/// state is shared across providers. When a consuming project references multiple provider packages
/// every loaded generator runs; each runs the same arbitration and the one whose <see cref="Dialect"/>
/// matches the resolved attribute does the emission. If the dialect is ambiguous, only the generator
/// whose dialect sorts first ordinally emits materializers and the INQ014 diagnostic.
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
        // Entities via the attribute index; each [InquiryTable] class is projected into an
        // equatable EntityData (diagnostics carried as data) so the transform caches.
        var entities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownSymbols.EntityAttributeNamespace + ".InquiryTableAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => EntityProcessor.Extract((INamedTypeSymbol)ctx.TargetSymbol, ct))
            .WithTrackingName(EntitiesTrackingName)
            .Collect();

        // Stores carry no class-level attribute (identified by the InquiryStore<T> base), so they use
        // a narrow syntactic predicate; non-stores transform to null and are filtered out.
        var stores = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsStoreCandidateClass(node),
                transform: static (ctx, ct) => ExtractStore(ctx, ct))
            .Where(static store => store is not null)
            .Select(static (store, _) => store!)
            .WithTrackingName(StoresTrackingName)
            .Collect();

        // Dialect ownership depends on the whole compilation (assembly attributes + referenced
        // assemblies), so it re-projects on every edit — but into a tiny equatable value, so the
        // output stage still caches whenever ownership is unchanged.
        var ownership = context.CompilationProvider
            .Select((compilation, _) => ResolveOwnership(compilation))
            .WithTrackingName(OwnershipTrackingName);

        var combined = entities.Combine(stores).Combine(ownership);

        context.RegisterSourceOutput(combined, (spc, data) =>
            Execute(spc, data.Left.Left, data.Left.Right, data.Right));
    }

    internal const string EntitiesTrackingName = "InquiryEntities";
    internal const string StoresTrackingName = "InquiryStores";
    internal const string OwnershipTrackingName = "InquiryDialectOwnership";

    private static bool IsStoreCandidateClass(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax cls || cls.BaseList is null)
        {
            return false;
        }

        // Syntactic-only first pass (Extract re-validates semantically). Match the base type's
        // right-most simple name identifier exactly — no whole-type ToString() allocation on this
        // per-node hot path, and no false match against names that merely contain "InquiryStore".
        foreach (var baseType in cls.BaseList.Types)
        {
            var name = baseType.Type switch
            {
                QualifiedNameSyntax qualified => qualified.Right,
                AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name,
                SimpleNameSyntax simple => simple,
                _ => null,
            };

            if (name is not null && name.Identifier.ValueText == "InquiryStore")
            {
                return true;
            }
        }

        return false;
    }

    private static StoreData? ExtractStore(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)context.Node, cancellationToken) is not INamedTypeSymbol storeSymbol)
        {
            return null;
        }

        return StoreProcessor.Extract(storeSymbol, cancellationToken);
    }

    private void Execute(
        SourceProductionContext context,
        ImmutableArray<EntityData> entities,
        ImmutableArray<StoreData> stores,
        DialectOwnership ownership)
    {
        // Entity diagnostics are surfaced regardless of dialect ownership (matching the previous
        // behavior, where entity discovery ran before arbitration).
        foreach (var entity in entities)
        {
            foreach (var diagnostic in entity.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        }

        var mappedEntities = new Dictionary<string, EntityData>();
        foreach (var entity in entities)
        {
            if (entity.IsMapped)
            {
                mappedEntities[entity.FullyQualifiedName] = entity;
            }
        }

        // Nothing in this compilation needs codegen — skip arbitration so consumer projects that
        // only reference the runtime don't get spurious referenced-package dialect-collision noise.
        if (mappedEntities.Count == 0 && stores.IsEmpty)
        {
            return;
        }

        if (ownership.Kind is DialectOwnershipKind.NotMine or DialectOwnershipKind.AmbiguousFollower)
        {
            return;
        }

        var entityRegistrations = ImmutableArray.CreateBuilder<EntityRegistration>();
        foreach (var entity in mappedEntities.Values)
        {
            entityRegistrations.Add(EntityProcessor.EmitMaterializer(context, entity));
        }

        var storeRegistrations = ImmutableArray.CreateBuilder<StoreRegistration>();

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
            foreach (var store in stores)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                foreach (var diagnostic in store.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic.ToDiagnostic());
                }

                var registration = StoreProcessor.Emit(context, store, mappedEntities, sqlBuilder);
                if (registration is not null)
                {
                    storeRegistrations.Add(registration);
                }
            }

            // W7: emit one per-assembly schema DDL file for the resolved dialect. Iterate the original
            // entity array (source order) filtered to mapped entities so emission is deterministic.
            var schemaEntities = entities.Where(e => e.IsMapped).ToList();
            SchemaEmitter.Emit(context, schemaEntities, sqlBuilder);
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
        // Only the alphabetically-first provider in the ambiguous set is responsible for diagnostics
        // and materializer emission, so multi-provider builds produce one diagnostic (not N) and one
        // set of materializer files (not N colliding copies). If our dialect isn't in the set, stay silent.
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

    private readonly record struct DialectOwnership(DialectOwnershipKind Kind, string AmbiguousDialects)
    {
        public DialectOwnership(DialectOwnershipKind kind)
            : this(kind, string.Empty)
        {
        }
    }
}
