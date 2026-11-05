using Catalog.Deployment;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class SchemaEvolutionIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public SchemaEvolutionIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    // The sample keeps V1 and V2 as separate assemblies, so its frozen scripts must still match what
    // the generator emits for them. A real project's first script stops matching after its first change.
    [Fact]
    public void FrozenScriptsMatchTheGeneratedSchemaTheyWereTakenFrom()
    {
        Assert.Equal(Sql(Catalog.V1.CatalogSchema.Ddl), Sql(Migrations.Initial.Contents));
        Assert.EndsWith(Sql(Catalog.V2.CatalogSchema.ProviderArtifactsDdl), Sql(Migrations.WidenCode.Contents), StringComparison.Ordinal);

        var v1TypeName = Catalog.V1.CatalogSchema.ProviderArtifactsDdl.Split('\'')[1];
        Assert.Contains($"DROP TYPE {v1TypeName};", Migrations.RetireV1Type.Contents, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task WorkflowPrintsTheDocumentedTranscript()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        using var output = new StringWriter { NewLine = "\n" };

        await SchemaEvolution.RunAsync(_fixture.AdminConnectionString, output);

        var expected = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SchemaEvolution", "expected-output.txt"));
        Assert.Equal(expected.ReplaceLineEndings("\n"), output.ToString());
    }

    // Comment lines are allowed in the frozen files; the generated constants have none.
    private static string Sql(string value) => string.Join('\n', value.ReplaceLineEndings("\n")
        .Split('\n')
        .Where(static line => !line.StartsWith("--", StringComparison.Ordinal))).Trim();
}
