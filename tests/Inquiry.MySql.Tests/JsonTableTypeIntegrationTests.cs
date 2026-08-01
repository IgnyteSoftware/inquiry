using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class JsonTableTypeIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public JsonTableTypeIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SupportedTypesAndNullableElementsMatch()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "jsontypes");
        await MySqlFamilyJsonTableAssertions.RunAsync(harness.GetRequiredService<MySqlFamilyJsonTableItemStore>());
    }

    private const string Ddl = MySqlFamilyJsonTableAssertions.Ddl;
}
