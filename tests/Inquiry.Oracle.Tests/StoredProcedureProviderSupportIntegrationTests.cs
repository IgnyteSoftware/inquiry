using Inquiry.Entities;
using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Stores;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

[InquiryTable("TP478Item")]
public sealed class OracleP478Item
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;
}

[InquiryTable("TP478Line")]
public sealed class OracleP478Line
{
    [InquiryKey]
    public int Id { get; set; }

    public int ItemId { get; set; }
}

public sealed partial class OracleP478ItemStore : InquiryStore<OracleP478Item>
{
    [InquiryStoredProcedure("P478_ROWS")]
    public partial Task<IReadOnlyList<OracleP478Item>> ListAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_ROWS")]
    public partial IAsyncEnumerable<OracleP478Item> StreamAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_ROWS")]
    public partial Task<OracleP478Item?> SingleOfRowsAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_ONE")]
    public partial Task<OracleP478Item?> OneAsync(int id, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_TOUCH")]
    public partial Task<int> TouchAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_COUNT", OutputParameter = "total")]
    public partial Task<int> CountAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_NULL_OUT", OutputParameter = "total")]
    public partial Task<int?> NullOutAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_NULL_OUT", OutputParameter = "total")]
    public partial Task<int> NullOutAsDefaultAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_DECIMAL_OUT", OutputParameter = "total")]
    public partial Task<decimal> DecimalOutAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_INCREMENT")]
    public partial Task<int> IncrementAsync([InquiryParameter(IsInputOutput = true)] int counter, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_INCREMENT")]
    public partial Task<int?> IncrementNullableAsync([InquiryParameter(IsInputOutput = true)] int? counter, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_LABEL")]
    public partial Task<string> LabelAsync([InquiryParameter(IsInputOutput = true, Length = 50)] string label, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_PAYLOAD")]
    public partial Task<byte[]> PayloadAsync([InquiryParameter(IsInputOutput = true, Length = 16)] byte[] payload, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_REPORT")]
    public partial Task<(IReadOnlyList<OracleP478Item>, IReadOnlyList<OracleP478Line>)> ReportAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_REPORT_SHORT")]
    public partial Task<(IReadOnlyList<OracleP478Item>, IReadOnlyList<OracleP478Line>)> ReportShortAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_REPORT_LONG")]
    public partial Task<(IReadOnlyList<OracleP478Item>, IReadOnlyList<OracleP478Line>)> ReportLongAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_REPORT_UNOPENED")]
    public partial Task<(IReadOnlyList<OracleP478Item>, IReadOnlyList<OracleP478Line>)> ReportUnopenedAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_RENAMED")]
    public partial Task<IReadOnlyList<OracleP478Item>> RenamedAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("P478_REORDERED")]
    public partial Task<IReadOnlyList<OracleP478Item>> ReorderedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Live evidence for each procedure feature that the guide lists as supported on Oracle (#478).
/// Every procedure that a lifecycle test calls logs one row per execution, so each test can show one
/// execution per call, participation in the caller's transaction, and early cancellation.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class StoredProcedureProviderSupportIntegrationTests
{
    private readonly OracleContainerFixture _fixture;

    public StoredProcedureProviderSupportIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    private static readonly string[] _procedures =
    [
        """
        CREATE OR REPLACE PROCEDURE P478_ROWS(p_min_id IN NUMBER, c0 OUT SYS_REFCURSOR) AS
        BEGIN
            INSERT INTO TP478Call (CalledAt) VALUES (SYSTIMESTAMP);
            OPEN c0 FOR SELECT Id, Name FROM TP478Item WHERE Id >= p_min_id ORDER BY Id;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_ONE(p_id IN NUMBER, c0 OUT SYS_REFCURSOR) AS
        BEGIN
            INSERT INTO TP478Call (CalledAt) VALUES (SYSTIMESTAMP);
            OPEN c0 FOR SELECT Id, Name FROM TP478Item WHERE Id = p_id;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_TOUCH(minId IN NUMBER) AS
        BEGIN
            INSERT INTO TP478Call (CalledAt) VALUES (SYSTIMESTAMP);
            UPDATE TP478Item SET Name = Name WHERE Id >= minId;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_COUNT(minId IN NUMBER, total OUT NUMBER) AS
        BEGIN
            INSERT INTO TP478Call (CalledAt) VALUES (SYSTIMESTAMP);
            SELECT COUNT(*) INTO total FROM TP478Item WHERE Id >= minId;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_NULL_OUT(total OUT NUMBER) AS
        BEGIN
            total := NULL;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_DECIMAL_OUT(total OUT NUMBER) AS
        BEGIN
            total := 19.75;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_INCREMENT(counter IN OUT NUMBER) AS
        BEGIN
            INSERT INTO TP478Call (CalledAt) VALUES (SYSTIMESTAMP);
            IF counter < 0 THEN counter := NULL; ELSE counter := counter + 1; END IF;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_LABEL(label IN OUT NVARCHAR2) AS
        BEGIN
            INSERT INTO TP478Call (CalledAt) VALUES (SYSTIMESTAMP);
            label := UPPER(label) || '!';
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_PAYLOAD(payload IN OUT RAW) AS
        BEGIN
            INSERT INTO TP478Call (CalledAt) VALUES (SYSTIMESTAMP);
            payload := UTL_RAW.CONCAT(payload, HEXTORAW('FF'));
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_REPORT(p_min_id IN NUMBER, c0 OUT SYS_REFCURSOR, c1 OUT SYS_REFCURSOR) AS
        BEGIN
            INSERT INTO TP478Call (CalledAt) VALUES (SYSTIMESTAMP);
            OPEN c0 FOR SELECT Id, Name FROM TP478Item WHERE Id >= p_min_id ORDER BY Id;
            OPEN c1 FOR SELECT Id, ItemId FROM TP478Line WHERE ItemId >= p_min_id ORDER BY Id;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_REPORT_SHORT(c0 OUT SYS_REFCURSOR) AS
        BEGIN
            OPEN c0 FOR SELECT Id, Name FROM TP478Item ORDER BY Id;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_REPORT_LONG(c0 OUT SYS_REFCURSOR, c1 OUT SYS_REFCURSOR, c2 OUT SYS_REFCURSOR) AS
        BEGIN
            OPEN c0 FOR SELECT Id, Name FROM TP478Item ORDER BY Id;
            OPEN c1 FOR SELECT Id, ItemId FROM TP478Line ORDER BY Id;
            OPEN c2 FOR SELECT Id, Name FROM TP478Item ORDER BY Id;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_REPORT_UNOPENED(c0 OUT SYS_REFCURSOR, c1 OUT SYS_REFCURSOR) AS
        BEGIN
            OPEN c0 FOR SELECT Id, Name FROM TP478Item ORDER BY Id;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_RENAMED(c0 OUT SYS_REFCURSOR) AS
        BEGIN
            OPEN c0 FOR SELECT Id, Name AS Title FROM TP478Item ORDER BY Id;
        END;
        """,
        """
        CREATE OR REPLACE PROCEDURE P478_REORDERED(c0 OUT SYS_REFCURSOR) AS
        BEGIN
            OPEN c0 FOR SELECT Name, 99 AS Extra, Id FROM TP478Item ORDER BY Id;
        END;
        """,
    ];

    private async Task<OracleTestHarness> CreateHarnessAsync()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            """
            CREATE TABLE TP478Item (Id NUMBER(10) PRIMARY KEY, Name NVARCHAR2(50) NOT NULL);
            CREATE TABLE TP478Line (Id NUMBER(10) PRIMARY KEY, ItemId NUMBER(10) NOT NULL);
            CREATE TABLE TP478Call (CalledAt TIMESTAMP NOT NULL)
            """,
            "sproc478");

        await using var connection = new OracleConnection(harness.ConnectionString);
        await connection.OpenAsync();
        foreach (var statement in _procedures.Prepend("""
            INSERT ALL
                INTO TP478Item (Id, Name) VALUES (1, 'Widget')
                INTO TP478Item (Id, Name) VALUES (2, 'Gadget')
                INTO TP478Item (Id, Name) VALUES (3, 'Gizmo')
                INTO TP478Line (Id, ItemId) VALUES (10, 1)
                INTO TP478Line (Id, ItemId) VALUES (11, 2)
                INTO TP478Line (Id, ItemId) VALUES (12, 3)
            SELECT 1 FROM DUAL
            """))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }

        return harness;
    }

    private static Task<int> CallCountAsync(IInquiry inquiry)
        => inquiry.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM TP478Call");

    private static async Task<List<T>> DrainAsync<T>(IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
    {
        var items = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Runs one procedure method alone, in a committed transaction, in a rolled-back transaction, and with
    /// a canceled token, and checks the call log after each step.
    /// </summary>
    private static async Task AssertLifecycleAsync<T>(OracleTestHarness harness, Func<CancellationToken, Task<T>> call, Action<T> assertResult)
    {
        var inquiry = harness.GetRequiredService<IInquiry>();
        var start = await CallCountAsync(inquiry);

        assertResult(await call(CancellationToken.None));
        Assert.Equal(start + 1, await CallCountAsync(inquiry));

        assertResult(await inquiry.ExecuteInTransactionAsync(_ => call(CancellationToken.None)));
        Assert.Equal(start + 2, await CallCountAsync(inquiry));

        var rollback = await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.ExecuteInTransactionAsync<T>(async _ =>
        {
            assertResult(await call(CancellationToken.None));
            Assert.Equal(start + 3, await CallCountAsync(inquiry));
            throw new InvalidOperationException("roll back");
        }));
        Assert.Equal("roll back", rollback.Message);
        Assert.Equal(start + 2, await CallCountAsync(inquiry));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call(new CancellationToken(canceled: true)));
        Assert.Equal(start + 2, await CallCountAsync(inquiry));
    }

    private static OracleP478ItemStore Store(OracleTestHarness harness) => harness.GetRequiredService<OracleP478ItemStore>();

    [SkippableFact]
    public async Task ListRunsOncePerCall()
    {
        await using var harness = await CreateHarnessAsync();
        await AssertLifecycleAsync(harness, ct => Store(harness).ListAsync(2, ct),
            static items => Assert.Equal([2, 3], items.Select(static i => i.Id)));
    }

    [SkippableFact]
    public async Task StreamRunsOncePerEnumeration()
    {
        await using var harness = await CreateHarnessAsync();
        await AssertLifecycleAsync(harness, ct => DrainAsync(Store(harness).StreamAsync(2, ct), ct),
            static items => Assert.Equal(["Gadget", "Gizmo"], items.Select(static i => i.Name)));

        // Creating the stream does not run the procedure; each full enumeration runs it once.
        var stream = Store(harness).StreamAsync(1);
        var inquiry = harness.GetRequiredService<IInquiry>();
        Assert.Equal(2, await CallCountAsync(inquiry));
        Assert.Equal(3, (await DrainAsync(stream)).Count);
        Assert.Equal(3, (await DrainAsync(stream)).Count);
        Assert.Equal(4, await CallCountAsync(inquiry));
    }

    [SkippableFact]
    public async Task SingleRunsOncePerCall()
    {
        await using var harness = await CreateHarnessAsync();
        await AssertLifecycleAsync(harness, ct => Store(harness).OneAsync(3, ct),
            static item => Assert.Equal("Gizmo", Assert.IsType<OracleP478Item>(item).Name));

        Assert.Null(await Store(harness).OneAsync(99));
        Assert.Empty(await Store(harness).ListAsync(99));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(harness).SingleOfRowsAsync(1));
    }

    [SkippableFact]
    public async Task AffectedRowsRunOncePerCall()
    {
        await using var harness = await CreateHarnessAsync();

        // ODP.NET reports -1 for a procedure call, whatever the procedure changed.
        await AssertLifecycleAsync(harness, ct => Store(harness).TouchAsync(2, ct),
            static affected => Assert.Equal(-1, affected));
    }

    [SkippableFact]
    public async Task OutputParameterRunsOncePerCall()
    {
        await using var harness = await CreateHarnessAsync();
        await AssertLifecycleAsync(harness, ct => Store(harness).CountAsync(2, ct),
            static total => Assert.Equal(2, total));

        Assert.Null(await Store(harness).NullOutAsync());
        Assert.Equal(0, await Store(harness).NullOutAsDefaultAsync());
        Assert.Equal(19.75m, await Store(harness).DecimalOutAsync());
    }

    [SkippableFact]
    public async Task InOutParameterRunsOncePerCall()
    {
        await using var harness = await CreateHarnessAsync();
        await AssertLifecycleAsync(harness, ct => Store(harness).IncrementAsync(41, ct),
            static value => Assert.Equal(42, value));

        Assert.Null(await Store(harness).IncrementNullableAsync(-1));
        await AssertLifecycleAsync(harness, ct => Store(harness).LabelAsync("abc", ct),
            static label => Assert.Equal("ABC!", label));
        await AssertLifecycleAsync(harness, ct => Store(harness).PayloadAsync([0x01, 0x02], ct),
            static payload => Assert.Equal([0x01, 0x02, 0xFF], payload));
    }

    [SkippableFact]
    public async Task MultipleResultSetsRunOncePerCall()
    {
        await using var harness = await CreateHarnessAsync();
        await AssertLifecycleAsync(harness, ct => Store(harness).ReportAsync(2, ct), static report =>
        {
            Assert.Equal([2, 3], report.Item1.Select(static i => i.Id));
            Assert.Equal([11, 12], report.Item2.Select(static l => l.Id));
        });
    }

    [SkippableFact]
    public async Task MissingCursorFailsInTheDatabase()
    {
        await using var harness = await CreateHarnessAsync();
        var store = Store(harness);

        // The wrapper passes one cursor per tuple element, so a procedure with fewer or more cursors fails to bind.
        var fewer = await Assert.ThrowsAsync<OracleException>(() => store.ReportShortAsync());
        Assert.Contains("PLS-00306", fewer.Message, StringComparison.Ordinal);
        var more = await Assert.ThrowsAsync<OracleException>(() => store.ReportLongAsync());
        Assert.Contains("PLS-00306", more.Message, StringComparison.Ordinal);

        var unopened = await Assert.ThrowsAsync<OracleException>(() => store.ReportUnopenedAsync());
        Assert.Contains("ORA-01001", unopened.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task ResultColumnsMapByName()
    {
        await using var harness = await CreateHarnessAsync();
        var store = Store(harness);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenamedAsync());
        Assert.Contains("Name", error.Message, StringComparison.Ordinal);

        var items = await store.ReorderedAsync();
        Assert.Equal([1, 2, 3], items.Select(static i => i.Id));
        Assert.Equal(["Widget", "Gadget", "Gizmo"], items.Select(static i => i.Name));
    }
}
