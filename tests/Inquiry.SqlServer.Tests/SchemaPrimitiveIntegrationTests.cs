using Inquiry.Entities;
using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("PrimitiveCovering")]
[InquiryIndex(nameof(Category), Name = "IX_PrimitiveCovering_Category", Include = new[] { nameof(Payload) })]
public sealed class PrimitiveCovering
{
    [InquiryKey] public long Id { get; set; }
    [InquiryColumn] public int Category { get; set; }
    [InquiryColumn(Length = 64)] public string Payload { get; set; } = string.Empty;
}

[Collection(SqlServerCollection.Name)]
public sealed class SchemaPrimitiveIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public SchemaPrimitiveIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GeneratedSchemaPrimitivesAreCatalogedAndEnforced()
    {
        var ddl = GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl,
            "PrimitiveParent", "PrimitiveOptionalParent", "PrimitiveChild", "PrimitiveCovering");
        Assert.Contains("([Category]) INCLUDE ([Payload])", ddl);
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "genprimitive", false);
        await using var connection = new SqlConnection(harness.ConnectionString);
        await connection.OpenAsync();

        Assert.Equal("ParentId,code", await StringAsync(connection, "SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.name='IX_PrimitiveChild_Parent_Code' AND ic.is_included_column=0"));
        Assert.Equal("TenantId,code", await StringAsync(connection, "SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.name='UX_PrimitiveChild_Tenant_Code' AND i.is_unique=1 AND ic.is_included_column=0"));
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.name='IX_PrimitiveCovering_Category' AND c.name='Payload' AND ic.is_included_column=1"));
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_PrimitiveChild_Quantity'"));
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_PrimitiveChild_Code'"));
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.foreign_keys WHERE name='FK_PrimitiveChild_Parent' AND delete_referential_action_desc='CASCADE'"));
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.foreign_keys WHERE name='FK_PrimitiveChild_OptionalParent' AND delete_referential_action_desc='SET_NULL'"));
        await AssertEnforcementAsync(connection);
        await ExecAsync(connection, GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "UpdateCascadeParent", "UpdateCascadeChild"));
        await ExecAsync(connection, "INSERT INTO UpdateCascadeParent VALUES(10); INSERT INTO UpdateCascadeChild VALUES(1,10); UPDATE UpdateCascadeParent SET Id=11 WHERE Id=10");
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM UpdateCascadeChild WHERE ParentId=11"));
    }

    private static async Task AssertEnforcementAsync(SqlConnection c)
    {
        await ExecAsync(c, "INSERT INTO PrimitiveParent (Id) VALUES (1); INSERT INTO PrimitiveOptionalParent (Id) VALUES (1); INSERT INTO PrimitiveChild (Id,ParentId,OptionalParentId,TenantId,Code,Quantity) VALUES (1,1,1,7,'A',1)");
        await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, "INSERT INTO PrimitiveChild (Id,ParentId,TenantId,Code,Quantity) VALUES (2,1,7,'A',1)"));
        await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, "INSERT INTO PrimitiveChild (Id,ParentId,TenantId,Code,Quantity) VALUES (3,1,8,'B',-1)"));
        await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, "INSERT INTO PrimitiveChild (Id,ParentId,TenantId,Code,Quantity) VALUES (4,1,9,'',1)"));
        await ExecAsync(c, "DELETE FROM PrimitiveOptionalParent WHERE Id=1");
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM PrimitiveChild WHERE Id=1 AND OptionalParentId IS NULL"));
        await ExecAsync(c, "DELETE FROM PrimitiveParent WHERE Id=1");
        Assert.Equal(0, await ScalarAsync(c, "SELECT COUNT(*) FROM PrimitiveChild"));
    }

    private static async Task ExecAsync(SqlConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }
    private static async Task<int> ScalarAsync(SqlConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt32(await cmd.ExecuteScalarAsync()); }
    private static async Task<string?> StringAsync(SqlConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToString(await cmd.ExecuteScalarAsync()); }
}
