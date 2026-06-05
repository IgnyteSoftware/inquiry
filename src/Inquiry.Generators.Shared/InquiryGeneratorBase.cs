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

        // projections: each [InquiryProjection] class is projected into an equatable ProjectionData
        // (a keyless column subset) so its materializer caches like an entity's.
        var projections = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownSymbols.EntityAttributeNamespace + ".InquiryProjectionAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => ProjectionProcessor.Extract((INamedTypeSymbol)ctx.TargetSymbol))
            .WithTrackingName(ProjectionsTrackingName)
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

        var combined = entities.Combine(stores).Combine(projections).Combine(ownership);

        context.RegisterSourceOutput(combined, (spc, data) =>
            Execute(spc, data.Left.Left.Left, data.Left.Left.Right, data.Left.Right, data.Right));
    }

    internal const string EntitiesTrackingName = "InquiryEntities";
    internal const string StoresTrackingName = "InquiryStores";
    internal const string ProjectionsTrackingName = "InquiryProjections";
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
        ImmutableArray<ProjectionData> projections,
        DialectOwnership ownership)
    {
        // Entity (and projection) diagnostics are surfaced regardless of dialect ownership (matching the
        // previous behavior, where entity discovery ran before arbitration).
        foreach (var entity in entities)
        {
            foreach (var diagnostic in entity.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        }

        foreach (var projection in projections)
        {
            foreach (var diagnostic in projection.Diagnostics)
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

        var mappedProjections = new Dictionary<string, ProjectionData>();
        foreach (var projection in projections)
        {
            if (projection.IsMapped)
            {
                mappedProjections[projection.FullyQualifiedName] = projection;
            }
        }

        // Nothing in this compilation needs codegen — skip arbitration so consumer projects that
        // only reference the runtime don't get spurious referenced-package dialect-collision noise.
        if (mappedEntities.Count == 0 && stores.IsEmpty && mappedProjections.Count == 0)
        {
            return;
        }

        if (ownership.Kind is DialectOwnershipKind.NotMine or DialectOwnershipKind.AmbiguousFollower or DialectOwnershipKind.UnknownFollower)
        {
            return;
        }

        if (ownership.Kind == DialectOwnershipKind.UnknownLeader)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.DialectUnknown,
                location: null,
                FormatDialectForDiagnostic(ownership.UnknownDialect),
                string.IsNullOrWhiteSpace(ownership.AmbiguousDialects) ? "<none>" : ownership.AmbiguousDialects));
            return;
        }

        var entityRegistrations = ImmutableArray.CreateBuilder<EntityRegistration>();
        foreach (var entity in mappedEntities.Values)
        {
            entityRegistrations.Add(EntityProcessor.EmitMaterializer(context, entity));
        }

        // projection materializers register and emit exactly like entity materializers (same
        // IInquiryEntityMaterializer<T> contract), so they share the registration set.
        foreach (var projection in mappedProjections.Values)
        {
            entityRegistrations.Add(ProjectionProcessor.EmitMaterializer(context, projection));
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

                var registration = StoreProcessor.Emit(context, store, mappedEntities, mappedProjections, sqlBuilder);
                if (registration is not null)
                {
                    storeRegistrations.Add(registration);
                }
            }

            // emit one per-assembly schema DDL file for the resolved dialect. Iterate the original
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
        var referencedNames = compilation.SourceModule.ReferencedAssemblySymbols
            .SelectMany(ReadDialectName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(System.StringComparer.Ordinal)
            .ToArray();

        var ownNames = ReadDialectName(compilation.Assembly)
            .Distinct(System.StringComparer.Ordinal)
            .ToArray();
        if (ownNames.Length > 1)
        {
            return ArbitrateAmbiguous(ownNames);
        }

        if (ownNames.Length == 1)
        {
            if (ownNames[0] == Dialect)
            {
                return new DialectOwnership(DialectOwnershipKind.Owned);
            }

            return referencedNames.Contains(ownNames[0], System.StringComparer.Ordinal)
                ? new DialectOwnership(DialectOwnershipKind.NotMine)
                : ArbitrateUnknown(ownNames[0], referencedNames);
        }

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

    private DialectOwnership ArbitrateUnknown(string unknownDialect, string[] availableDialects)
    {
        var ordered = availableDialects.Length == 0
            ? new[] { Dialect }
            : availableDialects.OrderBy(d => d, System.StringComparer.Ordinal).ToArray();
        var joined = string.Join(", ", ordered);
        var kind = Dialect == ordered[0]
            ? DialectOwnershipKind.UnknownLeader
            : DialectOwnershipKind.UnknownFollower;
        return new DialectOwnership(kind, joined, unknownDialect);
    }

    private static IEnumerable<string> ReadDialectName(IAssemblySymbol assembly)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != DialectAttributeFullName)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0)
            {
                yield return attribute.ConstructorArguments[0].Value as string ?? string.Empty;
            }
        }
    }

    private static string FormatDialectForDiagnostic(string dialect)
        => string.IsNullOrWhiteSpace(dialect) ? "<empty>" : dialect;

    private enum DialectOwnershipKind { Owned, NotMine, AmbiguousLeader, AmbiguousFollower, UnknownLeader, UnknownFollower }

    private readonly record struct DialectOwnership(DialectOwnershipKind Kind, string AmbiguousDialects, string UnknownDialect)
    {
        public DialectOwnership(DialectOwnershipKind kind)
            : this(kind, string.Empty, string.Empty)
        {
        }

        public DialectOwnership(DialectOwnershipKind kind, string ambiguousDialects)
            : this(kind, ambiguousDialects, string.Empty)
        {
        }
    }
}
