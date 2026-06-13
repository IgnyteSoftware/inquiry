using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("ProcItem")]
public sealed class ProcItem
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }

    [InquiryColumn(Length = 50)]
    public string Category { get; set; } = string.Empty;

    [InquiryColumn]
    public decimal Amount { get; set; }
}

public partial class ProcItemStore : InquiryStore<ProcItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(ProcItem item, CancellationToken cancellationToken = default);

    /// <summary>Reads the summed amount back from an OUTPUT parameter.</summary>
    [InquiryStoredProcedure("usp_SumByCategory", OutputParameter = "Total")]
    public partial Task<decimal> SumByCategoryAsync(string category, CancellationToken cancellationToken = default);

    /// <summary>Reads the row count back from the procedure's RETURN value.</summary>
    [InquiryStoredProcedure("usp_CountByCategory", ReturnsValue = true)]
    public partial Task<int> CountByCategoryAsync(string category, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stored-procedure scalar output against real SQL Server: an <c>OUTPUT</c> parameter and a
/// <c>RETURN</c> value are read back as the generated method's <c>Task&lt;TScalar&gt;</c> result.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class StoredProcedureOutputIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public StoredProcedureOutputIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    // CREATE PROCEDURE must be first in its batch, so each proc is created inside EXEC('…') — that
    // runs it as a nested batch and keeps the whole script in one ExecuteNonQuery.
    private const string Ddl = """
        CREATE TABLE ProcItem (Id INT IDENTITY(1,1) PRIMARY KEY, Category NVARCHAR(50) NOT NULL, Amount DECIMAL(18,2) NOT NULL);
        EXEC('CREATE PROCEDURE usp_SumByCategory @Category NVARCHAR(50), @Total DECIMAL(18,2) OUTPUT AS BEGIN SET NOCOUNT ON; SELECT @Total = COALESCE(SUM(Amount), 0) FROM ProcItem WHERE Category = @Category; END');
        EXEC('CREATE PROCEDURE usp_CountByCategory @Category NVARCHAR(50) AS BEGIN SET NOCOUNT ON; DECLARE @n INT; SELECT @n = COUNT(*) FROM ProcItem WHERE Category = @Category; RETURN @n; END');
        """;

    [SkippableFact]
    public async Task OutputParameterIsReadBackAsResult()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "proc");
        var store = harness.GetRequiredService<ProcItemStore>();

        await store.InsertAsync(new ProcItem { Category = "coffee", Amount = 12.50m });
        await store.InsertAsync(new ProcItem { Category = "coffee", Amount = 7.25m });
        await store.InsertAsync(new ProcItem { Category = "tea", Amount = 4.00m });

        Assert.Equal(19.75m, await store.SumByCategoryAsync("coffee"));
        Assert.Equal(4.00m, await store.SumByCategoryAsync("tea"));
        // COALESCE makes an empty category sum 0 rather than NULL.
        Assert.Equal(0m, await store.SumByCategoryAsync("juice"));
    }

    [SkippableFact]
    public async Task ReturnValueIsReadBackAsResult()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "proc");
        var store = harness.GetRequiredService<ProcItemStore>();

        await store.InsertAsync(new ProcItem { Category = "coffee", Amount = 1m });
        await store.InsertAsync(new ProcItem { Category = "coffee", Amount = 2m });
        await store.InsertAsync(new ProcItem { Category = "tea", Amount = 3m });

        Assert.Equal(2, await store.CountByCategoryAsync("coffee"));
        Assert.Equal(1, await store.CountByCategoryAsync("tea"));
        Assert.Equal(0, await store.CountByCategoryAsync("juice"));
    }
}
