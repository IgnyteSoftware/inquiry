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
                transform: static (ctx, ct) => EntityProcessor.Extract((INamedTypeSymbol)ctx.TargetSymbol, ctx.SemanticModel.Compilation, ct))
            .WithTrackingName(EntitiesTrackingName)
            .Collect();

        // Views: each [InquiryView] class is projected into an equatable EntityData with IsView=true
        // (read-only, keyless-permitted, no DDL). Merged into the entity array below so all the
        // materializer / store-linking / registration machinery treats a view like any other entity.
        var views = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownSymbols.EntityAttributeNamespace + ".InquiryViewAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => EntityProcessor.ExtractView((INamedTypeSymbol)ctx.TargetSymbol, ctx.SemanticModel.Compilation, ct))
            .WithTrackingName(ViewsTrackingName)
            .Collect();

        // Tables and views share the EntityData model and every downstream stage, so concatenate them
        // into one array. The Execute signature stays unchanged; IsView gates read-only behavior.
        var allEntities = entities.Combine(views).Select(static (pair, _) => pair.Left.AddRange(pair.Right));

        // projections: each [InquiryProjection] class is projected into an equatable ProjectionData
        // (a keyless column subset) so its materializer caches like an entity's.
        var projections = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownSymbols.EntityAttributeNamespace + ".InquiryProjectionAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => ProjectionProcessor.Extract((INamedTypeSymbol)ctx.TargetSymbol, ct))
            .WithTrackingName(ProjectionsTrackingName)
            .Collect();

        // Ad-hoc DTOs: each [InquiryAdHoc] class is projected into an equatable AdHocData. Its
        // materializer registers like an entity's, giving the ad-hoc IInquiry.Query* path a
        // DI-resolved materializer for result shapes that are neither entities nor projections.
        var adHocs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownSymbols.EntityAttributeNamespace + ".InquiryAdHocAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => AdHocProcessor.Extract((INamedTypeSymbol)ctx.TargetSymbol, ct))
            .WithTrackingName(AdHocTrackingName)
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
            .Select((compilation, cancellationToken) => ResolveOwnership(compilation, cancellationToken))
            .WithTrackingName(OwnershipTrackingName);

        var combined = allEntities.Combine(stores).Combine(projections).Combine(adHocs).Combine(ownership);

        context.RegisterSourceOutput(combined, (spc, data) =>
            Execute(spc, data.Left.Left.Left.Left, data.Left.Left.Left.Right, data.Left.Left.Right, data.Left.Right, data.Right));
    }

    internal const string EntitiesTrackingName = "InquiryEntities";
    internal const string ViewsTrackingName = "InquiryViews";
    internal const string StoresTrackingName = "InquiryStores";
    internal const string ProjectionsTrackingName = "InquiryProjections";
    internal const string AdHocTrackingName = "InquiryAdHocDtos";
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
        ImmutableArray<AdHocData> adHocs,
        DialectOwnership ownership)
    {
        // Entity (and projection/ad-hoc) diagnostics are surfaced regardless of dialect ownership
        // (matching the previous behavior, where entity discovery ran before arbitration).
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

        foreach (var adHoc in adHocs)
        {
            foreach (var diagnostic in adHoc.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        }

        if (!entities.Any(static entity => entity.IsMapped) && stores.IsEmpty && projections.IsEmpty && adHocs.IsEmpty) return;
        if (ownership.Kind is DialectOwnershipKind.NotMine or DialectOwnershipKind.AmbiguousFollower or DialectOwnershipKind.UnknownFollower) return;
        if (ownership.Kind == DialectOwnershipKind.UnknownLeader)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.DialectUnknown, null,
                FormatDialectForDiagnostic(ownership.UnknownDialect), string.IsNullOrWhiteSpace(ownership.AmbiguousDialects) ? "<none>" : ownership.AmbiguousDialects));
            return;
        }
        if (ownership.Kind == DialectOwnershipKind.AmbiguousLeader)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.DialectAmbiguous, null, ownership.AmbiguousDialects));
            return;
        }

        var sqlBuilder = CreateSqlBuilder();
        entities = ResolveComputedExpressions(context, entities, sqlBuilder, out var computedInvalidEntityNames);
        var mappedEntities = new Dictionary<string, EntityData>();
        foreach (var entity in entities)
        {
            if (entity.IsMapped)
            {
                mappedEntities[entity.FullyQualifiedName] = entity;
            }
        }

        // Relation shapes are validated here, at declaration time, so a mistyped foreign key or a
        // composite-key child is reported even when no method eager-loads the relation (the emit
        // path no longer re-reports these).
        ValidateRelations(context, mappedEntities);

        var mappedProjections = new Dictionary<string, ProjectionData>();
        foreach (var projection in projections)
        {
            if (projection.IsMapped)
            {
                mappedProjections[projection.FullyQualifiedName] = projection;
            }
        }

        var mappedAdHocs = new Dictionary<string, AdHocData>();
        foreach (var adHoc in adHocs)
        {
            if (adHoc.IsMapped)
            {
                mappedAdHocs[adHoc.FullyQualifiedName] = adHoc;
            }
        }

        // Nothing in this compilation needs codegen — skip arbitration so consumer projects that
        // only reference the runtime don't get spurious referenced-package dialect-collision noise.
        if (mappedEntities.Count == 0 && stores.IsEmpty && mappedProjections.Count == 0 && mappedAdHocs.Count == 0)
        {
            return;
        }

        var entityRegistrations = ImmutableArray.CreateBuilder<EntityRegistration>();
        foreach (var entity in mappedEntities.Values)
        {
            entityRegistrations.Add(EntityProcessor.EmitMaterializer(context, entity, sqlBuilder));
        }

        // projection materializers register and emit exactly like entity materializers (same
        // IInquiryEntityMaterializer<T> contract), so they share the registration set.
        foreach (var projection in mappedProjections.Values)
        {
            entityRegistrations.Add(ProjectionProcessor.EmitMaterializer(context, projection, sqlBuilder));
        }

        // ad-hoc DTO materializers share the registration set too. They are dialect-independent
        // (reads only — no SQL is ever generated for them), so the dialect owner emits them like
        // the other materializers.
        foreach (var adHoc in mappedAdHocs.Values)
        {
            entityRegistrations.Add(AdHocProcessor.EmitMaterializer(context, adHoc, sqlBuilder));
        }

        var storeRegistrations = ImmutableArray.CreateBuilder<StoreRegistration>();
        var collectionArtifacts = ImmutableArray.CreateBuilder<CollectionParameterArtifact>();

        foreach (var store in stores)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            foreach (var diagnostic in store.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            var emission = StoreProcessor.Emit(
                context,
                store,
                mappedEntities,
                mappedProjections,
                sqlBuilder,
                ownership.EmitUnsupportedOperationStubs,
                computedInvalidEntityNames);
            if (emission is not null)
            {
                storeRegistrations.Add(emission.Registration);
                collectionArtifacts.AddRange(emission.Artifacts);
            }
        }

        // emit one per-assembly schema DDL file for the resolved dialect. Iterate the original
        // entity array (source order) filtered to mapped entities so emission is deterministic.
        // Views are defined in the database, not created by Inquiry — exclude them from DDL.
        var schemaEntities = entities.Where(e => e.IsMapped && !e.IsView).ToList();
        var providerArtifacts = collectionArtifacts
            .GroupBy(static artifact => artifact.Identity, System.StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static artifact => artifact.Schema, System.StringComparer.Ordinal)
            .ThenBy(static artifact => artifact.Name, System.StringComparer.Ordinal)
            .ToArray();
        if (!string.IsNullOrEmpty(ownership.ManifestMetadataCollisionKey))
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaManifestMetadataCollision,
                null, ownership.ManifestMetadataCollisionKey));
        }
        SchemaEmitter.Emit(context, schemaEntities, sqlBuilder, providerArtifacts,
            emitManifestMetadata: string.IsNullOrEmpty(ownership.ManifestMetadataCollisionKey));

        if (storeRegistrations.Count > 0 || entityRegistrations.Count > 0)
        {
            RegistrationEmitter.Emit(context, entityRegistrations.ToImmutable(), storeRegistrations.ToImmutable());
        }
    }

    /// <summary>
    /// Reports relation-shape errors at declaration time for every mapped entity's relations,
    /// regardless of whether any store method eager-loads them: a foreign-key property that is not a
    /// mapped column on the side that should own it (INQ040), the same property found on the opposite
    /// side — a reversed relation (INQ058), and a composite-key child (INQ041). A relation whose
    /// child type isn't a mapped entity is left alone (the emit path tolerates it).
    /// </summary>
    private static ImmutableArray<EntityData> ResolveComputedExpressions(SourceProductionContext context, ImmutableArray<EntityData> entities, SqlBuilder builder,
        out HashSet<string> computedInvalidEntityNames)
    {
        computedInvalidEntityNames = new HashSet<string>(System.StringComparer.Ordinal);
        var resolved = ImmutableArray.CreateBuilder<EntityData>(entities.Length);
        foreach (var entity in entities)
        {
            var valid = entity.IsMapped;
            var columns = ImmutableArray.CreateBuilder<ColumnData>(entity.Columns.Count);
            foreach (var column in entity.Columns)
            {
                var fallback = column.ComputedExpression;
                var overrides = column.ComputedExpressionOverrides.AsImmutableArray();
                var selected = fallback;
                var selectedLocation = column.ComputedExpressionLocation ?? column.Location;
                var reasons = new List<(LocationData? Location, string Reason)>();
                foreach (var item in overrides)
                {
                    if (!IsValidProviderId(item.ProviderId)) reasons.Add((item.ProviderIdLocation, "provider id is invalid; use lowercase ASCII [a-z][a-z0-9.-]{0,63}"));
                    if (string.IsNullOrWhiteSpace(item.Expression)) reasons.Add((item.ExpressionLocation, "override expression is empty or whitespace"));
                }
                foreach (var duplicate in overrides.Where(item => IsValidProviderId(item.ProviderId)).GroupBy(static item => item.ProviderId, System.StringComparer.Ordinal).Where(static group => group.Count() > 1))
                    reasons.Add((duplicate.Skip(1).First().ExpressionLocation, "more than one override declares provider '" + duplicate.Key + "'"));
                if (overrides.Length > 0 && string.IsNullOrWhiteSpace(fallback)) reasons.Add((overrides[0].ExpressionLocation, "a provider override requires a non-empty InquiryColumn.Computed fallback"));
                var providerOverride = overrides.FirstOrDefault(item => item.ProviderId == builder.ProviderId);
                if (providerOverride is not null) { selected = providerOverride.Expression; selectedLocation = providerOverride.ExpressionLocation; }
                if (fallback is not null || overrides.Length > 0)
                {
                    if (string.IsNullOrWhiteSpace(selected))
                    {
                        if (providerOverride is null) reasons.Add((selectedLocation, "expression is empty or whitespace"));
                    }
                    else
                    {
                        var failures = builder.ValidateComputedExpression(selected!);
                        if (failures.Count > 0) reasons.Add((selectedLocation, string.Join("; ", failures)));
                        if (builder.RequiresBoundedComputedStrings && column.TypeClass == DbTypeClass.String)
                        {
                            var maxLength = builder.MaxBoundedStringLength(column.IsUnicode);
                            if (column.Length <= 0 || column.Length > maxLength)
                            {
                                context.ReportDiagnostic(Diagnostic.Create(
                                    InquiryDiagnosticDescriptors.ComputedStringRequiresBoundedLength,
                                    selectedLocation?.ToLocation(),
                                    entity.FullyQualifiedName + "." + column.PropertyName,
                                    maxLength));
                                valid = false;
                                computedInvalidEntityNames.Add(entity.FullyQualifiedName);
                                selected = null;
                            }
                        }
                    }
                }
                foreach (var reason in reasons)
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.ComputedExpressionInvalid,
                        reason.Location?.ToLocation(), entity.FullyQualifiedName + "." + column.PropertyName, builder.ProviderId, reason.Reason));
                if (reasons.Count > 0) { valid = false; computedInvalidEntityNames.Add(entity.FullyQualifiedName); }
                columns.Add(column with { ComputedExpression = reasons.Count == 0 && selected is not null ? builder.RenderComputedExpression(selected) : selected });
            }
            var finalColumns = columns.ToImmutable();
            ColumnData? Find(ColumnData? original) => original is null ? null : finalColumns.FirstOrDefault(column => column.PropertyName == original.PropertyName);
            resolved.Add(entity with
            {
                Columns = new EquatableArray<ColumnData>(finalColumns),
                Keys = new EquatableArray<ColumnData>(entity.Keys.AsImmutableArray().Select(key => finalColumns.First(column => column.PropertyName == key.PropertyName)).ToImmutableArray()),
                SoftDeleteColumn = Find(entity.SoftDeleteColumn),
                ConcurrencyToken = Find(entity.ConcurrencyToken),
                IsMapped = valid,
            });
        }
        return resolved.ToImmutable();
    }

    private static bool IsValidProviderId(string value)
    {
        if (value.Length is < 1 or > 64 || value[0] is < 'a' or > 'z') return false;
        for (var i = 1; i < value.Length; i++)
            if (!(value[i] is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')) return false;
        return true;
    }

    private static void ValidateRelations(SourceProductionContext context, Dictionary<string, EntityData> mappedEntities)
    {
        foreach (var entity in mappedEntities.Values)
        {
            foreach (var relation in entity.Relations)
            {
                if (relation.IsManyToMany)
                {
                    // A many-to-many association must be a collection nav whose junction and related
                    // entities are both mapped, the junction must carry the two named FK properties, and
                    // the related entity must have a single-column key (we JOIN/index on it).
                    var validJunction = relation.JunctionEntityFullyQualifiedName is { } junctionFqn &&
                        mappedEntities.TryGetValue(junctionFqn, out var junction) &&
                        FindEntityColumn(junction, relation.JunctionParentForeignKeyProperty ?? string.Empty) is not null &&
                        relation.JunctionChildForeignKeyProperties.Count == 1 &&
                        FindEntityColumn(junction, relation.JunctionChildForeignKeyProperties[0]) is not null;
                    var validChild = mappedEntities.TryGetValue(relation.ChildEntityFullyQualifiedName, out var mnChild) &&
                        mnChild.Keys.Count == 1;

                    if (!relation.IsCollection || !validJunction || !validChild)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InquiryDiagnosticDescriptors.ManyToManyInvalid, relation.Location?.ToLocation(),
                            entity.Name, relation.PropertyName));
                    }

                    continue;
                }

                if (!mappedEntities.TryGetValue(relation.ChildEntityFullyQualifiedName, out var child))
                {
                    continue;
                }

                // A to-many (collection) relation's FK lives on the child; a to-one (reference)
                // relation's FK lives on the parent (this entity).
                var owner = relation.IsCollection ? child : entity;
                var other = relation.IsCollection ? entity : child;
                var relationKind = relation.IsCollection ? "collection" : "reference";
                var location = relation.Location?.ToLocation();

                if (FindEntityColumn(owner, relation.ForeignKeyProperty) is null)
                {
                    if (FindEntityColumn(other, relation.ForeignKeyProperty) is not null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InquiryDiagnosticDescriptors.RelationForeignKeyWrongSide, location,
                            entity.Name, relation.PropertyName, relation.ForeignKeyProperty, owner.Name, relationKind, other.Name));
                    }
                    else
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InquiryDiagnosticDescriptors.UnknownRelationForeignKey, location,
                            entity.Name, relation.PropertyName, relation.ForeignKeyProperty, owner.Name));
                    }
                }

                if (child.Keys.Count > 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.RelationCompositeChildKey, location,
                        entity.Name, relation.PropertyName, child.Name, child.Keys.Count));
                }
            }
        }
    }

    private static ColumnData? FindEntityColumn(EntityData entity, string propertyName)
    {
        foreach (var column in entity.Columns.AsImmutableArray())
        {
            if (column.PropertyName == propertyName)
            {
                return column;
            }
        }

        return null;
    }

    private DialectOwnership ResolveOwnership(Compilation compilation, CancellationToken cancellationToken)
    {
        var ownership = ResolveDialectOwnership(compilation) with
        {
            EmitUnsupportedOperationStubs = ShouldEmitUnsupportedOperationStubs(compilation, cancellationToken),
        };
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "System.Reflection.AssemblyMetadataAttribute"
                || attribute.ConstructorArguments.Length == 0
                || attribute.ConstructorArguments[0].Value is not string key
                || !key.StartsWith("Inquiry.SchemaManifest.", System.StringComparison.Ordinal)) continue;
            return ownership with { ManifestMetadataCollisionKey = key };
        }
        return ownership;
    }

    private static bool ShouldEmitUnsupportedOperationStubs(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var fallback = compilation.Options.SpecificDiagnosticOptions.TryGetValue("INQ039", out var compilationAction)
            ? compilationAction
            : ReportDiagnostic.Default;
        var provider = compilation.Options.SyntaxTreeOptionsProvider;
        if (provider is null)
        {
            return IsUnsupportedOperationStubOptIn(fallback);
        }

        if (provider.TryGetGlobalDiagnosticValue("INQ039", cancellationToken, out var globalAction))
        {
            fallback = globalAction;
        }

        ReportDiagnostic? uniformAction = null;
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = provider.TryGetDiagnosticValue(tree, "INQ039", cancellationToken, out var treeAction)
                    ? treeAction
                    : fallback;
            if (!IsUnsupportedOperationStubOptIn(action) ||
                uniformAction is { } previousAction && previousAction != action)
            {
                return false;
            }

            uniformAction = action;
        }

        return uniformAction is { } resolvedAction
            ? IsUnsupportedOperationStubOptIn(resolvedAction)
            : IsUnsupportedOperationStubOptIn(fallback);
    }

    private static bool IsUnsupportedOperationStubOptIn(ReportDiagnostic action)
        => action is not ReportDiagnostic.Default and not ReportDiagnostic.Error;

    private DialectOwnership ResolveDialectOwnership(Compilation compilation)
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

    private readonly record struct DialectOwnership(
        DialectOwnershipKind Kind,
        string AmbiguousDialects,
        string UnknownDialect,
        string ManifestMetadataCollisionKey,
        bool EmitUnsupportedOperationStubs)
    {
        public DialectOwnership(DialectOwnershipKind kind)
            : this(kind, string.Empty, string.Empty, string.Empty, false)
        {
        }

        public DialectOwnership(DialectOwnershipKind kind, string ambiguousDialects)
            : this(kind, ambiguousDialects, string.Empty, string.Empty, false)
        {
        }

        public DialectOwnership(DialectOwnershipKind kind, string ambiguousDialects, string unknownDialect)
            : this(kind, ambiguousDialects, unknownDialect, string.Empty, false)
        {
        }
    }
}
