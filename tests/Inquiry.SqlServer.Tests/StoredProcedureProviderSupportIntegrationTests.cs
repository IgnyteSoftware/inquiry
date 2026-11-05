using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("P478Item")]
public sealed class P478Item
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 50)]
    public string Name { get; set; } = string.Empty;
}

[InquiryTable("P478Line")]
public sealed class P478Line
{
    [InquiryKey]
    public int Id { get; set; }

    public int ItemId { get; set; }
}

public partial class P478ItemStore : InquiryStore<P478Item>
{
    [InquiryStoredProcedure("usp_P478_Rows")]
    public partial Task<IReadOnlyList<P478Item>> ListAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Rows")]
    public partial IAsyncEnumerable<P478Item> StreamAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Rows")]
    public partial Task<P478Item?> SingleOfRowsAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_One")]
    public partial Task<P478Item?> OneAsync(int id, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Touch")]
    public partial Task<int> TouchAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_TouchQuiet")]
    public partial Task<int> TouchQuietAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Count", OutputParameter = "total")]
    public partial Task<int> CountAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_NullOut", OutputParameter = "total")]
    public partial Task<int?> NullOutAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_NullOut", OutputParameter = "total")]
    public partial Task<int> NullOutAsDefaultAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_NameOf", OutputParameter = "name")]
    public partial Task<string?> NameOfAsync(int id, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_BytesOf", OutputParameter = "payload")]
    public partial Task<byte[]?> BytesOfAsync(int id, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Increment")]
    public partial Task<int> IncrementAsync([InquiryParameter(IsInputOutput = true)] int value, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Increment")]
    public partial Task<int?> IncrementNullableAsync([InquiryParameter(IsInputOutput = true)] int? value, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Label")]
    public partial Task<string> LabelAsync([InquiryParameter(IsInputOutput = true, Length = 50)] string label, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Payload")]
    public partial Task<byte[]> PayloadAsync([InquiryParameter(IsInputOutput = true, Length = 16)] byte[] payload, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Status", ReturnsValue = true)]
    public partial Task<int> StatusAsync(int id, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Report")]
    public partial Task<(IReadOnlyList<P478Item>, IReadOnlyList<P478Line>)> ReportAsync(int minId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_ReportShort")]
    public partial Task<(IReadOnlyList<P478Item>, IReadOnlyList<P478Line>)> ReportShortAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_ReportExtra")]
    public partial Task<(IReadOnlyList<P478Item>, IReadOnlyList<P478Line>)> ReportExtraAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_NoResult")]
    public partial Task<IReadOnlyList<P478Item>> NoResultListAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_NoResult")]
    public partial Task<P478Item?> NoResultSingleAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Renamed")]
    public partial Task<IReadOnlyList<P478Item>> RenamedAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_Reordered")]
    public partial Task<IReadOnlyList<P478Item>> ReorderedAsync(CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_TvpInOut")]
    public partial Task<int> TvpInOutAsync(
        [InquiryParameter(TvpTypeName = "[dbo].[P478Ids]")] IEnumerable<int> ids,
        [InquiryParameter(IsInputOutput = true)] int value,
        CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_P478_TvpReport")]
    public partial Task<(IReadOnlyList<P478Item>, IReadOnlyList<P478Line>)> TvpReportAsync(
        [InquiryParameter(TvpTypeName = "[dbo].[P478Ids]")] IEnumerable<int> ids,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Live evidence for each procedure feature that the guide lists as supported on SQL Server (#478).
/// Every procedure that a lifecycle test calls logs one row per execution, so each test can show one
/// execution per call, participation in the caller's transaction, and early cancellation.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class StoredProcedureProviderSupportIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public StoredProcedureProviderSupportIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = """
        CREATE TYPE [dbo].[P478Ids] AS TABLE ([Value] INT NOT NULL);
        CREATE TABLE P478Item (Id INT PRIMARY KEY, Name NVARCHAR(50) NOT NULL, Payload VARBINARY(16) NULL);
        CREATE TABLE P478Line (Id INT PRIMARY KEY, ItemId INT NOT NULL);
        CREATE TABLE P478Call (CallId INT IDENTITY(1,1) PRIMARY KEY);
        INSERT INTO P478Item (Id, Name, Payload) VALUES (1, N'Widget', 0x0102), (2, N'Gadget', NULL), (3, N'Gizmo', 0x03);
        INSERT INTO P478Line (Id, ItemId) VALUES (10, 1), (11, 2), (12, 3);
        EXEC('CREATE PROCEDURE usp_P478_Rows @minId INT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SELECT Id, Name FROM P478Item WHERE Id >= @minId ORDER BY Id; END');
        EXEC('CREATE PROCEDURE usp_P478_One @id INT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SELECT Id, Name FROM P478Item WHERE Id = @id; END');
        EXEC('CREATE PROCEDURE usp_P478_Touch @minId INT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SET NOCOUNT OFF; UPDATE P478Item SET Name = Name WHERE Id >= @minId; END');
        EXEC('CREATE PROCEDURE usp_P478_TouchQuiet @minId INT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; UPDATE P478Item SET Name = Name WHERE Id >= @minId; END');
        EXEC('CREATE PROCEDURE usp_P478_Count @minId INT, @total INT OUTPUT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SELECT @total = COUNT(*) FROM P478Item WHERE Id >= @minId; END');
        EXEC('CREATE PROCEDURE usp_P478_NullOut @total INT OUTPUT AS BEGIN SET NOCOUNT ON; SET @total = NULL; END');
        EXEC('CREATE PROCEDURE usp_P478_NameOf @id INT, @name NVARCHAR(50) OUTPUT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SELECT @name = Name FROM P478Item WHERE Id = @id; END');
        EXEC('CREATE PROCEDURE usp_P478_BytesOf @id INT, @payload VARBINARY(16) OUTPUT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SELECT @payload = Payload FROM P478Item WHERE Id = @id; END');
        EXEC('CREATE PROCEDURE usp_P478_Increment @value INT OUTPUT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SET @value = CASE WHEN @value < 0 THEN NULL ELSE @value + 1 END; END');
        EXEC('CREATE PROCEDURE usp_P478_Label @label NVARCHAR(50) OUTPUT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SET @label = UPPER(@label) + N''!''; END');
        EXEC('CREATE PROCEDURE usp_P478_Payload @payload VARBINARY(16) OUTPUT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SET @payload = @payload + 0xFF; END');
        EXEC('CREATE PROCEDURE usp_P478_Status @id INT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; IF EXISTS (SELECT 1 FROM P478Item WHERE Id = @id) RETURN 0; RETURN 37; END');
        EXEC('CREATE PROCEDURE usp_P478_Report @minId INT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SELECT Id, Name FROM P478Item WHERE Id >= @minId ORDER BY Id; SELECT Id, ItemId FROM P478Line WHERE ItemId >= @minId ORDER BY Id; END');
        EXEC('CREATE PROCEDURE usp_P478_ReportShort AS BEGIN SET NOCOUNT ON; SELECT Id, Name FROM P478Item ORDER BY Id; END');
        EXEC('CREATE PROCEDURE usp_P478_ReportExtra AS BEGIN SET NOCOUNT ON; SELECT Id, Name FROM P478Item ORDER BY Id; SELECT Id, ItemId FROM P478Line ORDER BY Id; SELECT 1 AS Unread; END');
        EXEC('CREATE PROCEDURE usp_P478_NoResult AS BEGIN SET NOCOUNT ON; UPDATE P478Item SET Name = Name WHERE Id = 1; END');
        EXEC('CREATE PROCEDURE usp_P478_Renamed AS BEGIN SET NOCOUNT ON; SELECT Id, Name AS Title FROM P478Item ORDER BY Id; END');
        EXEC('CREATE PROCEDURE usp_P478_Reordered AS BEGIN SET NOCOUNT ON; SELECT Name, 99 AS Extra, Id FROM P478Item ORDER BY Id; END');
        EXEC('CREATE PROCEDURE usp_P478_TvpInOut @ids [dbo].[P478Ids] READONLY, @value INT OUTPUT AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SET @value = @value + (SELECT COUNT(*) FROM @ids); END');
        EXEC('CREATE PROCEDURE usp_P478_TvpReport @ids [dbo].[P478Ids] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO P478Call DEFAULT VALUES; SELECT i.Id, i.Name FROM P478Item i INNER JOIN @ids t ON i.Id = t.[Value] ORDER BY i.Id; SELECT l.Id, l.ItemId FROM P478Line l INNER JOIN @ids t ON l.ItemId = t.[Value] ORDER BY l.Id; END');
        """;

    private async Task<SqlServerTestHarness> CreateHarnessAsync()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        return await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "sproc478", provisionProviderArtifacts: false);
    }

    private static Task<int> CallCountAsync(IInquiry inquiry)
        => inquiry.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM P478Call");

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
    private static async Task AssertLifecycleAsync<T>(SqlServerTestHarness harness, Func<CancellationToken, Task<T>> call, Action<T> assertResult)
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

    private static P478ItemStore Store(SqlServerTestHarness harness) => harness.GetRequiredService<P478ItemStore>();

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
            static item => Assert.Equal("Gizmo", Assert.IsType<P478Item>(item).Name));

        Assert.Null(await Store(harness).OneAsync(99));
        Assert.Empty(await Store(harness).ListAsync(99));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(harness).SingleOfRowsAsync(1));
    }

    [SkippableFact]
    public async Task AffectedRowsRunOncePerCall()
    {
        await using var harness = await CreateHarnessAsync();
        await AssertLifecycleAsync(harness, ct => Store(harness).TouchAsync(2, ct),
            static affected => Assert.Equal(2, affected));

        // SET NOCOUNT ON suppresses the count, so SqlClient reports -1.
        Assert.Equal(-1, await Store(harness).TouchQuietAsync(2));
    }

    [SkippableFact]
    public async Task OutputParameterRunsOncePerCall()
    {
        await using var harness = await CreateHarnessAsync();
        await AssertLifecycleAsync(harness, ct => Store(harness).CountAsync(2, ct),
            static total => Assert.Equal(2, total));
    }

    [SkippableFact]
    public async Task OutputParameterReadsNullStringAndBinaryValues()
    {
        await using var harness = await CreateHarnessAsync();
        var store = Store(harness);

        Assert.Null(await store.NullOutAsync());
        Assert.Equal(0, await store.NullOutAsDefaultAsync());
        Assert.Null(await store.NameOfAsync(99));
        Assert.Null(await store.BytesOfAsync(2));

        await AssertLifecycleAsync(harness, ct => store.NameOfAsync(2, ct),
            static name => Assert.Equal("Gadget", name));
        await AssertLifecycleAsync(harness, ct => store.BytesOfAsync(1, ct),
            static payload => Assert.Equal([0x01, 0x02], payload));
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
    public async Task ReturnValueRunsOncePerCall()
    {
        await using var harness = await CreateHarnessAsync();
        await AssertLifecycleAsync(harness, ct => Store(harness).StatusAsync(99, ct),
            static status => Assert.Equal(37, status));
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
    public async Task TvpCombinesWithInOutAndMultipleResultSets()
    {
        await using var harness = await CreateHarnessAsync();
        await AssertLifecycleAsync(harness, ct => Store(harness).TvpInOutAsync([1, 3], 40, ct),
            static value => Assert.Equal(42, value));

        await AssertLifecycleAsync(harness, ct => Store(harness).TvpReportAsync([1, 3], ct), static report =>
        {
            Assert.Equal([1, 3], report.Item1.Select(static i => i.Id));
            Assert.Equal([10, 12], report.Item2.Select(static l => l.Id));
        });
    }

    [SkippableFact]
    public async Task MissingResultSetThrowsAndExtraResultSetIsIgnored()
    {
        await using var harness = await CreateHarnessAsync();
        var store = Store(harness);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReportShortAsync());
        Assert.Contains("no more result sets", error.Message, StringComparison.Ordinal);

        var (items, lines) = await store.ReportExtraAsync();
        Assert.Equal(3, items.Count);
        Assert.Equal(3, lines.Count);

        // A procedure that returns no result set reads as no rows.
        Assert.Empty(await store.NoResultListAsync());
        Assert.Null(await store.NoResultSingleAsync());
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
