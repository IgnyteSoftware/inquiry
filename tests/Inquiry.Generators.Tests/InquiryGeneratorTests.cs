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

            public abstract partial class OrganizationStore : InquiryStore<Organization>
            {
                protected OrganizationStore(IInquiry inquiry)
                    : base(inquiry)
                {
                }

                [InquirySelectAll]
                public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);

                [InquirySelectOneByKey]
                public abstract Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

                [InquirySelectAllByField("IsActive")]
                public abstract IAsyncEnumerable<Organization> SelectByIsActiveAsync(bool isActive, CancellationToken cancellationToken = default);

                [InquiryInsert]
                public abstract Task<int> InsertAsync(Organization organization, CancellationToken cancellationToken = default);

                [InquiryInsert(ReturnEntity = true)]
                public abstract Task<Organization?> InsertReturningAsync(Organization organization, CancellationToken cancellationToken = default);

                [InquiryUpdate]
                public abstract Task<bool> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);

                [InquiryDeleteOneByKey]
                public abstract Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);
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

        Assert.Contains("public sealed class GeneratedOrganizationStore : global::Demo.OrganizationStore", generatedText);
        Assert.Contains("public GeneratedOrganizationStore(global::Inquiry.IInquiry inquiry)", generatedText);

        // All SQL is emitted as const string fields baked at generation time. No runtime
        // dialect call, no _ctx, no _columns array survives in the generated store.
        Assert.Contains("private const string _sqlSelectAll = \"SELECT \\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\" FROM \\\"TOrganization\\\"\";", generatedText);
        Assert.Contains("private const string _sqlSelectByKey = \"SELECT \\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\" FROM \\\"TOrganization\\\" WHERE \\\"Key\\\" = @Key\";", generatedText);
        Assert.Contains("private const string _sqlInsert = \"INSERT INTO \\\"TOrganization\\\" (\\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\") VALUES (@Key, @Name, @IsActive)\";", generatedText);
        Assert.Contains("private const string _sqlInsertReturning = \"INSERT INTO \\\"TOrganization\\\" (\\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\") VALUES (@Key, @Name, @IsActive) RETURNING \\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\"\";", generatedText);
        Assert.Contains("private const string _sqlUpdate = \"UPDATE \\\"TOrganization\\\" SET \\\"Name\\\" = @Name, \\\"IsActive\\\" = @IsActive WHERE \\\"Key\\\" = @Key\";", generatedText);
        Assert.Contains("private const string _sqlDeleteByKey = \"DELETE FROM \\\"TOrganization\\\" WHERE \\\"Key\\\" = @Key\";", generatedText);
        Assert.Contains("private const string _sqlSelectBy_IsActive = \"SELECT \\\"Key\\\", \\\"Name\\\", \\\"IsActive\\\" FROM \\\"TOrganization\\\" WHERE \\\"IsActive\\\" = @IsActive\";", generatedText);

        // Read paths now dispatch through the struct-materializer overloads so the JIT can
        // specialize per concrete TMaterializer and inline the Materialize call.
        Assert.Contains("Inquiry.QueryAsync<global::Demo.Organization, global::Demo.OrganizationInquiryEntityStructMaterializer>", generatedText);
        Assert.Contains("Inquiry.QuerySingleOrDefaultAsync<global::Demo.Organization, global::Demo.OrganizationInquiryEntityStructMaterializer>", generatedText);
        Assert.Contains("Inquiry.ExecuteAsync", generatedText);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@Key\", key)", generatedText);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@IsActive\", isActive)", generatedText);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@Key\", organization.Key)", generatedText);
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
        Assert.Contains("TryAddScoped<global::Demo.OrganizationStore, global::Demo.GeneratedOrganizationStore>", generatedServicesText);
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

            public abstract partial class UserStore : InquiryStore<User>
            {
                protected UserStore(IInquiry inquiry) : base(inquiry) {}

                [InquirySelectAll]
                public abstract IAsyncEnumerable<User> SelectAllAsync(CancellationToken cancellationToken = default);
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

            public abstract partial class WidgetStore : InquiryStore<Widget>
            {
                protected WidgetStore(IInquiry inquiry) : base(inquiry) {}

                [InquiryInsert]
                public abstract Task<int> InsertAsync(Widget widget, CancellationToken cancellationToken = default);
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
    public void InsertWithOnlyDatabaseSuppliedColumnsUsesEmptyParameterArray()
    {
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

            public abstract partial class WidgetStore : InquiryStore<Widget>
            {
                protected WidgetStore(IInquiry inquiry) : base(inquiry) {}

                [InquiryInsert]
                public abstract Task<int> InsertAsync(Widget widget, CancellationToken cancellationToken = default);
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

        Assert.Contains("global::System.Array.Empty<global::Inquiry.Parameters.InquiryParameter>()", generatedText);
        Assert.DoesNotContain("new global::Inquiry.Parameters.InquiryParameter(\"@Id\", widget.Id)", generatedText);
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

            public abstract partial class WidgetStore : InquiryStore<Widget>
            {
                protected WidgetStore(IInquiry inquiry) : base(inquiry) {}

                [InquirySelectAll]
                public abstract IAsyncEnumerable<Widget> SelectAllAsync(CancellationToken cancellationToken = default);
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
    [InlineData("[InquirySelectAll]", "public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);", "_sqlSelectAll")]
    [InlineData("[InquirySelectOneByKey]", "public abstract Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);", "_sqlSelectByKey")]
    [InlineData("[InquirySelectAllByField(\"IsActive\")]", "public abstract IAsyncEnumerable<Organization> SelectByIsActiveAsync(bool isActive, CancellationToken cancellationToken = default);", "_sqlSelectBy_IsActive")]
    [InlineData("[InquiryInsert]", "public abstract Task<int> InsertAsync(Organization organization, CancellationToken cancellationToken = default);", "_sqlInsert")]
    [InlineData("[InquiryUpdate]", "public abstract Task<bool> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);", "_sqlUpdate")]
    [InlineData("[InquiryDeleteOneByKey]", "public abstract Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);", "_sqlDeleteByKey")]
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

            public abstract partial class OrganizationStore : InquiryStore<Organization>
            {
                protected OrganizationStore(IInquiry inquiry)
                    : base(inquiry)
                {
                }

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

            public abstract partial class OrganizationStore : InquiryStore<Organization>
            {
                protected OrganizationStore(IInquiry inquiry)
                    : base(inquiry)
                {
                }

                [InquirySelectAll]
                public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);
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
                protected OrganizationStore(IInquiry inquiry) : base(inquiry) {}

                [InquirySelectAll]
                public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);
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

            public abstract partial class OrganizationStore : InquiryStore<Organization>
            {
                protected OrganizationStore(IInquiry inquiry) : base(inquiry) {}

                [InquirySelectAllByField("DoesNotExist")]
                public abstract IAsyncEnumerable<Organization> SelectByMissingAsync(string value, CancellationToken cancellationToken = default);
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

            public abstract partial class UnmappedStore : InquiryStore<Unmapped>
            {
                protected UnmappedStore(IInquiry inquiry) : base(inquiry) {}

                [InquirySelectAll]
                public abstract IAsyncEnumerable<Unmapped> SelectAllAsync(CancellationToken cancellationToken = default);
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
    public void ReportsDiagnosticForNonAbstractStoreMethod()
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

            public abstract partial class OrganizationStore : InquiryStore<Organization>
            {
                protected OrganizationStore(IInquiry inquiry) : base(inquiry) {}

                [InquirySelectAll]
                public IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default)
                    => throw new System.NotImplementedException();
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ010");
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

            public abstract partial class OrganizationStore : InquiryStore<Organization>
            {
                protected OrganizationStore(IInquiry inquiry) : base(inquiry) {}

                [InquirySelectAll]
                public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);
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
    public void ReportsDiagnosticForUnknownDialectName()
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

            public abstract partial class OrganizationStore : InquiryStore<Organization>
            {
                protected OrganizationStore(IInquiry inquiry) : base(inquiry) {}

                [InquirySelectAll]
                public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ015");
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

        var generator = new InquiryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() }, parseOptions: parseOptions);
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

        return references;
    }

    private sealed record GeneratorTestResult(
        Compilation Compilation,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        GeneratorDriverRunResult RunResult);
}
