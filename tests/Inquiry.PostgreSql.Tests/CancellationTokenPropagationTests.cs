using Inquiry.Connections;
using Inquiry.PostgreSql.Tests.Fixtures;
using Xunit;

namespace Inquiry.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CancellationTokenPropagationTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public CancellationTokenPropagationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task CancelledTokenCancelsInFlightOperation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancel");
        var factory = harness.GetRequiredService<IInquiryConnectionFactory>();

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_sleep(30)";

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => command.ExecuteNonQueryAsync(cts.Token));
    }
}
