using Inquiry.Connections;
using Inquiry.MariaDb.Tests.Fixtures;
using Xunit;

namespace Inquiry.MariaDb.Tests;

[Collection(MariaDbCollection.Name)]
public sealed class CancellationTokenPropagationTests
{
    private readonly MariaDbContainerFixture _fixture;

    public CancellationTokenPropagationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task CancelledTokenCancelsInFlightOperation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancel");
        var factory = harness.GetRequiredService<IInquiryConnectionFactory>();

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SLEEP(30)";

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => command.ExecuteNonQueryAsync(cts.Token));
    }
}
