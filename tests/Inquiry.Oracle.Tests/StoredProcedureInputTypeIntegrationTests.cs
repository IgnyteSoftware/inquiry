using Inquiry.Entities;
using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Stores;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

[InquiryTable("TP547Row")]
public sealed class OracleP547Row
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn]
    public Guid? Token { get; set; }

    [InquiryColumn]
    public DateTime? SeenAt { get; set; }

    [InquiryColumn]
    public bool Flag { get; set; }

    [InquiryColumn]
    public DateOnly? Day { get; set; }

    [InquiryColumn]
    public TimeOnly? TimeOfDay { get; set; }
}

public sealed partial class OracleP547RowStore : InquiryStore<OracleP547Row>
{
    // Task<int> procedures bind by name, so these names match the procedure's parameters.
    [InquiryStoredProcedure("P547_SAVE")]
    public partial Task<int> SaveAsync(
        int rowId, Guid token, DateTime seenAt, bool flag, DateOnly day, TimeOnly timeOfDay,
        CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P547_SAVE")]
    public partial Task<int> SaveNullableAsync(
        int rowId, Guid? token, DateTime? seenAt, bool flag, DateOnly? day, TimeOnly? timeOfDay,
        CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<OracleP547Row?> GetAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Procedure inputs use Oracle's DbType choices, as entity binders do (#547). ODP.NET rejects the
/// portable DbType.Guid and DbType.DateTime2, so these calls failed before the procedure ran.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class StoredProcedureInputTypeIntegrationTests
{
    private readonly OracleContainerFixture _fixture;

    public StoredProcedureInputTypeIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    private async Task<OracleTestHarness> CreateHarnessAsync()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            """
            CREATE TABLE TP547Row (
                Id NUMBER(10) PRIMARY KEY,
                Token RAW(16) NULL,
                SeenAt TIMESTAMP(7) NULL,
                Flag NUMBER(1) NOT NULL,
                Day DATE NULL,
                TimeOfDay INTERVAL DAY(0) TO SECOND(7) NULL)
            """,
            "sproc547");

        await using var connection = new OracleConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE OR REPLACE PROCEDURE P547_SAVE(
                rowId IN NUMBER, token IN RAW, seenAt IN TIMESTAMP, flag IN NUMBER, day IN DATE,
                timeOfDay IN INTERVAL DAY TO SECOND) AS
            BEGIN
                INSERT INTO TP547Row (Id, Token, SeenAt, Flag, Day, TimeOfDay)
                VALUES (rowId, token, seenAt, flag, day, timeOfDay);
            END;
            """;
        await command.ExecuteNonQueryAsync();
        return harness;
    }

    [SkippableFact]
    public async Task ProcedureReceivesGuidDateTimeBoolDateOnlyAndTimeOnlyInputs()
    {
        await using var harness = await CreateHarnessAsync();
        var store = harness.GetRequiredService<OracleP547RowStore>();
        var token = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        var seenAt = new DateTime(2026, 11, 12, 13, 14, 15, 678, DateTimeKind.Unspecified);
        var day = new DateOnly(2026, 2, 28);
        var timeOfDay = new TimeOnly(23, 59, 58, 765);

        await store.SaveAsync(1, token, seenAt, flag: true, day, timeOfDay);

        var row = Assert.IsType<OracleP547Row>(await store.GetAsync(1));
        Assert.Equal(token, row.Token);
        Assert.Equal(seenAt, row.SeenAt);
        Assert.True(row.Flag);
        Assert.Equal(day, row.Day);
        Assert.Equal(timeOfDay, row.TimeOfDay);
    }

    [SkippableFact]
    public async Task ProcedureReceivesNullableInputsAsValuesAndAsNull()
    {
        await using var harness = await CreateHarnessAsync();
        var store = harness.GetRequiredService<OracleP547RowStore>();
        var token = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
        var seenAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        var day = new DateOnly(2025, 1, 2);
        var timeOfDay = new TimeOnly(3, 4, 5);

        await store.SaveNullableAsync(1, token, seenAt, flag: false, day, timeOfDay);
        await store.SaveNullableAsync(2, null, null, flag: true, null, null);

        var withValues = Assert.IsType<OracleP547Row>(await store.GetAsync(1));
        Assert.Equal(token, withValues.Token);
        Assert.Equal(seenAt, withValues.SeenAt);
        Assert.False(withValues.Flag);
        Assert.Equal(day, withValues.Day);
        Assert.Equal(timeOfDay, withValues.TimeOfDay);

        var withNulls = Assert.IsType<OracleP547Row>(await store.GetAsync(2));
        Assert.Null(withNulls.Token);
        Assert.Null(withNulls.SeenAt);
        Assert.True(withNulls.Flag);
        Assert.Null(withNulls.Day);
        Assert.Null(withNulls.TimeOfDay);
    }
}
