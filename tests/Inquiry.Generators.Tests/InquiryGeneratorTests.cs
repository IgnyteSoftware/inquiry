using System.Collections.Immutable;
using System.Reflection;
using Inquiry.Generators;
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

                [InquirySelect]
                public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);

                [InquirySelectByKey]
                public abstract Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

                [InquirySelectByField("IsActive")]
                public abstract IAsyncEnumerable<Organization> SelectByIsActiveAsync(bool isActive, CancellationToken cancellationToken = default);

                [InquiryInsert]
                public abstract Task<int> InsertAsync(Organization organization, CancellationToken cancellationToken = default);

                [InquiryUpdate]
                public abstract Task<bool> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);

                [InquiryDeleteByKey]
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
        Assert.Contains("_inquiry.QueryAsync<global::Demo.Organization>", generatedText);
        Assert.Contains("_inquiry.QuerySingleOrDefaultAsync<global::Demo.Organization>", generatedText);
        Assert.Contains("_inquiry.ExecuteAsync", generatedText);
        Assert.Contains("new { key = key }", generatedText);
        Assert.Contains("new { value = isActive }", generatedText);
        Assert.Contains("Key = organization.Key", generatedText);
        Assert.Contains("SELECT [Key], [Name], [IsActive] FROM [TOrganization]", generatedText);
        Assert.Contains("INSERT INTO [TOrganization] ([Key], [Name], [IsActive]) VALUES (@Key, @Name, @IsActive)", generatedText);
        Assert.Contains("UPDATE [TOrganization] SET [Name] = @Name, [IsActive] = @IsActive WHERE [Key] = @Key", generatedText);
        Assert.Contains("DELETE FROM [TOrganization] WHERE [Key] = @key", generatedText);
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

        var generatedServices = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("InquiryGeneratedServiceRegistration.g.cs", StringComparison.Ordinal));
        var generatedServicesText = generatedServices.GetText().ToString();

        Assert.Contains("IInquiryServiceRegistration", generatedServicesText);
        Assert.Contains("void AddServices(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)", generatedServicesText);
        Assert.Contains("TryAddSingleton<global::Inquiry.IInquiryEntityMaterializer<global::Demo.Organization>, global::Demo.OrganizationInquiryEntityMaterializer>", generatedServicesText);
        Assert.Contains("TryAddTransient<global::Demo.OrganizationStore, global::Demo.GeneratedOrganizationStore>", generatedServicesText);
    }

    [Fact]
    public void ReportsDiagnosticForMissingKey()
    {
        const string source = """
            using Inquiry;

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
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ISourceGenerator[] { generator }, parseOptions: parseOptions);
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
