using Inquiry.Connections;
using Inquiry.SqlServer.Tests.Fixtures;
using Xunit;

namespace Inquiry.SqlServer.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class CancellationTokenPropagationTests
{
    private readonly SqlServerContainerFixture _fixture;

    public CancellationTokenPropagationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task CancelledTokenCancelsInFlightOperation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancel");
        var factory = harness.GetRequiredService<IInquiryConnectionFactory>();

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "WAITFOR DELAY '00:00:30'";

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => command.ExecuteNonQueryAsync(cts.Token));
    }
}
