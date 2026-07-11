using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Generated;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("TenantGadget", Schema = "9 odd].schema'")]
public sealed class TenantGadget
{
    [InquiryKey(IsGenerated = true)] public int Id { get; set; }
    [InquiryColumn] public string Name { get; set; } = string.Empty;
    [InquiryColumn] public bool IsActive { get; set; }
}

public partial class TenantGadgetStore : InquiryStore<TenantGadget>
{
    [InquiryInsert] public partial Task<int> InsertAsync(TenantGadget gadget, CancellationToken cancellationToken = default);
    [InquiryCount] public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
    [InquiryDeleteAll] public partial Task<int> DeleteAllAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
    [InquiryExists, InquiryWhere("IsActive", Compare.In)] public partial Task<bool> ExistsActiveAsync(IReadOnlyList<bool> values, CancellationToken cancellationToken = default);
}

[Collection(SqlServerCollection.Name)]
public sealed class TvpArtifactIntegrationTests
{
    private const string GadgetTableDdl =
        "CREATE TABLE [Gadget] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [Name] NVARCHAR(MAX) NOT NULL);";
    private const string TenantTableDdl =
        "CREATE TABLE [9 odd]].schema'].[TenantGadget] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [Name] NVARCHAR(MAX) NOT NULL, [IsActive] BIT NOT NULL);";

    private readonly SqlServerContainerFixture _fixture;
    public TvpArtifactIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task MissingArtifactIsReportedThenSetupAllowsRetry()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, GadgetTableDdl, "tvpmissing", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<GadgetStore>();
        await store.InsertAsync(new Gadget { Name = "one" });

        var missingBefore = await ReadValidationAsync(harness.ConnectionString);
        Assert.Contains(missingBefore, row => row.Name == "[dbo].[Inquiry_Tvp_5fcff71acdcd2dc2f2d9b8c73ef6cfb000902eeb236c89d2221808eb2617bbee]" && row.Signature == "int");
        var missingException = await Assert.ThrowsAsync<SqlException>(() => store.DeleteAllAsync(new[] { 1 }));
        Assert.Contains("Inquiry_Tvp_5fcff71acdcd2dc2f2d9b8c73ef6cfb000902eeb236c89d2221808eb2617bbee", missingException.Message);

        await ExecuteAsync(harness.ConnectionString, "CREATE TYPE [dbo].[Legacy_Unrelated_Type] AS TABLE ([Value] INT NOT NULL);");

        await ExecuteAsync(harness.ConnectionString, InquiryGeneratedSchema.ProviderArtifactsDdl);
        await ExecuteAsync(harness.ConnectionString, InquiryGeneratedSchema.ProviderArtifactsDdl);

        Assert.Empty(await ReadValidationAsync(harness.ConnectionString));
        Assert.Equal(1, await CountRowsAsync(harness.ConnectionString, "SELECT 1 FROM sys.table_types WHERE [name] = N'Legacy_Unrelated_Type'"));
        Assert.Equal(1, await store.DeleteAllAsync(new[] { 1 }));
    }

    [SkippableFact]
    public async Task ProvisionedArtifactsWorkInCustomSchemaAndAmbientTransaction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            InquiryGeneratedSchema.ProviderArtifactsDdl + TenantTableDdl,
            "tvptenant");
        var store = harness.GetRequiredService<TenantGadgetStore>();
        await store.InsertAsync(new TenantGadget { Name = "tenant", IsActive = true });
        Assert.True(await store.ExistsActiveAsync(new[] { true }));
        Assert.Equal(1, await CountRowsAsync(harness.ConnectionString,
            "SELECT 1 FROM sys.table_types t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.[name] = N'Inquiry_Tvp_99018d95b2ee7c9aa52743c067fc764dc8be06f1824e3aeef4085921e1ce24c7' AND s.[name] = N'9 odd].schema'''"));
        Assert.Equal(0, await CountRowsAsync(harness.ConnectionString,
            "SELECT 1 FROM sys.table_types t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.[name] = N'Inquiry_Tvp_99018d95b2ee7c9aa52743c067fc764dc8be06f1824e3aeef4085921e1ce24c7' AND s.[name] <> N'9 odd].schema'''"));
        var inquiry = harness.GetRequiredService<global::Inquiry.IInquiry>();

        await using (var transaction = await inquiry.BeginTransactionAsync())
        {
            Assert.Equal(1, await store.DeleteAllAsync(new[] { 1 }));
            await transaction.RollbackAsync();
        }

        Assert.Equal(1, await store.CountAsync());
        Assert.Equal(1, await store.DeleteAllAsync(new[] { 1 }));
    }

    [SkippableFact]
    public async Task ProvisionedArtifactsAreDatabaseLocalAndSupportConcurrentUse()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var ddl = InquiryGeneratedSchema.ProviderArtifactsDdl + GadgetTableDdl;
        await using var first = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "tvpdb1");
        await using var second = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "tvpdb2");
        var firstStore = first.GetRequiredService<GadgetStore>();
        var secondStore = second.GetRequiredService<GadgetStore>();

        for (var i = 0; i < 12; i++)
        {
            await firstStore.InsertAsync(new Gadget { Name = "first" + i });
            await secondStore.InsertAsync(new Gadget { Name = "second" + i });
        }

        var operations = Enumerable.Range(1, 12)
            .Select(id => firstStore.DeleteAllAsync(new[] { id }))
            .Append(secondStore.DeleteAllAsync(Enumerable.Range(1, 12).ToArray()));
        var affected = await Task.WhenAll(operations);

        Assert.All(affected, count => Assert.True(count > 0));
        Assert.Equal(0, await firstStore.CountAsync());
        Assert.Equal(0, await secondStore.CountAsync());
        var expectedArtifactCount = CountOccurrences(InquiryGeneratedSchema.ProviderArtifactsDdl, "CREATE TYPE");
        Assert.Equal(expectedArtifactCount, await CountRowsAsync(first.ConnectionString, "SELECT 1 FROM sys.table_types WHERE [name] LIKE N'Inquiry_Tvp_%'"));
        Assert.Equal(expectedArtifactCount, await CountRowsAsync(second.ConnectionString, "SELECT 1 FROM sys.table_types WHERE [name] LIKE N'Inquiry_Tvp_%'"));
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountRowsAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync()) count++;
        return count;
    }

    private static async Task<List<(string Name, string Signature)>> ReadValidationAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = InquiryGeneratedSchema.ProviderArtifactsValidationSql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(search, index, System.StringComparison.Ordinal)) >= 0; index += search.Length) count++;
        return count;
    }
}
