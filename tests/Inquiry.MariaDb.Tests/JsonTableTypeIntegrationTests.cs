using Inquiry.FeatureCatalog;
using Inquiry.MariaDb.Tests.Fixtures;

namespace Inquiry.MariaDb.Tests;

[Collection(MariaDbCollection.Name)]
public sealed class JsonTableTypeIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public JsonTableTypeIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SupportedTypesAndNullableElementsMatch()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, MySqlFamilyJsonTableAssertions.Ddl, "jsontypes");
        await MySqlFamilyJsonTableAssertions.RunAsync(harness.GetRequiredService<MySqlFamilyJsonTableItemStore>());
    }
}
