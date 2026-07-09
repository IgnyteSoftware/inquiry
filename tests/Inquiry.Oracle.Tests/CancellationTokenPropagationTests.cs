using Inquiry.Connections;
using Inquiry.Oracle.Tests.Fixtures;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class CancellationTokenPropagationTests
{
    private readonly OracleContainerFixture _fixture;

    public CancellationTokenPropagationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task CancelledTokenCancelsInFlightOperation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        // DBMS_SESSION.SLEEP requires EXECUTE privilege not included in the RESOURCE role
        // granted to throwaway test users; grant it via the admin connection first.
        await using (var admin = new OracleConnection(_fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var grant = admin.CreateCommand();
            grant.CommandText = "GRANT EXECUTE ON SYS.DBMS_SESSION TO PUBLIC";
            await grant.ExecuteNonQueryAsync();
        }

        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "cancel");
        var factory = harness.GetRequiredService<IInquiryConnectionFactory>();

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "BEGIN DBMS_SESSION.SLEEP(30); END;";

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // ODP.NET may throw OracleException(1013) rather than OperationCanceledException
        // on cancellation. Either proves the token reached the provider and interrupted the
        // in-flight operation.
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => command.ExecuteNonQueryAsync(cts.Token));
        Assert.True(
            ex is OperationCanceledException || (ex is OracleException oe && oe.Number == 1013),
            $"Expected OperationCanceledException or ORA-01013, got {ex.GetType().Name}: {ex.Message}");
    }
}
