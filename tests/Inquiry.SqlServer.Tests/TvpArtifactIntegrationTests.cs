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
    [InquiryDelete, InquiryWhere("Id", Compare.In)] public partial Task<int> DeleteAllAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
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
        Assert.Contains(missingBefore, row => row.Name == "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]" && row.Signature == "int|nullable=0" && row.Status == "missing");
        var missingException = await Assert.ThrowsAsync<SqlException>(() => store.DeleteAllAsync(new[] { 1 }));
        Assert.Contains("Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c", missingException.Message);

        await ExecuteAsync(harness.ConnectionString, "CREATE TYPE [dbo].[Legacy_Unrelated_Type] AS TABLE ([Value] INT NOT NULL);");

        await ExecuteAsync(harness.ConnectionString, InquiryGeneratedSchema.ProviderArtifactsDdl);
        await ExecuteAsync(harness.ConnectionString, InquiryGeneratedSchema.ProviderArtifactsDdl);

        Assert.Empty(await ReadValidationAsync(harness.ConnectionString));
        Assert.Equal(1, await CountRowsAsync(harness.ConnectionString, "SELECT 1 FROM sys.table_types WHERE [name] = N'Legacy_Unrelated_Type'"));
        Assert.Equal(1, await store.DeleteAllAsync(new[] { 1 }));
    }

    [SkippableFact]
    public async Task ValidationReportsPhysicalFacetNullabilityColumnAndConstraintMismatches()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        const string mismatchedTypes = """
            CREATE TYPE [dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c] AS TABLE ([Value] BIGINT NOT NULL);
            CREATE TYPE [dbo].[Inquiry_Tvp_5b86da9c743bc5a42f435d80fa53e2211aaf39d0c63e0667d10e22f8568e363d] AS TABLE ([Value] VARCHAR(36) NOT NULL);
            CREATE TYPE [dbo].[Inquiry_Tvp_6d293380161186274625aaedc8f873365b00c21142df94f623c6da21797e3c95] AS TABLE ([Value] DECIMAL(28,6) NOT NULL);
            CREATE TYPE [dbo].[Inquiry_Tvp_f7ac397aaa62fa2481870cb41e3566b81a4abb655ad27377bb06ea1a6d9d43b1] AS TABLE ([Value] VARBINARY(16) NOT NULL);
            CREATE TYPE [dbo].[Inquiry_Tvp_931ec7b250e6f015c53a7d76dcba53455cc23f51619180df1aab1b8c9bcd1b20] AS TABLE ([Value] DATETIMEOFFSET(2) NOT NULL);
            CREATE TYPE [dbo].[Inquiry_Tvp_2d174cb3d85ff203a565bc6e937dc8b2699fe31ad7d60bf974d202d8310c9d35] AS TABLE ([Value] INT NOT NULL);
            CREATE TYPE [dbo].[Inquiry_Tvp_f85bdace93140f453274ed1a4437867bf88e27efbf7c01452adbc53e9e0c0ef2] AS TABLE ([Value] CHAR(5) NOT NULL PRIMARY KEY);
            CREATE TYPE [dbo].[Inquiry_Tvp_77099780825d001b56d65e3b803ce36bdb8704598526311209bd2a8531932481] AS TABLE ([Value] BINARY(4) NOT NULL, [Extra] INT NULL);
            """;
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            mismatchedTypes,
            "tvpmismatch",
            provisionProviderArtifacts: false);

        var rows = await ReadValidationAsync(harness.ConnectionString);
        var mismatched = rows.Where(static row => row.Status == "mismatched").ToDictionary(static row => row.Signature);

        Assert.Contains("int|nullable=0", mismatched.Keys);
        Assert.Contains("varchar(37)|nullable=0", mismatched.Keys);
        Assert.Contains("decimal(29,7)|nullable=0", mismatched.Keys);
        Assert.Contains("varbinary(17)|nullable=0", mismatched.Keys);
        Assert.Contains("datetimeoffset(3)|nullable=0", mismatched.Keys);
        Assert.Contains("int|nullable=1", mismatched.Keys);
        Assert.Contains("char(5)|nullable=0", mismatched.Keys);
        Assert.Contains("binary(4)|nullable=0", mismatched.Keys);
        Assert.All(mismatched.Values, static row => Assert.Contains("does not exactly match", row.Details));
    }

    [SkippableTheory]
    [InlineData("alias", "CREATE TYPE [dbo].[AliasInt] FROM INT NOT NULL; EXEC(N'CREATE TYPE [dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c] AS TABLE ([Value] [dbo].[AliasInt] NOT NULL)');", "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]")]
    [InlineData("name", "CREATE TYPE [dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c] AS TABLE ([Wrong] INT NOT NULL);", "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]")]
    [InlineData("ordinal", "CREATE TYPE [dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c] AS TABLE ([Extra] INT NULL, [Value] INT NOT NULL);", "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]")]
    [InlineData("check", "CREATE TYPE [dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c] AS TABLE ([Value] INT NOT NULL CHECK ([Value] > 0));", "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]")]
    [InlineData("default", "CREATE TYPE [dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c] AS TABLE ([Value] INT NOT NULL DEFAULT (0));", "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]")]
    [InlineData("identity", "CREATE TYPE [dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c] AS TABLE ([Value] INT IDENTITY(1,1) NOT NULL);", "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]")]
    [InlineData("computed", "CREATE TYPE [dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c] AS TABLE ([Value] INT NOT NULL, [Computed] AS ([Value] + 1));", "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]")]
    [InlineData("collation", "CREATE TYPE [dbo].[Inquiry_Tvp_f2eaaa262a5392ae45922f38ea30b9ed4c414a6e6c502340e41458a5e1eded0f] AS TABLE ([Value] NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL);", "[dbo].[Inquiry_Tvp_f2eaaa262a5392ae45922f38ea30b9ed4c414a6e6c502340e41458a5e1eded0f]")]
    public async Task ValidationRejectsEveryCatalogShapeMismatch(string caseName, string ddl, string artifactName)
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            ddl,
            "tvpcatalog" + caseName,
            provisionProviderArtifacts: false);

        var row = Assert.Single(await ReadValidationAsync(harness.ConnectionString), value => value.Name == artifactName);
        Assert.Equal("mismatched", row.Status);
        Assert.Contains("does not exactly match", row.Details);
    }

    [SkippableFact]
    public async Task LeastPrivilegeValidationDistinguishesInvisibleMetadataFromMissingTypes()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, GadgetTableDdl, "tvpvisibility");
        var login = "inq_tvp_" + System.Guid.NewGuid().ToString("N");
        const string password = "Inquiry_Tvp_69!aA123";

        try
        {
            await ExecuteAsync(_fixture.AdminConnectionString, $"CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;");
            await ExecuteAsync(harness.ConnectionString, $"CREATE USER [{login}] FOR LOGIN [{login}]; GRANT CONNECT TO [{login}];");
            var limited = new SqlConnectionStringBuilder(harness.ConnectionString)
            {
                IntegratedSecurity = false,
                UserID = login,
                Password = password,
                Pooling = false,
            }.ToString();

            var rows = await ReadValidationAsync(limited);

            Assert.NotEmpty(rows);
            Assert.All(rows, static row => Assert.Equal("metadata-invisible", row.Status));
            Assert.All(rows, static row => Assert.Contains("not visible", row.Details));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await ExecuteAsync(_fixture.AdminConnectionString, $"IF SUSER_ID(N'{login}') IS NOT NULL DROP LOGIN [{login}];");
        }
    }

    [SkippableFact]
    public async Task ProvisionedArtifactsWorkInCustomSchemaAndAmbientTransaction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            TenantTableDdl,
            "tvptenant");
        var store = harness.GetRequiredService<TenantGadgetStore>();
        await store.InsertAsync(new TenantGadget { Name = "tenant", IsActive = true });
        Assert.True(await store.ExistsActiveAsync(new[] { true }));
        Assert.Equal(1, await CountRowsAsync(harness.ConnectionString,
            "SELECT 1 FROM sys.table_types t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.[name] = N'Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c' AND s.[name] = N'9 odd].schema'''"));
        Assert.Equal(1, await CountRowsAsync(harness.ConnectionString,
            "SELECT 1 FROM sys.table_types t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.[name] = N'Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c' AND s.[name] <> N'9 odd].schema'''"));
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
        await using var first = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, GadgetTableDdl, "tvpdb1");
        await using var second = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, GadgetTableDdl, "tvpdb2");
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

    private static async Task<List<ValidationRow>> ReadValidationAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = InquiryGeneratedSchema.ProviderArtifactsValidationSql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ValidationRow>();
        while (await reader.ReadAsync()) rows.Add(new ValidationRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }

    private sealed record ValidationRow(string Name, string Signature, string Status, string Details);

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(search, index, System.StringComparison.Ordinal)) >= 0; index += search.Length) count++;
        return count;
    }
}
