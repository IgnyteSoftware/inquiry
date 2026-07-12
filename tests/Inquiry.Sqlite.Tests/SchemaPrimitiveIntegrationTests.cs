using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.Sqlite.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Inquiry.Sqlite.Tests;

public sealed class SchemaPrimitiveIntegrationTests
{
    [Fact]
    public async Task GeneratedSchemaPrimitivesAreCatalogedAndEnforced()
    {
        var ddl = GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "PrimitiveParent", "PrimitiveOptionalParent", "PrimitiveChild");
        await using var harness = await SqliteTestHarness.CreateAsync(ddl, "GenPrimitive");
        await using var connection = new SqliteConnection(harness.ConnectionString);
        await connection.OpenAsync();

        Assert.Equal("ParentId,code", await StringAsync(connection, "SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_index_info('IX_PrimitiveChild_Parent_Code') ORDER BY seqno)"));
        Assert.Equal("TenantId,code", await StringAsync(connection, "SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_index_info('UX_PrimitiveChild_Tenant_Code') ORDER BY seqno)"));
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_index_list('PrimitiveChild') WHERE name='UX_PrimitiveChild_Tenant_Code' AND [unique]=1"));
        var tableSql = await StringAsync(connection, "SELECT sql FROM sqlite_master WHERE name='PrimitiveChild'");
        Assert.Contains("CK_PrimitiveChild_Quantity", tableSql);
        Assert.Contains("CK_PrimitiveChild_Code", tableSql);

        await ExecAsync(connection, "INSERT INTO PrimitiveParent VALUES(1); INSERT INTO PrimitiveOptionalParent VALUES(1); INSERT INTO PrimitiveChild VALUES(1,1,1,7,'A',1)");
        await Assert.ThrowsAsync<SqliteException>(() => ExecAsync(connection, "INSERT INTO PrimitiveChild VALUES(2,1,NULL,7,'A',1)"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecAsync(connection, "INSERT INTO PrimitiveChild VALUES(3,1,NULL,8,'B',-1)"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecAsync(connection, "INSERT INTO PrimitiveChild VALUES(4,1,NULL,9,'',1)"));
        await ExecAsync(connection, "DELETE FROM PrimitiveOptionalParent WHERE Id=1");
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM PrimitiveChild WHERE OptionalParentId IS NULL"));
        await ExecAsync(connection, "DELETE FROM PrimitiveParent WHERE Id=1");
        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM PrimitiveChild"));

        var updateDdl = GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "UpdateCascadeParent", "UpdateCascadeChild");
        await ExecAsync(connection, updateDdl);
        await ExecAsync(connection, "INSERT INTO UpdateCascadeParent VALUES(10); INSERT INTO UpdateCascadeChild VALUES(1,10); UPDATE UpdateCascadeParent SET Id=11 WHERE Id=10");
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM UpdateCascadeChild WHERE ParentId=11"));
    }

    private static async Task ExecAsync(SqliteConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    private static async Task<int> ScalarAsync(SqliteConnection connection, string sql) => Convert.ToInt32(await ValueAsync(connection, sql));
    private static async Task<string?> StringAsync(SqliteConnection connection, string sql) => Convert.ToString(await ValueAsync(connection, sql));
    private static async Task<object?> ValueAsync(SqliteConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
}
