using Inquiry.Entities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Inquiry.Generators.Tests;

public sealed class InquiryGeneratorTests
{
    [Fact]
    public void GeneratesConcreteStoreForValidCrudStore()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; } = Guid.NewGuid();

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn]
                public bool IsActive { get; set; } = true;
            }

            public partial class OrganizationStore : InquiryStore<Organization>
            {

                [InquirySelectAll]
                public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);

                [InquirySelectOneByKey]
                public partial Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

                [InquirySelectAllByField("IsActive")]
                public partial IAsyncEnumerable<Organization> SelectByIsActiveAsync(bool isActive, CancellationToken cancellationToken = default);

                [InquiryInsert]
                public partial Task<int> InsertAsync(Organization organization, CancellationToken cancellationToken = default);

                [InquiryInsert(ReturnEntity = true)]
                public partial Task<Organization?> InsertReturningAsync(Organization organization, CancellationToken cancellationToken = default);

                [InquiryUpdate]
                public partial Task<bool> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);

                [InquiryDeleteOneByKey]
                public partial Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("OrganizationStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        Assert.Contains("partial class OrganizationStore", generatedText);
        Assert.Contains("public OrganizationStore(global::Inquiry.IInquiry inquiry)", generatedText);

        // All SQL is emitted as const string fields baked at generation time. No runtime
        // dialect call, no _ctx, no _columns array survives in the generated store.
        Assert.Contains("private const string _sqlSelectAll = \"SELECT \\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\" FROM \\\"TOrganization\\\"\";", generatedText);
        Assert.Contains("private const string _sqlSelectByKey = \"SELECT \\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\" FROM \\\"TOrganization\\\" WHERE \\\"Key\\\" = @Key\";", generatedText);
        Assert.Contains("private const string _sqlInsert = \"INSERT INTO \\\"TOrganization\\\" (\\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\") VALUES (@Key, @Name, @IsActive)\";", generatedText);
        Assert.Contains("private const string _sqlInsertReturning = \"INSERT INTO \\\"TOrganization\\\" (\\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\") VALUES (@Key, @Name, @IsActive) RETURNING \\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\"\";", generatedText);
        Assert.Contains("private const string _sqlUpdate = \"UPDATE \\\"TOrganization\\\" SET \\\"Name\\\" = @Name, \\\"IsActive\\\" = @IsActive WHERE \\\"Key\\\" = @Key\";", generatedText);
        Assert.Contains("private const string _sqlDeleteByKey = \"DELETE FROM \\\"TOrganization\\\" WHERE \\\"Key\\\" = @Key\";", generatedText);
        Assert.Contains("private const string _sqlSelectBy_IsActive = \"SELECT \\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\" FROM \\\"TOrganization\\\" WHERE \\\"IsActive\\\" = @IsActive\";", generatedText);

        // Read paths dispatch through the struct-materializer overloads so the JIT can specialize
        // per concrete TMaterializer and inline the Materialize call. Streaming SelectAll /
        // SelectAllByField (IAsyncEnumerable) use the 2-arg struct QueryAsync overload.
        Assert.Contains("Inquiry.QueryAsync<global::Demo.Organization, global::Demo.OrganizationInquiryEntityStructMaterializer>", generatedText);

        // SelectByKey binds the key via the allocation-free static-delegate fast path: a 3-arg
        // QuerySingleOrDefaultAsync<TEntity, TArgs, TMaterializer> with an inline static binder —
        // no InquiryParameter[] / InquiryCommand allocation per call.
        Assert.Contains("Inquiry.QuerySingleOrDefaultAsync<global::Demo.Organization, global::System.Guid, global::Demo.OrganizationInquiryEntityStructMaterializer>(", generatedText);
        Assert.Contains("static (_cmd, _key) =>", generatedText);
        Assert.Contains("_p0.ParameterName = \"@Key\";", generatedText);

        // Returning InsertReturning binds the whole entity via the same fast path (TArgs = entity).
        Assert.Contains("Inquiry.QuerySingleOrDefaultAsync<global::Demo.Organization, global::Demo.Organization, global::Demo.OrganizationInquiryEntityStructMaterializer>(", generatedText);
        Assert.Contains("static (_cmd, _e) =>", generatedText);

        // Non-returning Insert/Update/Delete use the allocation-free ExecuteAsync<TArgs> fast path.
        Assert.Contains("Inquiry.ExecuteAsync", generatedText);

        // Streaming SelectAllByField (IAsyncEnumerable, no buffered list) keeps the InquiryParameter[]
        // path — there is no fast streaming overload.
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@IsActive\", isActive)", generatedText);
        Assert.DoesNotContain("InquirySqlDialect", generatedText);
        Assert.DoesNotContain("CreateContext", generatedText);
        Assert.DoesNotContain("BuildSelectAllSql", generatedText);
        Assert.DoesNotContain("BuildInsertSql", generatedText);
        Assert.DoesNotContain("InquirySqlColumn", generatedText);
        Assert.DoesNotContain("_columns", generatedText);
        Assert.DoesNotContain("_sqlStatements", generatedText);
        Assert.DoesNotContain("InquirySqlStatementBuilder", generatedText);
        Assert.DoesNotContain("InquirySqlStatementSet", generatedText);
        Assert.DoesNotContain("AddParameter", generatedText);
        Assert.DoesNotContain("ConnectionFactory.OpenConnectionAsync", generatedText);
        Assert.DoesNotContain("CreateCommand()", generatedText);
        Assert.DoesNotContain("ExecuteReaderAsync", generatedText);
        Assert.DoesNotContain("ExecuteNonQueryAsync", generatedText);

        var generatedEntity = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("Organization.InquiryEntity.g.cs", StringComparison.Ordinal));
        var generatedEntityText = generatedEntity.GetText().ToString();

        Assert.Contains("IInquiryEntityMaterializer<global::Demo.Organization>", generatedEntityText);

        // Both materializer flavours emitted: class for ad-hoc IInquiry queries (DI singleton),
        // struct for the generated-store hot path (`default(T)` passed inline so the JIT can
        // specialize the pipeline body per concrete TMaterializer).
        Assert.Contains("internal sealed class OrganizationInquiryEntityMaterializer", generatedEntityText);
        Assert.Contains("internal readonly struct OrganizationInquiryEntityStructMaterializer", generatedEntityText);

        var generatedServices = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("InquiryGeneratedServiceRegistration.g.cs", StringComparison.Ordinal));
        var generatedServicesText = generatedServices.GetText().ToString();

        Assert.Contains("IInquiryServiceRegistration", generatedServicesText);
        Assert.Contains("void AddServices(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)", generatedServicesText);
        Assert.DoesNotContain("IInquiryEntityMetadata", generatedServicesText);
        Assert.Contains("TryAddSingleton<global::Inquiry.Materialization.IInquiryEntityMaterializer<global::Demo.Organization>, global::Demo.OrganizationInquiryEntityMaterializer>", generatedServicesText);
        Assert.Contains("TryAddScoped<global::Demo.OrganizationStore>", generatedServicesText);
    }

    [Fact]
    public void ForeignKeyAttributeMapsAsRegularColumn()
    {
        // Verifies that [InquiryForeignKey] participates in column discovery the same way
        // [InquiryColumn] does: 2-arg form defaults the column name to the property name,
        // 3-arg form honors the explicit local column name.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TUser")]
            public sealed class User
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryForeignKey("TOrganization", "Key")]
                public Guid TOrganizationKey { get; set; }

                [InquiryForeignKey("AltColumnName", "TOther", "Key")]
                public Guid OtherKey { get; set; }
            }

            public partial class UserStore : InquiryStore<User>
            {

                [InquirySelectAll]
                public partial IAsyncEnumerable<User> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("UserStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // FK columns flow into the baked SELECT projection — 2-arg form uses the property name
        // as the column name, 3-arg form uses the explicit local column name.
        Assert.Contains("private const string _sqlSelectAll = \"SELECT \\\"Key\\\", \\\"TOrganizationKey\\\", \\\"AltColumnName\\\" FROM \\\"TUser\\\"\";", generatedText);
    }

    [Fact]
    public void DefaultedColumnIsMarkedAndExcludedFromInsertParameters()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TWidget")]
            public sealed class Widget
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn(UseDatabaseDefault = true)]
                public DateTime CreatedAt { get; set; }
            }

            public partial class WidgetStore : InquiryStore<Widget>
            {

                [InquiryInsert]
                public partial Task<int> InsertAsync(Widget widget, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // Defaulted columns appear in SELECT projections (the database fills them in) but are
        // omitted from INSERT column/value lists and from the parameter array.
        Assert.Contains("private const string _sqlInsert = \"INSERT INTO \\\"TWidget\\\" (\\\"Key\\\", \\\"Name\\\") VALUES (@Key, @Name)\";", generatedText);
        Assert.DoesNotContain("new global::Inquiry.Parameters.InquiryParameter(\"@CreatedAt\", widget.CreatedAt)", generatedText);
    }

    [Fact]
    public void FastPathBinderCoercesEnumColumnsToUnderlyingPrimitive()
    {
        // Mirrors the historical InquiryParameterBinder behaviour: enum values must be assigned
        // as their underlying primitive (long/int/short/...), not as boxed enum instances, so
        // providers like Npgsql that reject unmapped enums see the value they expect.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            public enum Status { Inactive = 0, Active = 1 }
            public enum BigStatus : long { A = 0, B = 1 }

            [InquiryTable("TWidget")]
            public sealed class Widget
            {
                [InquiryKey]
                public Status Key { get; set; }

                [InquiryColumn]
                public Status PlainStatus { get; set; }

                [InquiryColumn]
                public Status? NullableStatus { get; set; }

                [InquiryColumn]
                public BigStatus BigStatus { get; set; }
            }

            public partial class WidgetStore : InquiryStore<Widget>
            {
                [InquiryInsert]
                public partial Task<int> InsertAsync(Widget widget, CancellationToken cancellationToken = default);

                [InquiryDeleteOneByKey]
                public partial Task<bool> DeleteByKeyAsync(Status key, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // Non-nullable Int32-underlying enum → cast to int.
        Assert.Contains("(object)(int)_e.PlainStatus", generatedText);
        // Nullable Int32-underlying enum → HasValue-checked cast to int, DBNull otherwise.
        Assert.Contains("_e.NullableStatus.HasValue ? (object)(int)_e.NullableStatus.Value : global::System.DBNull.Value", generatedText);
        // Non-nullable Int64-underlying enum → cast to long.
        Assert.Contains("(object)(long)_e.BigStatus", generatedText);
        // Key parameter on DeleteByKey is also enum-typed → coerce on the lambda arg as well.
        Assert.Contains("(object)(int)_key", generatedText);
        // The raw "(object?)_e.PlainStatus ?? DBNull" form must not appear for enum columns.
        Assert.DoesNotContain("(object?)_e.PlainStatus ?? global::System.DBNull.Value", generatedText);
    }

    [Fact]
    public void InsertWithOnlyDatabaseSuppliedColumnsEmitsEmptyBindLambda()
    {
        // When every column is database-generated, the fast-path Inquiry.ExecuteAsync still
        // receives the entity + a static binder, but the binder body adds no parameters.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TWidget")]
            public sealed class Widget
            {
                [InquiryKey(IsGenerated = true)]
                public int Id { get; set; }
            }

            public partial class WidgetStore : InquiryStore<Widget>
            {

                [InquiryInsert]
                public partial Task<int> InsertAsync(Widget widget, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // Fast path takes (sql, args, static lambda, ct); no InquiryParameter[] or CreateParameter call.
        Assert.Contains("Inquiry.ExecuteAsync(", generatedText);
        Assert.Contains("static (_cmd, _e) =>", generatedText);
        Assert.DoesNotContain("_cmd.CreateParameter()", generatedText);
        Assert.DoesNotContain("new global::Inquiry.Parameters.InquiryParameter(\"@Id\"", generatedText);
        Assert.DoesNotContain("global::System.Array.Empty<global::Inquiry.Parameters.InquiryParameter>()", generatedText);
    }

    [Fact]
    public void TableNameDefaultsToEntityTypeNameWhenAttributeIsParameterless()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable]
            public sealed class Widget
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            public partial class WidgetStore : InquiryStore<Widget>
            {

                [InquirySelectAll]
                public partial IAsyncEnumerable<Widget> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // Table name defaults to the entity type name when [InquiryTable] has no arguments.
        Assert.Contains("private const string _sqlSelectAll = \"SELECT \\\"Key\\\", \\\"Name\\\" FROM \\\"Widget\\\"\";", generatedText);
    }

    [Theory]
    [InlineData("[InquirySelectAll]", "public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);", "_sqlSelectAll")]
    [InlineData("[InquirySelectOneByKey]", "public partial Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);", "_sqlSelectByKey")]
    [InlineData("[InquirySelectAllByField(\"IsActive\")]", "public partial IAsyncEnumerable<Organization> SelectByIsActiveAsync(bool isActive, CancellationToken cancellationToken = default);", "_sqlSelectBy_IsActive")]
    [InlineData("[InquiryInsert]", "public partial Task<int> InsertAsync(Organization organization, CancellationToken cancellationToken = default);", "_sqlInsert")]
    [InlineData("[InquiryUpdate]", "public partial Task<bool> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);", "_sqlUpdate")]
    [InlineData("[InquiryDeleteOneByKey]", "public partial Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);", "_sqlDeleteByKey")]
    public void GeneratesStoreMethodForEachOperationSlice(string attribute, string methodDeclaration, string expectedStatement)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn]
                public bool IsActive { get; set; }
            }

            public partial class OrganizationStore : InquiryStore<Organization>
            {

                {{attribute}}
                {{methodDeclaration}}
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("OrganizationStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        Assert.Contains(expectedStatement, generatedText);
    }

    [Fact]
    public void ConsumerCompilationNeedsOnlyInquiryRuntimeTypes()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }
            }

            public partial class OrganizationStore : InquiryStore<Organization>
            {

                [InquirySelectAll]
                public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(errors);
        Assert.Contains(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("OrganizationStore.InquiryStore.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void EagerLoadGroupsByTypedKeyNotBoxedObject()
    {
        // The eager loader joins parents and children in memory via dictionaries. Those dictionaries
        // must be keyed by the FK/key's non-nullable type (here int), not object — otherwise every
        // value-type FK is boxed twice per row (once on insert, once on lookup).
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Categories")]
            public sealed class Category
            {
                [InquiryKey("CategoryID", IsGenerated = true)]
                public int? CategoryID { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            [InquiryTable("Products")]
            public sealed class Product
            {
                [InquiryKey("ProductID", IsGenerated = true)]
                public int? ProductID { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;

                [InquiryForeignKey("CategoryID", "Categories", "CategoryID")]
                public int? CategoryID { get; set; }

                [InquiryRelation(nameof(CategoryID))]
                public Category? Category { get; set; }
            }

            public partial class ProductStore : InquiryStore<Product>
            {
                [InquirySelectAllEager]
                public partial IAsyncEnumerable<Product> SelectAllWithCategoryAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        // The generator itself runs clean. We intentionally do NOT assert the *output* compiles in
        // this minimal harness: the eager loader emits `IAsyncEnumerable<T>.ConfigureAwait(false)`,
        // whose extension only resolves with an implicit/global `using System.Threading.Tasks` that
        // real consumer projects (and Inquiry.Sqlite.Tests) have but this bare harness does not.
        // End-to-end compilation + behavior of the eager path is covered by Inquiry.Sqlite.Tests
        // (EagerLoadingIntegrationTests) and the full solution build.
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("ProductStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // The many-to-one join dictionary exists, is NOT keyed by object, and the value-type FK is
        // unwrapped with .Value instead of boxed via an (object) cast.
        Assert.Contains("_parents_Category", generatedText);
        Assert.Contains("_entity.CategoryID.Value", generatedText);
        Assert.DoesNotContain("Dictionary<object", generatedText);
        Assert.DoesNotContain("(object)", generatedText);
    }

    [Fact]
    public void ReportsDiagnosticForMissingKey()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ001");
    }

    [Fact]
    public void AcceptsMultipleKeysWhenNoneAreGenerated()
    {
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key1 { get; set; }

                [InquiryKey]
                public Guid Key2 { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsDiagnosticForCompositeKeyContainingGeneratedColumn()
    {
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TItem")]
            public sealed class Item
            {
                [InquiryKey(IsGenerated = true)]
                public int? Id { get; set; }

                [InquiryKey]
                public Guid Tag { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ011");
    }

    [Fact]
    public void ReportsDiagnosticForDuplicateColumnName()
    {
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn("Name")]
                public string A { get; set; } = string.Empty;

                [InquiryColumn("Name")]
                public string B { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ002");
    }

    [Fact]
    public void UnknownPropertyTypeRoutesThroughGetFieldValueFallback()
    {
        // Per the permissive type policy: the generator does NOT reject unknown CLR types.
        // The materializer emits GetFieldValue<T>(i) and lets the provider decide at runtime.
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            public sealed class CustomThing { }

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn]
                public CustomThing Thing { get; set; } = new();
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);

        var generatedEntity = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("Organization.InquiryEntity.g.cs", StringComparison.Ordinal));
        Assert.Contains("reader.GetFieldValue<global::Demo.CustomThing>(1)", generatedEntity.GetText().ToString());
    }

    [Fact]
    public void EnumPropertyReadsUnderlyingIntegerAndCasts()
    {
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            public enum Status { Inactive = 0, Active = 1 }
            public enum BigStatus : long { A = 0, B = 1 }

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn]
                public Status Status { get; set; }

                [InquiryColumn]
                public Status? NullableStatus { get; set; }

                [InquiryColumn]
                public BigStatus BigStatus { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);

        var generatedEntity = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("Organization.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = generatedEntity.GetText().ToString();

        Assert.Contains("Status = (global::Demo.Status)reader.GetInt32(1)", text);
        Assert.Contains("NullableStatus = reader.IsDBNull(2) ? (global::Demo.Status?)null : (global::Demo.Status)reader.GetInt32(2)", text);
        Assert.Contains("BigStatus = (global::Demo.BigStatus)reader.GetInt64(3)", text);
    }

    [Fact]
    public void NewPrimitiveAndModernTypesAreSupported()
    {
        // Verifies byte/char get specialized DbDataReader calls and that DateOnly/TimeOnly/
        // TimeSpan/uint/ushort/ulong/sbyte route through GetFieldValue<T>.
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TWidget")]
            public sealed class Widget
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn] public byte Flags { get; set; }
                [InquiryColumn] public sbyte Signed { get; set; }
                [InquiryColumn] public char Initial { get; set; }
                [InquiryColumn] public ushort UShortValue { get; set; }
                [InquiryColumn] public uint UIntValue { get; set; }
                [InquiryColumn] public ulong ULongValue { get; set; }
                [InquiryColumn] public DateOnly OnlyDate { get; set; }
                [InquiryColumn] public TimeOnly OnlyTime { get; set; }
                [InquiryColumn] public TimeSpan Span { get; set; }
                [InquiryColumn] public DateOnly? OnlyDateNullable { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);

        var generatedEntity = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("Widget.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = generatedEntity.GetText().ToString();

        Assert.Contains("Flags = reader.GetByte(", text);
        Assert.Contains("Initial = reader.GetChar(", text);
        Assert.Contains("Signed = reader.GetFieldValue<sbyte>(", text);
        Assert.Contains("UShortValue = reader.GetFieldValue<ushort>(", text);
        Assert.Contains("UIntValue = reader.GetFieldValue<uint>(", text);
        Assert.Contains("ULongValue = reader.GetFieldValue<ulong>(", text);
        Assert.Contains("OnlyDate = reader.GetFieldValue<global::System.DateOnly>(", text);
        Assert.Contains("OnlyTime = reader.GetFieldValue<global::System.TimeOnly>(", text);
        Assert.Contains("Span = reader.GetFieldValue<global::System.TimeSpan>(", text);
        Assert.Contains("OnlyDateNullable = reader.IsDBNull(", text);
    }

    [Fact]
    public void ReportsDiagnosticForNonPartialStore()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }
            }

            public abstract class OrganizationStore : InquiryStore<Organization>
            {

                [InquirySelectAll]
                public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ004");
    }

    [Fact]
    public void ReportsDiagnosticForUnknownField()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }
            }

            public partial class OrganizationStore : InquiryStore<Organization>
            {

                [InquirySelectAllByField("DoesNotExist")]
                public partial IAsyncEnumerable<Organization> SelectByMissingAsync(string value, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ007");
    }

    [Fact]
    public void ReportsDiagnosticForStoreEntityNotMapped()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Stores;

            namespace Demo;

            public sealed class Unmapped
            {
                public Guid Key { get; set; }
            }

            public partial class UnmappedStore : InquiryStore<Unmapped>
            {

                [InquirySelectAll]
                public partial IAsyncEnumerable<Unmapped> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ008");
    }

    [Fact]
    public void ReportsDiagnosticForPropertyWithoutPublicSetter()
    {
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn]
                public string ReadOnly { get; } = string.Empty;
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ009");
    }

    [Fact]
    public void ReportsDiagnosticForNonPartialStoreMethod()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }
            }

            public partial class OrganizationStore : InquiryStore<Organization>
            {

                [InquirySelectAll]
                public IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default)
                    => throw new System.NotImplementedException();
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ010");
    }

    [Fact]
    public void ReportsDiagnosticForNestedStore()
    {
        // The generator emits its partial at the namespace level, so a store nested inside
        // another type would land at the wrong scope and the partial method definitions
        // would have no implementations. Reject up front.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }
            }

            public static class Outer
            {
                public partial class OrganizationStore : InquiryStore<Organization>
                {
                    [InquirySelectAll]
                    public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ016");
    }

    [Fact]
    public void ReportsDiagnosticForAbstractStore()
    {
        // The generator now emits the constructor onto the user's class, so an abstract
        // store would still be abstract after combining the partials and DI cannot instantiate
        // it. Reject before emitting.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }
            }

            public abstract partial class OrganizationStore : InquiryStore<Organization>
            {
                [InquirySelectAll]
                public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ017");
    }

    [Fact]
    public void SqlServerDialectEmitsBracketedIdentifiersAndMergeUpsert()
    {
        // Spot-checks the SqlServerSqlBuilder output by exercising the full CRUD surface
        // including INSERT/UPDATE returning (OUTPUT INSERTED.*) and the MERGE-style upsert.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class OrganizationStore : InquiryStore<Organization>
            {
                [InquirySelectAll]
                public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);

                [InquiryInsert(ReturnEntity = true)]
                public partial Task<Organization?> InsertReturningAsync(Organization o, CancellationToken cancellationToken = default);

                [InquiryUpdate(ReturnEntity = true)]
                public partial Task<Organization?> UpdateReturningAsync(Organization o, CancellationToken cancellationToken = default);

                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Organization o, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("OrganizationStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // Bracket-quoted identifiers, OUTPUT INSERTED.* for returning mutations, and MERGE upsert.
        Assert.Contains("private const string _sqlSelectAll = \"SELECT [Key], [Name] FROM [TOrganization]\";", generatedText);
        Assert.Contains("[Key], [Name]) OUTPUT INSERTED.[Key], INSERTED.[Name] VALUES (@Key, @Name)", generatedText);
        Assert.Contains("OUTPUT INSERTED.[Key], INSERTED.[Name] WHERE [Key] = @Key", generatedText);
        Assert.Contains("MERGE INTO [TOrganization] AS target", generatedText);
        Assert.Contains("WHEN MATCHED THEN UPDATE SET [Name] = @Name", generatedText);
    }

    [Fact]
    public void PostgreSqlDialectEmitsDoubleQuotedIdentifiersAndOnConflictUpsert()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class OrganizationStore : InquiryStore<Organization>
            {
                [InquirySelectAll]
                public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);

                [InquiryInsert(ReturnEntity = true)]
                public partial Task<Organization?> InsertReturningAsync(Organization o, CancellationToken cancellationToken = default);

                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Organization o, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "PostgreSql");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("OrganizationStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // Double-quoted identifiers, RETURNING for insert-returning, ON CONFLICT DO UPDATE upsert.
        Assert.Contains("private const string _sqlSelectAll = \"SELECT \\\"Key\\\", \\\"Name\\\" FROM \\\"TOrganization\\\"\";", generatedText);
        Assert.Contains("VALUES (@Key, @Name) RETURNING \\\"Key\\\", \\\"Name\\\"", generatedText);
        Assert.Contains("ON CONFLICT (\\\"Key\\\") DO UPDATE SET \\\"Name\\\" = @Name", generatedText);
    }

    private const string PredicateEntitySource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Products")]
        public sealed class Product
        {
            [InquiryKey]
            public Guid Id { get; set; }

            [InquiryColumn]
            public string ProductName { get; set; } = string.Empty;

            [InquiryColumn]
            public decimal? UnitPrice { get; set; }

            [InquiryColumn]
            public short? UnitsInStock { get; set; }

            [InquiryColumn]
            public int? CategoryId { get; set; }

            [InquiryColumn]
            public bool Discontinued { get; set; }
        }

        public partial class ProductStore : InquiryStore<Product>
        {
            STORE_METHODS
        }
        """;

    private static string PredicateSource(string storeMethods)
        => PredicateEntitySource.Replace("STORE_METHODS", storeMethods);

    [Fact]
    public void SelectAllByPredicateEmitsComparisonAndLikeWhereClause()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("UnitPrice", Compare.GreaterThanOrEqual)]
                [InquiryWhere("ProductName", Compare.Like)]
                public partial Task<IReadOnlyList<Product>> SearchAsync(decimal? minPrice, string namePattern, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        // Comparison + LIKE compose with AND; parameter names derive from the resolved column.
        Assert.Contains(
            "private const string _sqlPredicate_SearchAsync = \"SELECT \\\"Id\\\", \\\"ProductName\\\", \\\"UnitPrice\\\", \\\"UnitsInStock\\\", \\\"CategoryId\\\", \\\"Discontinued\\\" FROM \\\"Products\\\" WHERE \\\"UnitPrice\\\" >= @UnitPrice AND \\\"ProductName\\\" LIKE @ProductName\";",
            generatedText);

        // Scalar predicates bind through a DbCommand binder with the same column-derived names.
        Assert.Contains("_p0.ParameterName = \"@UnitPrice\";", generatedText);
        Assert.Contains("_p1.ParameterName = \"@ProductName\";", generatedText);
    }

    [Fact]
    public void SelectAllByPredicateEmitsBetweenWithLoHiParameters()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("UnitsInStock", Compare.Between)]
                public partial Task<IReadOnlyList<Product>> InStockRangeAsync(short? low, short? high, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        Assert.Contains(
            "WHERE \\\"UnitsInStock\\\" BETWEEN @UnitsInStock_lo AND @UnitsInStock_hi\";",
            generatedText);
        Assert.Contains("_p0.ParameterName = \"@UnitsInStock_lo\";", generatedText);
        Assert.Contains("_p1.ParameterName = \"@UnitsInStock_hi\";", generatedText);
    }

    [Fact]
    public void SelectAllByPredicateEmitsInSentinelAndRuntimeExpansion()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        // IN bakes a single-placeholder sentinel into the const SQL; the binder expands it at run time.
        Assert.Contains("WHERE \\\"CategoryId\\\" IN (@CategoryId)\";", generatedText);
        Assert.Contains("global::Inquiry.Parameters.InquiryInExpansion.Expand(_c, \"@CategoryId\", categoryIds);", generatedText);
    }

    [Fact]
    public void SelectAllByPredicateEmitsIsNullWithNoParameters()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.IsNull)]
                public partial Task<IReadOnlyList<Product>> WithoutCategoryAsync(CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        Assert.Contains("WHERE \\\"CategoryId\\\" IS NULL\";", generatedText);
        // No parameters are created for a null-test predicate.
        Assert.DoesNotContain("CreateParameter()", generatedText);
    }

    [Fact]
    public void SelectAllByPredicateEmitsOrGroupInDeclarationOrder()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("Discontinued", Compare.Equal)]
                [InquiryWhere("UnitsInStock", Compare.LessThan, Or = true)]
                public partial Task<IReadOnlyList<Product>> DiscontinuedOrLowStockAsync(bool discontinued, short? threshold, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        Assert.Contains(
            "WHERE \\\"Discontinued\\\" = @Discontinued OR \\\"UnitsInStock\\\" < @UnitsInStock\";",
            generatedText);
    }

    [Fact]
    public void SqlServerSelectAllByPredicateUsesBracketIdentifiers()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("UnitPrice", Compare.GreaterThanOrEqual)]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> SearchAsync(decimal? minPrice, System.Collections.Generic.IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        Assert.Contains("WHERE [UnitPrice] >= @UnitPrice AND [CategoryId] IN (@CategoryId)", generatedText);
    }

    [Fact]
    public void ReportsInRequiresCollectionDiagnostic()
    {
        // Compare.In with a scalar parameter (not a collection) is INQ018.
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(int categoryId, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ018");
    }

    [Fact]
    public void ReportsParameterMismatchDiagnosticForWrongArity()
    {
        // Two scalar criteria but only one non-CancellationToken parameter is INQ019.
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("UnitPrice", Compare.GreaterThanOrEqual)]
                [InquiryWhere("ProductName", Compare.Like)]
                public partial Task<IReadOnlyList<Product>> SearchAsync(decimal? minPrice, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ019");
    }

    [Fact]
    public void ReportsUnknownFieldDiagnosticForUnmappedPredicateField()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("DoesNotExist", Compare.Equal)]
                public partial Task<IReadOnlyList<Product>> SearchAsync(int value, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ007");
    }

    private static string GeneratedProductStoreText(GeneratorTestResult result)
    {
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);
        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("ProductStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return generatedStore.GetText().ToString();
    }

    [Fact]
    public void MySqlDialectEmitsBacktickIdentifiersAndOnDuplicateKeyUpsertWithEmulatedReturning()
    {
        // Spot-checks the MySqlSqlBuilder output for a client-supplied (non-generated) key:
        // backtick quoting, ON DUPLICATE KEY UPDATE ... VALUES(col) upsert, and the emulated
        // returning batch (INSERT ...; SELECT ... WHERE key = @Key) since MySQL lacks RETURNING.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class OrganizationStore : InquiryStore<Organization>
            {
                [InquirySelectAll]
                public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);

                [InquiryInsert(ReturnEntity = true)]
                public partial Task<Organization?> InsertReturningAsync(Organization o, CancellationToken cancellationToken = default);

                [InquiryUpdate(ReturnEntity = true)]
                public partial Task<Organization?> UpdateReturningAsync(Organization o, CancellationToken cancellationToken = default);

                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Organization o, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "MySql");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("OrganizationStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // Backtick-quoted identifiers.
        Assert.Contains("private const string _sqlSelectAll = \"SELECT `Key`, `Name` FROM `TOrganization`\";", generatedText);
        // Insert-returning: two-statement batch ending in SELECT ... WHERE key = @Key (client key).
        Assert.Contains("private const string _sqlInsertReturning = \"INSERT INTO `TOrganization` (`Key`, `Name`) VALUES (@Key, @Name); SELECT `Key`, `Name` FROM `TOrganization` WHERE `Key` = @Key\";", generatedText);
        // Update-returning: UPDATE ...; SELECT ... WHERE keywhere.
        Assert.Contains("private const string _sqlUpdateReturning = \"UPDATE `TOrganization` SET `Name` = @Name WHERE `Key` = @Key; SELECT `Key`, `Name` FROM `TOrganization` WHERE `Key` = @Key\";", generatedText);
        // Upsert: ON DUPLICATE KEY UPDATE col = VALUES(col).
        Assert.Contains("private const string _sqlUpsert = \"INSERT INTO `TOrganization` (`Key`, `Name`) VALUES (@Key, @Name) ON DUPLICATE KEY UPDATE `Name` = VALUES(`Name`)\";", generatedText);
    }

    [Fact]
    public void MySqlDialectEmitsLastInsertIdReturningForGeneratedKey()
    {
        // For a database-generated (AUTO_INCREMENT) key, the emulated returning batch selects the
        // freshly inserted row via LAST_INSERT_ID() (session-scoped, safe on a dedicated connection),
        // and the generated-key upsert uses the native ON DUPLICATE KEY UPDATE form.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Categories")]
            public sealed class Category
            {
                [InquiryKey("CategoryID", IsGenerated = true)]
                public int? CategoryID { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            public partial class CategoryStore : InquiryStore<Category>
            {
                [InquiryInsert(ReturnEntity = true)]
                public partial Task<Category?> InsertReturningAsync(Category c, CancellationToken cancellationToken = default);

                [InquiryUpsert(ReturnEntity = true)]
                public partial Task<Category?> UpsertReturningAsync(Category c, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "MySql");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("CategoryStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // Generated key omitted from the INSERT column list; returning SELECT keyed on LAST_INSERT_ID().
        Assert.Contains("private const string _sqlInsertReturning = \"INSERT INTO `Categories` (`Name`) VALUES (@Name); SELECT `CategoryID`, `Name` FROM `Categories` WHERE `CategoryID` = LAST_INSERT_ID()\";", generatedText);
        // Generated-key upsert-returning: native ON DUPLICATE KEY UPDATE, returning via LAST_INSERT_ID().
        Assert.Contains("private const string _sqlUpsertReturning = \"INSERT INTO `Categories` (`CategoryID`, `Name`) VALUES (@CategoryID, @Name) ON DUPLICATE KEY UPDATE `Name` = VALUES(`Name`); SELECT `CategoryID`, `Name` FROM `Categories` WHERE `CategoryID` = LAST_INSERT_ID()\";", generatedText);
    }

    [Fact]
    public void ReportsAmbiguousDialectWhenMultipleProvidersAreReferenced()
    {
        // The test compilation references all three Inquiry provider assemblies (Sqlite,
        // PostgreSql, SqlServer); without an explicit [assembly: InquiryDialect] on the
        // consumer, that's three markers and the generator reports INQ014. The fix path is to
        // either drop the surplus provider references or apply an explicit consumer-level
        // attribute (which is exactly what RunGenerator does in every other test).
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }
            }

            public partial class OrganizationStore : InquiryStore<Organization>
            {

                [InquirySelectAll]
                public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: null);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ014");
        Assert.DoesNotContain(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("OrganizationStore.InquiryStore.g.cs", StringComparison.Ordinal));
        // Materializers don't depend on the dialect and should still be emitted.
        Assert.Contains(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("Organization.InquiryEntity.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownDialectProducesNoStoreSqlBecauseNoInstalledProviderMatches()
    {
        // In the per-provider architecture there's no central registry that knows which dialect
        // names are valid; each installed provider's generator only emits when the resolved
        // dialect matches its own. Declaring an unrecognised dialect therefore silently leaves
        // the store partial methods without implementations (a CS8795 the user resolves by
        // installing a matching provider or fixing the dialect name).
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }
            }

            public partial class OrganizationStore : InquiryStore<Organization>
            {

                [InquirySelectAll]
                public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");

        Assert.DoesNotContain(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("OrganizationStore.InquiryStore.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void EagerRelationToUnmappedChildDoesNotCrashGenerator()
    {
        // An [InquiryRelation] can point at a type that is not an [InquiryTable] entity. The eager
        // emitter must skip such unresolved relations (like the SQL-field emission does) rather than
        // index the relation→child map and throw KeyNotFoundException, which crashes the generator.
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            // Deliberately NOT [InquiryTable]: referenced by a relation but never mapped.
            public sealed class Unmapped
            {
                public int Id { get; set; }
            }

            [InquiryTable("TParent")]
            public sealed class Parent
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryForeignKey("ChildId", "Unmapped", "Id")]
                public int? ChildId { get; set; }

                [InquiryRelation(nameof(ChildId))]
                public Unmapped? Child { get; set; }
            }

            public partial class ParentStore : InquiryStore<Parent>
            {
                [InquirySelectAllEager]
                public partial IAsyncEnumerable<Parent> SelectAllEagerAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        // No generator threw: every per-generator result must be exception-free.
        Assert.All(result.RunResult.Results, static r => Assert.Null(r.Exception));
    }

    [Fact]
    public void IncrementalPipelineCachesWhenUnrelatedSourceChanges()
    {
        // Proves the incremental rewrite delivers real caching: editing an unrelated file must not
        // re-run the entity/store discovery transforms. That only holds because their outputs are
        // value-equatable models with no symbols.
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TWidget")]
            public sealed class Widget
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            public partial class WidgetStore : InquiryStore<Widget>
            {
                [InquirySelectAll]
                public partial IAsyncEnumerable<Widget> SelectAllAsync(CancellationToken cancellationToken = default);

                [InquirySelectOneByKey]
                public partial Task<Widget?> SelectByKeyAsync(int id, CancellationToken cancellationToken = default);
            }
            """;

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(source, parseOptions),
            CSharpSyntaxTree.ParseText("[assembly: global::Inquiry.InquiryDialect(\"Sqlite\")]", parseOptions),
        };
        var compilation = CSharpCompilation.Create(
            "InquiryIncrementalTests",
            trees,
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator().AsSourceGenerator() },
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        // First run primes the incremental cache.
        driver = driver.RunGenerators(compilation);

        // Add an unrelated source file (no Inquiry entities or stores) and re-run the same driver.
        var updated = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "namespace Other { internal sealed class Unrelated { public int Value { get; set; } } }", parseOptions));
        driver = driver.RunGenerators(updated);

        var result = driver.GetRunResult().Results[0];

        AssertStepsCached(result, "InquiryEntities");
        AssertStepsCached(result, "InquiryStores");
    }

    [Fact]
    public void ChangedEntityReEmitsDependentStoreWithFreshSql()
    {
        // Correctness counterpart to the caching test: when an entity changes, its dependent store
        // must be re-emitted with fresh SQL (the generator must never serve cached/stale store code
        // for a changed entity, even though the store's own syntax is unchanged).
        static string EntitySource(string extraColumn) => $$"""
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TWidget")]
            public sealed class Widget
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            {{extraColumn}}
            }
            """;

        const string storeSource = """
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry.Stores;

            namespace Demo;

            public partial class WidgetStore : InquiryStore<Widget>
            {
                [InquirySelectAll]
                public partial IAsyncEnumerable<Widget> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
        var entityV1 = CSharpSyntaxTree.ParseText(EntitySource(string.Empty), parseOptions);
        var storeTree = CSharpSyntaxTree.ParseText(storeSource, parseOptions);
        var dialectTree = CSharpSyntaxTree.ParseText("[assembly: global::Inquiry.InquiryDialect(\"Sqlite\")]", parseOptions);

        var compilation = CSharpCompilation.Create(
            "InquiryReEmitTests",
            new[] { entityV1, storeTree, dialectTree },
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator().AsSourceGenerator() },
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        Assert.DoesNotContain("Extra", GeneratedStoreText(driver));

        // Add a column to the entity (store syntax untouched) and re-run the same driver.
        var entityV2 = CSharpSyntaxTree.ParseText(
            EntitySource("    [InquiryColumn]\r\n    public string Extra { get; set; } = string.Empty;"),
            parseOptions);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(entityV1, entityV2));

        // The store must reflect the new column — proof the changed entity re-emitted the store.
        Assert.Contains("Extra", GeneratedStoreText(driver));
    }

    private static string GeneratedStoreText(GeneratorDriver driver)
    {
        var store = driver.GetRunResult().Results[0].GeneratedSources
            .Single(s => s.HintName.EndsWith("WidgetStore.InquiryStore.g.cs", System.StringComparison.Ordinal));
        return store.SourceText.ToString();
    }

    private static void AssertStepsCached(GeneratorRunResult result, string trackingName)
    {
        Assert.True(result.TrackedSteps.ContainsKey(trackingName), $"Expected a tracked step named '{trackingName}'.");
        var steps = result.TrackedSteps[trackingName];
        Assert.NotEmpty(steps);
        foreach (var step in steps)
        {
            foreach (var output in step.Outputs)
            {
                Assert.True(
                    output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"Tracked step '{trackingName}' produced reason '{output.Reason}'; expected Cached or Unchanged.");
            }
        }
    }

    private static GeneratorTestResult RunGenerator(string source, string? dialect = "Sqlite")
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
        var trees = new List<Microsoft.CodeAnalysis.SyntaxTree> { CSharpSyntaxTree.ParseText(source, parseOptions) };

        // The generator picks its SqlBuilder from [assembly: Inquiry.InquiryDialect(...)]; tests
        // that exercise store emission inject it via this helper rather than repeating the
        // attribute in every source literal. Pass dialect: null to omit it (used by tests that
        // verify the missing-dialect diagnostic).
        if (dialect is not null)
        {
            trees.Add(CSharpSyntaxTree.ParseText(
                $"[assembly: global::Inquiry.InquiryDialect(\"{dialect}\")]",
                parseOptions));
        }

        var compilation = CSharpCompilation.Create(
            "InquiryGeneratorConsumerTests",
            trees,
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // Each provider's analyzer ships a self-contained generator; drive all three to mirror a
        // real consumer that has referenced multiple provider packages. Each generator runs the
        // same arbitration logic and at most one of them emits (the one whose dialect matches).
        var generators = new Microsoft.CodeAnalysis.ISourceGenerator[]
        {
            new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator().AsSourceGenerator(),
            new global::Inquiry.SqlServer.Analyzer.InquirySqlServerGenerator().AsSourceGenerator(),
            new global::Inquiry.PostgreSql.Analyzer.InquiryPostgreSqlGenerator().AsSourceGenerator(),
            new global::Inquiry.MySql.Analyzer.InquiryMySqlGenerator().AsSourceGenerator(),
        };
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        return new GeneratorTestResult(outputCompilation, generatorDiagnostics, driver.GetRunResult());
    }

    private static IReadOnlyList<MetadataReference> GetReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList() ?? new List<MetadataReference>();

        references.Add(MetadataReference.CreateFromFile(typeof(InquiryTableAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(ValueTask<>).Assembly.Location));

        // Force-load each provider's runtime assembly so its [assembly: InquiryDialect] attribute
        // ends up in the synthetic compilation's referenced-assembly set. Without this the test
        // process loads only the analyzer DLLs (which carry no dialect attribute) and the
        // ambiguous-dialect scenario can't be exercised.
        references.Add(MetadataReference.CreateFromFile(typeof(global::Inquiry.Sqlite.SqliteInquiryConnectionFactory).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(global::Inquiry.SqlServer.SqlServerInquiryConnectionFactory).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(global::Inquiry.PostgreSql.PostgreSqlInquiryConnectionFactory).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(global::Inquiry.MySql.MySqlInquiryConnectionFactory).Assembly.Location));

        return references;
    }

    private sealed record GeneratorTestResult(
        Compilation Compilation,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        GeneratorDriverRunResult RunResult);
}
