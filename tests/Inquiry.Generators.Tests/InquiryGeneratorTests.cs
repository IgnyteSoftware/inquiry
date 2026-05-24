using System.Collections.Immutable;
using System.Reflection;
using Inquiry.Entities;
using Inquiry.Generators;
using Inquiry.Stores;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
        Assert.Contains("private readonly global::Inquiry.Sql.InquirySqlStatementSet _sqlStatements;", generatedText);
        Assert.Contains("public GeneratedOrganizationStore(global::Inquiry.IInquiry inquiry, global::Inquiry.Sql.InquirySqlDialect sqlDialect)", generatedText);
        Assert.Contains("new global::Inquiry.Sql.InquirySqlStatementBuilder(sqlDialect).Build", generatedText);
        Assert.Contains("_inquiry.QueryAsync<global::Demo.Organization>", generatedText);
        Assert.Contains("_inquiry.QuerySingleOrDefaultAsync<global::Demo.Organization>", generatedText);
        Assert.Contains("_inquiry.ExecuteAsync", generatedText);
        Assert.Contains("new { key = key }", generatedText);
        Assert.Contains("new { value = isActive }", generatedText);
        Assert.Contains("Key = organization.Key", generatedText);
        Assert.Contains("_sqlStatements.SelectAll", generatedText);
        Assert.Contains("_sqlStatements.Insert", generatedText);
        Assert.Contains("_sqlStatements.Update", generatedText);
        Assert.Contains("_sqlStatements.DeleteByKey", generatedText);
        Assert.DoesNotContain("AddParameter", generatedText);
        Assert.DoesNotContain("SqlServer", generatedText);
        Assert.DoesNotContain("Sqlite", generatedText);
        Assert.DoesNotContain("ConnectionFactory.OpenConnectionAsync", generatedText);
        Assert.DoesNotContain("CreateCommand()", generatedText);
        Assert.DoesNotContain("ExecuteReaderAsync", generatedText);
        Assert.DoesNotContain("ExecuteNonQueryAsync", generatedText);

        var generatedEntity = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("Organization.InquiryEntity.g.cs", StringComparison.Ordinal));
        var generatedEntityText = generatedEntity.GetText().ToString();

        Assert.Contains("IInquiryEntityMaterializer<global::Demo.Organization>", generatedEntityText);

        var generatedServices = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("InquiryGeneratedServiceRegistration.g.cs", StringComparison.Ordinal));
        var generatedServicesText = generatedServices.GetText().ToString();

        Assert.Contains("IInquiryServiceRegistration", generatedServicesText);
        Assert.Contains("void AddServices(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)", generatedServicesText);
        Assert.Contains("TryAddSingleton<global::Inquiry.Entities.IInquiryEntityMetadata<global::Demo.Organization>, global::Demo.OrganizationInquiryEntityMetadata>", generatedServicesText);
        Assert.Contains("TryAddSingleton<global::Inquiry.Materialization.IInquiryEntityMaterializer<global::Demo.Organization>, global::Demo.OrganizationInquiryEntityMaterializer>", generatedServicesText);
        Assert.Contains("TryAddTransient<global::Demo.OrganizationStore, global::Demo.GeneratedOrganizationStore>", generatedServicesText);
    }

    [Fact]
    public void GeneratesForeignKeyMetadataWithoutChangingColumnMapping()
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

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            [InquiryTable("TUser")]
            public sealed class User
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryForeignKey("TOrganization", "Key")]
                public Guid OrganizationKey { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            public abstract partial class UserStore : InquiryStore<User>
            {
                protected UserStore(IInquiry inquiry)
                    : base(inquiry)
                {
                }

                [InquirySelectAll]
                public abstract IAsyncEnumerable<User> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedEntity = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("User.InquiryEntity.g.cs", StringComparison.Ordinal));
        var generatedEntityText = generatedEntity.GetText().ToString();

        Assert.Contains("IInquiryEntityMetadata<global::Demo.User>", generatedEntityText);
        Assert.Contains("new global::Inquiry.Sql.InquirySqlColumn(\"OrganizationKey\", \"OrganizationKey\", isKey: false", generatedEntityText);
        Assert.Contains("new global::Inquiry.Entities.InquiryForeignKey(\"OrganizationKey\", \"OrganizationKey\", \"TOrganization\", \"Key\")", generatedEntityText);
        Assert.DoesNotContain("new global::Inquiry.Sql.InquirySqlColumn(\"OrganizationKey\", \"TOrganization\"", generatedEntityText);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("UserStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedStoreText = generatedStore.GetText().ToString();

        Assert.Contains("new global::Inquiry.Sql.InquirySqlColumn(\"OrganizationKey\", \"OrganizationKey\", isKey: false", generatedStoreText);
        Assert.DoesNotContain("new global::Inquiry.Sql.InquirySqlColumn(\"OrganizationKey\", \"TOrganization\"", generatedStoreText);
    }

    [Fact]
    public void GeneratesForeignKeyMetadataWithExplicitLocalColumn()
    {
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TUser")]
            public sealed class User
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryForeignKey("OrganizationId", "TOrganization", "Key")]
                public Guid OrganizationKey { get; set; }
            }
            """;

        var result = RunGenerator(source);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedEntity = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("User.InquiryEntity.g.cs", StringComparison.Ordinal));
        var generatedEntityText = generatedEntity.GetText().ToString();

        Assert.Contains("new global::Inquiry.Sql.InquirySqlColumn(\"OrganizationKey\", \"OrganizationId\", isKey: false", generatedEntityText);
        Assert.Contains("new global::Inquiry.Entities.InquiryForeignKey(\"OrganizationKey\", \"OrganizationId\", \"TOrganization\", \"Key\")", generatedEntityText);
    }

    [Theory]
    [InlineData("[InquirySelectAll]", "public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);", "_sqlStatements.SelectAll")]
    [InlineData("[InquirySelectOneByKey]", "public abstract Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);", "_sqlStatements.SelectByKey")]
    [InlineData("[InquirySelectAllByField(\"IsActive\")]", "public abstract IAsyncEnumerable<Organization> SelectByIsActiveAsync(bool isActive, CancellationToken cancellationToken = default);", "_sqlStatements.SelectByField")]
    [InlineData("[InquiryInsert]", "public abstract Task<int> InsertAsync(Organization organization, CancellationToken cancellationToken = default);", "_sqlStatements.Insert")]
    [InlineData("[InquiryUpdate]", "public abstract Task<bool> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);", "_sqlStatements.Update")]
    [InlineData("[InquiryDeleteOneByKey]", "public abstract Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);", "_sqlStatements.DeleteByKey")]
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
    public void ReportsDiagnosticForInvalidForeignKey()
    {
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TUser")]
            public sealed class User
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryForeignKey("", "Key")]
                public Guid OrganizationKey { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ011");
    }

    private static GeneratorTestResult RunGenerator(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "InquiryGeneratorConsumerTests",
            new[] { syntaxTree },
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
