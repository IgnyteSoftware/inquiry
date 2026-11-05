using System.Collections.Immutable;
using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
/// <remarks>
/// This type supports only the first-party provider analyzers in this repository. It is not a stable
/// provider SDK and may change without a major-version release.
/// </remarks>
public abstract class InquiryGeneratorBase : IIncrementalGenerator
{
    private const string DialectAttributeFullName = "Inquiry.InquiryDialectAttribute";

    /// <summary>The dialect this generator emits SQL for (e.g. "Sqlite", "SqlServer", "PostgreSql").</summary>
    protected abstract string Dialect { get; }

    /// <summary>Factory for the dialect-specific SQL builder.</summary>
    protected abstract SqlBuilder CreateSqlBuilder();

    void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Entities via the attribute index; each [InquiryTable] class is projected into an
        // equatable EntityData (diagnostics carried as data) so the transform caches.
        var entities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownSymbols.EntityAttributeNamespace + ".InquiryTableAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => EntityProcessor.Extract((INamedTypeSymbol)ctx.TargetSymbol, ctx.SemanticModel.Compilation, ct))
            .WithTrackingName(EntitiesTrackingName);

        // Views: each [InquiryView] class is projected into an equatable EntityData with IsView=true
        // (read-only, keyless-permitted, no DDL). Every later stage treats a view like a table and
        // processes tables first, then views.
        var views = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownSymbols.EntityAttributeNamespace + ".InquiryViewAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => EntityProcessor.ExtractView((INamedTypeSymbol)ctx.TargetSymbol, ctx.SemanticModel.Compilation, ct))
            .WithTrackingName(ViewsTrackingName);

        // projections: each [InquiryProjection] class is projected into an equatable ProjectionData
        // (a keyless column subset) so its materializer caches like an entity's.
        var projections = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownSymbols.EntityAttributeNamespace + ".InquiryProjectionAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => ProjectionProcessor.Extract((INamedTypeSymbol)ctx.TargetSymbol, ct))
            .WithTrackingName(ProjectionsTrackingName);

        // Ad-hoc DTOs: each [InquiryAdHoc] class is projected into an equatable AdHocData. Its
        // materializer registers like an entity's, giving the ad-hoc IInquiry.Query* path a
        // DI-resolved materializer for result shapes that are neither entities nor projections.
        var adHocs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownSymbols.EntityAttributeNamespace + ".InquiryAdHocAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => AdHocProcessor.Extract((INamedTypeSymbol)ctx.TargetSymbol, ct))
            .WithTrackingName(AdHocTrackingName);

        // Stores carry no class-level attribute (identified by the InquiryStore<T> base), so they use
        // a narrow syntactic predicate; non-stores transform to null and are filtered out.
        var stores = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsStoreCandidateClass(node),
                transform: static (ctx, ct) => ExtractStore(ctx, ct))
            .Where(static store => store is not null)
            .Select(static (store, _) => store!)
            .WithTrackingName(StoresTrackingName);

        // Dialect ownership depends on the whole compilation (assembly attributes + referenced
        // assemblies), so it re-projects on every edit — but into a tiny equatable value, so the
        // output stage still caches whenever ownership is unchanged.
        var ownership = context.CompilationProvider
            .Select((compilation, cancellationToken) => ResolveOwnership(compilation, cancellationToken))
            .WithTrackingName(OwnershipTrackingName);

        var collectedTables = entities.Collect();
        var collectedViews = views.Collect();
        var collectedStores = stores.Collect();
        var collectedProjections = projections.Collect();
        var collectedAdHocs = adHocs.Collect();

        // Whether any model needs code at all, tested before computed expressions are resolved.
        var hasWork = collectedTables.Combine(collectedViews).Combine(collectedStores).Combine(collectedProjections).Combine(collectedAdHocs)
            .Select(static (all, _) =>
                all.Left.Left.Left.Left.Any(static entity => entity.IsMapped) || all.Left.Left.Left.Right.Any(static entity => entity.IsMapped)
                || !all.Left.Left.Right.IsEmpty || !all.Left.Right.IsEmpty || !all.Right.IsEmpty);

        // Computed expressions resolve per entity, so editing one entity resolves only that entity again.
        var resolvedTables = entities
            .Select((entity, _) => ComputedExpressionResolver.Resolve(entity, CreateSqlBuilder()))
            .WithTrackingName(ResolvedEntitiesTrackingName);
        var resolvedViews = views
            .Select((entity, _) => ComputedExpressionResolver.Resolve(entity, CreateSqlBuilder()))
            .WithTrackingName(ResolvedViewsTrackingName);
        var collectedResolvedTables = resolvedTables.Collect();
        var collectedResolvedViews = resolvedViews.Collect();

        var gate = ownership.Combine(hasWork).Combine(collectedResolvedTables).Combine(collectedResolvedViews)
            .Combine(collectedProjections).Combine(collectedAdHocs)
            .Select(static (all, _) => MaterializerGate.Create(
                all.Left.Left.Left.Left.Left.Kind == DialectOwnershipKind.Owned && all.Left.Left.Left.Left.Right,
                all.Left.Left.Left.Right, all.Left.Left.Right, all.Left.Right, all.Right))
            .WithTrackingName(MaterializerGateTrackingName);

        // Output nodes report diagnostics in registration order. They are registered in the order the
        // single output stage used to report them, so the diagnostic sequence does not change.
        context.RegisterSourceOutput(entities, static (spc, entity) => Report(spc, entity.Diagnostics));
        context.RegisterSourceOutput(views, static (spc, entity) => Report(spc, entity.Diagnostics));
        context.RegisterSourceOutput(projections, static (spc, projection) => Report(spc, projection.Diagnostics));
        context.RegisterSourceOutput(adHocs, static (spc, adHoc) => Report(spc, adHoc.Diagnostics));
        context.RegisterSourceOutput(ownership.Combine(hasWork), static (spc, pair) => ReportOwnership(spc, pair.Left, pair.Right));
        context.RegisterSourceOutput(resolvedTables.Combine(gate), static (spc, pair) => ReportWhenEmitting(spc, pair.Left, pair.Right));
        context.RegisterSourceOutput(resolvedViews.Combine(gate), static (spc, pair) => ReportWhenEmitting(spc, pair.Left, pair.Right));

        // Each materializer reads only its own model, so each gets its own output. An edit re-emits only
        // the materializers whose model changed.
        var tableMaterializers = resolvedTables.Combine(gate)
            .Select(static (pair, _) => pair.Right.Emits(pair.Left.Entity) ? pair.Left.Entity : null)
            .WithTrackingName(EntityMaterializersTrackingName);
        var viewMaterializers = resolvedViews.Combine(gate)
            .Select(static (pair, _) => pair.Right.Emits(pair.Left.Entity) ? pair.Left.Entity : null)
            .WithTrackingName(ViewMaterializersTrackingName);
        var projectionMaterializers = projections.Combine(gate)
            .Select(static (pair, _) => pair.Right.Emits(pair.Left) ? pair.Left : null)
            .WithTrackingName(ProjectionMaterializersTrackingName);
        var adHocMaterializers = adHocs.Combine(gate)
            .Select(static (pair, _) => pair.Right.Emits(pair.Left) ? pair.Left : null)
            .WithTrackingName(AdHocMaterializersTrackingName);
        context.RegisterSourceOutput(tableMaterializers, (spc, entity) => EmitEntityMaterializer(spc, entity));
        context.RegisterSourceOutput(viewMaterializers, (spc, entity) => EmitEntityMaterializer(spc, entity));
        context.RegisterSourceOutput(projectionMaterializers, (spc, projection) =>
        {
            if (projection is not null)
            {
                ProjectionProcessor.EmitMaterializer(spc, projection, CreateSqlBuilder());
            }
        });
        context.RegisterSourceOutput(adHocMaterializers, (spc, adHoc) =>
        {
            if (adHoc is not null)
            {
                AdHocProcessor.EmitMaterializer(spc, adHoc, CreateSqlBuilder());
            }
        });

        // Junctions, relations, stores, schema and registration still share one output over the whole
        // model, so any model edit re-runs all of them.
        var model = collectedResolvedTables.Combine(collectedResolvedViews).Combine(collectedStores)
            .Combine(collectedProjections).Combine(collectedAdHocs).Combine(ownership).Combine(gate)
            .WithTrackingName(ModelOutputTrackingName);

        context.RegisterSourceOutput(model, (spc, data) => Execute(spc,
            data.Left.Left.Left.Left.Left.Left, data.Left.Left.Left.Left.Left.Right, data.Left.Left.Left.Left.Right,
            data.Left.Left.Left.Right, data.Left.Left.Right, data.Left.Right, data.Right));
    }

    internal const string EntitiesTrackingName = "InquiryEntities";
    internal const string ViewsTrackingName = "InquiryViews";
    internal const string StoresTrackingName = "InquiryStores";
    internal const string ProjectionsTrackingName = "InquiryProjections";
    internal const string AdHocTrackingName = "InquiryAdHocDtos";
    internal const string OwnershipTrackingName = "InquiryDialectOwnership";
    internal const string ResolvedEntitiesTrackingName = "InquiryResolvedEntities";
    internal const string ResolvedViewsTrackingName = "InquiryResolvedViews";
    internal const string MaterializerGateTrackingName = "InquiryMaterializerGate";
    internal const string EntityMaterializersTrackingName = "InquiryEntityMaterializers";
    internal const string ViewMaterializersTrackingName = "InquiryViewMaterializers";
    internal const string ProjectionMaterializersTrackingName = "InquiryProjectionMaterializers";
    internal const string AdHocMaterializersTrackingName = "InquiryAdHocMaterializers";
    internal const string ModelOutputTrackingName = "InquiryModelOutput";

    private void EmitEntityMaterializer(SourceProductionContext context, EntityData? entity)
    {
        if (entity is not null)
        {
            EntityProcessor.EmitMaterializer(context, entity, CreateSqlBuilder());
        }
    }

    private static void Report(SourceProductionContext context, EquatableArray<DiagnosticData> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        }
    }

    private static void ReportWhenEmitting(SourceProductionContext context, ResolvedEntity entity, MaterializerGate gate)
    {
        if (gate.Emit)
        {
            Report(context, entity.Diagnostics);
        }
    }

    private static void ReportOwnership(SourceProductionContext context, DialectOwnership ownership, bool hasWork)
    {
        if (!hasWork)
        {
            return;
        }

        if (ownership.Kind == DialectOwnershipKind.UnknownLeader)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.DialectUnknown, null,
                FormatDialectForDiagnostic(ownership.UnknownDialect), string.IsNullOrWhiteSpace(ownership.AmbiguousDialects) ? "<none>" : ownership.AmbiguousDialects));
        }
        else if (ownership.Kind == DialectOwnershipKind.AmbiguousLeader)
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.DialectAmbiguous, null, ownership.AmbiguousDialects));
        }
    }

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
        ImmutableArray<ResolvedEntity> tables,
        ImmutableArray<ResolvedEntity> views,
        ImmutableArray<StoreData> stores,
        ImmutableArray<ProjectionData> projections,
        ImmutableArray<AdHocData> adHocs,
        DialectOwnership ownership,
        MaterializerGate gate)
    {
        // Discovery, ownership and computed-expression diagnostics are reported by earlier outputs.
        if (!gate.Emit)
        {
            return;
        }

        var sqlBuilder = CreateSqlBuilder();
        var computedInvalidEntityNames = new HashSet<string>(System.StringComparer.Ordinal);
        var resolvedEntities = ImmutableArray.CreateBuilder<EntityData>(tables.Length + views.Length);
        foreach (var resolved in tables.Concat(views))
        {
            resolvedEntities.Add(resolved.Entity);
            if (resolved.ComputedInvalid)
            {
                computedInvalidEntityNames.Add(resolved.Entity.FullyQualifiedName);
            }
        }

        var entities = resolvedEntities.MoveToImmutable();

        // Auto-managed [InquiryManyToMany] junctions become real entities here, before anything looks at
        // the entity set. Each declaring relation is rewritten to name the synthesized junction, so
        // relation validation, the SQL builders, and the schema emitter all see the shape an explicitly
        // mapped junction produces and need no auto-junction branch of their own.
        entities = AutoJunctionSynthesizer.Synthesize(context, entities, sqlBuilder);

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
            // A synthesized junction has no CLR type to materialize into, and nothing reads its rows as
            // entities — the eager loader reads its two key columns straight off the reader. It stays in
            // mappedEntities so relation resolution finds it, and in the schema set so it gets DDL.
            if (entity.IsSynthesizedJunction)
            {
                continue;
            }

            // Materializers have their own outputs. A name declared twice is emitted here once, as
            // before, because two outputs cannot add the same hint name.
            entityRegistrations.Add(gate.IsDuplicateEntity(entity.FullyQualifiedName)
                ? EntityProcessor.EmitMaterializer(context, entity, sqlBuilder)
                : new EntityRegistration(entity.FullyQualifiedName, entity.ClassMaterializerFullName));
        }

        // projection materializers register like entity materializers (same
        // IInquiryEntityMaterializer<T> contract), so they share the registration set.
        foreach (var projection in mappedProjections.Values)
        {
            entityRegistrations.Add(gate.IsDuplicateProjection(projection.FullyQualifiedName)
                ? ProjectionProcessor.EmitMaterializer(context, projection, sqlBuilder)
                : new EntityRegistration(projection.FullyQualifiedName, projection.ClassMaterializerFullName));
        }

        // ad-hoc DTO materializers share the registration set too. They are dialect-independent
        // (reads only — no SQL is ever generated for them), so the dialect owner emits them like
        // the other materializers.
        foreach (var adHoc in mappedAdHocs.Values)
        {
            entityRegistrations.Add(gate.IsDuplicateAdHoc(adHoc.FullyQualifiedName)
                ? AdHocProcessor.EmitMaterializer(context, adHoc, sqlBuilder)
                : new EntityRegistration(adHoc.FullyQualifiedName, adHoc.ClassMaterializerFullName));
        }

        var storeRegistrations = ImmutableArray.CreateBuilder<StoreRegistration>();
        var collectionArtifacts = ImmutableArray.CreateBuilder<CollectionParameterArtifact>();

        SchemaEmitter.ReportRotatingTokenStorageDiagnostics(context, entities.Where(static e => e.IsMapped).ToArray(), sqlBuilder);

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
    private static void ValidateRelations(SourceProductionContext context, Dictionary<string, EntityData> mappedEntities)
    {
        foreach (var entity in mappedEntities.Values)
        {
            foreach (var relation in entity.Relations)
            {
                if (relation.IsManyToMany)
                {
                    // A many-to-many association must be a collection nav whose junction and related
                    // entities are both mapped, and the junction must name one foreign-key property per
                    // key column of the related entity — one for a single-column key, or one per column
                    // for a composite key, paired by position. Each reason reports its own diagnostic so
                    // the message can name the offending type, property, or arity.
                    if (DescribeManyToManyFailure(mappedEntities, entity, relation) is { } diagnostic)
                    {
                        context.ReportDiagnostic(diagnostic);
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
                            entity.Name, relation.PropertyName, relation.ForeignKeyProperty, owner.Name,
                            DescribeNearMiss(owner, relation.ForeignKeyProperty)));
                    }
                }

                if (child.Keys.Count > 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.RelationCompositeChildKey, location,
                        entity.Name, relation.PropertyName, child.Name, child.Keys.Count));
                }
                else if (StoreProcessor.FindKeyStorageMismatch(entity, relation, child, junction: null) is { } mismatch)
                {
                    context.ReportDiagnostic(CreateKeyStorageMismatch(location, entity, relation, mismatch));
                }
            }
        }
    }

    private static ColumnData? FindEntityColumn(EntityData entity, string propertyName)
        => StoreProcessor.FindRelationColumn(entity, propertyName);

    /// <summary>
    /// The trailing sentence of INQ040 and INQ088. When a column name or a different casing would have
    /// matched, the message names the property to use, because the relation needs the exact property name.
    /// </summary>
    private static string DescribeNearMiss(EntityData entity, string name)
    {
        // A property match wins over a column match: when one property is mapped to another property's
        // name, the column match would suggest the wrong property.
        var columns = entity.Columns.AsImmutableArray();
        var match = columns.FirstOrDefault(column => string.Equals(column.PropertyName, name, StringComparison.OrdinalIgnoreCase))
            ?? columns.FirstOrDefault(column => string.Equals(column.ColumnName, name, StringComparison.OrdinalIgnoreCase));
        return match is null
            ? string.Empty
            : $"Foreign keys are matched by exact property name, not by column name or another casing: use '{match.PropertyName}'.";
    }

    private static Diagnostic CreateKeyStorageMismatch(
        Location? location, EntityData entity, RelationData relation, StoreProcessor.RelationKeyPair pair)
        => Diagnostic.Create(
            InquiryDiagnosticDescriptors.RelationKeyStorageMismatch, location,
            entity.Name, relation.PropertyName,
            pair.ForeignKeyOwner.Name + "." + pair.ForeignKey.PropertyName, DescribeKeyStorage(pair.ForeignKey),
            pair.KeyOwner.Name + "." + pair.Key.PropertyName, DescribeKeyStorage(pair.Key));

    private static string DescribeKeyStorage(ColumnData column)
    {
        if (column.Converter is { } converter)
        {
            var name = converter.ConverterTypeDisplay;
            return "converter " + (name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring("global::".Length) : name);
        }

        if (column.EnumAsString)
        {
            return "enum stored as text";
        }

        return column.Type.IsEnum ? "enum stored as a number" : "no converter";
    }

    /// <summary>
    /// Returns the diagnostic describing why a many-to-many relation is unusable, or <see langword="null"/>
    /// when it is valid. Checks run outermost-first — shape, then resolvability, then pairing, then declared
    /// targets and stored representation — so the
    /// reported reason is the one a user has to fix first rather than a downstream consequence of it.
    /// </summary>
    private static Diagnostic? DescribeManyToManyFailure(
        Dictionary<string, EntityData> mappedEntities, EntityData entity, RelationData relation)
    {
        var location = relation.Location?.ToLocation();

        // Not a collection navigation: the same INQ063 both forms report, and checked before the
        // auto-junction suppression below — the synthesizer deliberately stays silent on this one so it
        // is not reported as an auto-managed-specific failure, which would leave it reported nowhere.
        if (!relation.IsCollection)
        {
            return Diagnostic.Create(
                InquiryDiagnosticDescriptors.ManyToManyInvalid, location, entity.Name, relation.PropertyName);
        }

        // An auto-managed relation still holding no junction name is one the synthesizer rejected — it
        // already reported INQ090 saying why. Reporting again here would add INQ087 naming a junction
        // type "(unknown)" that the user never wrote.
        if (relation.IsAutoJunction && relation.JunctionEntityFullyQualifiedName is null)
        {
            return null;
        }

        if (relation.JunctionEntityFullyQualifiedName is not { } junctionFqn ||
            !mappedEntities.TryGetValue(junctionFqn, out var junction))
        {
            return Diagnostic.Create(
                InquiryDiagnosticDescriptors.ManyToManyTypeNotMapped, location,
                entity.Name, relation.PropertyName, Simplify(relation.JunctionEntityFullyQualifiedName));
        }

        if (!mappedEntities.TryGetValue(relation.ChildEntityFullyQualifiedName, out var child))
        {
            return Diagnostic.Create(
                InquiryDiagnosticDescriptors.ManyToManyTypeNotMapped, location,
                entity.Name, relation.PropertyName, Simplify(relation.ChildEntityFullyQualifiedName));
        }

        var parentForeignKey = relation.JunctionParentForeignKeyProperty ?? string.Empty;
        if (FindEntityColumn(junction, parentForeignKey) is null)
        {
            return Diagnostic.Create(
                InquiryDiagnosticDescriptors.ManyToManyForeignKeyNotMapped, location,
                entity.Name, relation.PropertyName, parentForeignKey, junction.Name,
                DescribeNearMiss(junction, parentForeignKey));
        }

        // An unresolvable child foreign-key name is a naming problem, not a pairing one, so it reports
        // INQ088 naming the string — checked before the arity/type rules, which would otherwise mask it.
        foreach (var name in relation.JunctionChildForeignKeyProperties)
        {
            if (FindEntityColumn(junction, name) is null)
            {
                return Diagnostic.Create(
                    InquiryDiagnosticDescriptors.ManyToManyForeignKeyNotMapped, location,
                    entity.Name, relation.PropertyName, name, junction.Name,
                    DescribeNearMiss(junction, name));
            }
        }

        if (!StoreProcessor.JunctionForeignKeysPairWithChildKeys(
                junction, child, relation.JunctionChildForeignKeyProperties))
        {
            var named = relation.JunctionChildForeignKeyProperties.Count;
            return Diagnostic.Create(
                InquiryDiagnosticDescriptors.ManyToManyChildKeyPairingInvalid, location,
                entity.Name, relation.PropertyName,
                named, named == 1 ? "y" : "ies",
                child.Name, child.Keys.Count, child.Keys.Count == 1 ? string.Empty : "s",
                DescribePairing(junction, child, relation.JunctionChildForeignKeyProperties));
        }

        if (StoreProcessor.DescribeJunctionTargetConflict(entity, relation, junction, child) is { } conflict)
        {
            return Diagnostic.Create(
                InquiryDiagnosticDescriptors.ManyToManyForeignKeyTargetConflict, location,
                entity.Name, relation.PropertyName, conflict);
        }

        if (StoreProcessor.FindKeyStorageMismatch(entity, relation, child, junction) is { } mismatch)
        {
            return CreateKeyStorageMismatch(location, entity, relation, mismatch);
        }

        return null;

        // Drops the namespace for readability. Only the segment BEFORE any type argument list is scanned:
        // the last '.' in "global::System.Collections.Generic.List<global::Demo.Product>" sits inside the
        // argument, so searching the whole string would render the type as "Product>".
        static string Simplify(string? fullyQualifiedName)
        {
            if (string.IsNullOrEmpty(fullyQualifiedName))
            {
                return "(unknown)";
            }

            var name = fullyQualifiedName!;

            // Only the segment before any type-argument list is scanned: the last '.' in
            // "…Generic.List<global::Demo.Product>" sits inside the argument. A name that OPENS with
            // '<' leaves nothing to scan — LastIndexOf would be handed a -1 start index and throw,
            // turning a diagnostic into a generator crash — so it is returned whole.
            var openAngle = name.IndexOf('<');
            if (openAngle == 0)
            {
                return name;
            }

            var searchEnd = openAngle < 0 ? name.Length : openAngle;
            var lastDot = name.LastIndexOf('.', searchEnd - 1, searchEnd);
            return lastDot < 0 ? name : name.Substring(lastDot + 1);
        }
    }

    /// <summary>
    /// The trailing sentence of INQ089 — names the first specific mismatch when the counts do line up, so
    /// a transposed list says which position is wrong instead of only restating the rule.
    /// </summary>
    private static string DescribePairing(
        EntityData junction, EntityData child, EquatableArray<string> foreignKeyNames)
    {
        if (foreignKeyNames.Count != child.Keys.Count)
        {
            return string.Empty;
        }

        for (var i = 0; i < foreignKeyNames.Count; i++)
        {
            for (var j = i + 1; j < foreignKeyNames.Count; j++)
            {
                if (string.Equals(foreignKeyNames[i], foreignKeyNames[j], StringComparison.Ordinal))
                {
                    return $"'{foreignKeyNames[i]}' is named more than once.";
                }
            }

            var foreignKey = FindEntityColumn(junction, foreignKeyNames[i]);
            var keyColumn = child.Keys[i];
            if (foreignKey is not null &&
                !string.Equals(
                    foreignKey.Type.NonNullableDisplayName, keyColumn.Type.NonNullableDisplayName, StringComparison.Ordinal))
            {
                return $"'{foreignKeyNames[i]}' ({foreignKey.Type.NonNullableDisplayName}) is paired with key column "
                    + $"'{keyColumn.PropertyName}' ({keyColumn.Type.NonNullableDisplayName}); the names may be in the wrong order.";
            }
        }

        return string.Empty;
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
                || !key.StartsWith("Inquiry.SchemaManifest.", System.StringComparison.Ordinal))
            {
                continue;
            }

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
