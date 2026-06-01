using System.Threading.Tasks;
using Inquiry;
using Inquiry.Commands;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// W5 runtime scalar path (<c>IInquiry.ExecuteScalarAsync&lt;T&gt;</c>): COUNT returns the value
/// (coerced from SQLite's long), and an aggregate over no rows (NULL) maps to <c>default(T)</c>.
/// </summary>
public sealed class ScalarExecutionTests
{
    private const string Ddl = "CREATE TABLE Nums (X INTEGER NOT NULL); INSERT INTO Nums (X) VALUES (1),(2),(3);";

    [Fact]
    public async Task ExecuteScalarCoercesCount()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Scalar");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var count = await inquiry.ExecuteScalarAsync<long>(new InquiryCommand("SELECT COUNT(*) FROM Nums"));
        Assert.Equal(3L, count);

        var sum = await inquiry.ExecuteScalarAsync<int>(new InquiryCommand("SELECT SUM(X) FROM Nums"));
        Assert.Equal(6, sum);
    }

    [Fact]
    public async Task ExecuteScalarMapsNullAggregateToDefault()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Scalar");
        var inquiry = harness.GetRequiredService<IInquiry>();

        // SUM over no rows is NULL → default(int?) == null.
        var sum = await inquiry.ExecuteScalarAsync<int?>(new InquiryCommand("SELECT SUM(X) FROM Nums WHERE X > 100"));
        Assert.Null(sum);
    }
}
