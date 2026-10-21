using Inquiry.Entities;
using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Stores;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

[InquiryTable("TProcedure")]
public sealed class OracleProcedureRow
{
    [InquiryKey]
    public int Id { get; set; }
}

public sealed partial class OracleProcedureStore : InquiryStore<OracleProcedureRow>
{
    [InquiryStoredProcedure("ADD_VALUES", OutputParameter = "Total")]
    public partial Task<int> AddAsync(int leftValue, int rightValue, CancellationToken cancellationToken = default);
}

[Collection(OracleCollection.Name)]
public sealed class StoredProcedureGeneratedPathIntegrationTests
{
    private readonly OracleContainerFixture _fixture;

    public StoredProcedureGeneratedPathIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GeneratedStorePreservesFormalNamesAndReadsOutputValue()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "stored_proc");
        await using (var connection = new OracleConnection(harness.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE OR REPLACE PROCEDURE ADD_VALUES(
                    leftValue IN NUMBER,
                    rightValue IN NUMBER,
                    Total OUT NUMBER)
                AS
                BEGIN
                    Total := leftValue + rightValue;
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var result = await harness.GetRequiredService<OracleProcedureStore>().AddAsync(17, 25);

        Assert.Equal(42, result);
    }
}
