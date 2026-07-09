using Inquiry.Connections;
using Inquiry.MySql.Tests.Fixtures;
using Xunit;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class CancellationTokenPropagationTests
{
    private readonly MySqlContainerFixture _fixture;

    public CancellationTokenPropagationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task CancelledTokenCancelsInFlightOperation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancel");
        var factory = harness.GetRequiredService<IInquiryConnectionFactory>();

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SLEEP(30)";

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => command.ExecuteNonQueryAsync(cts.Token));
    }
}
