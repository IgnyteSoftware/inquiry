using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Inquiry.Entities;
using Inquiry.Generated;
using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.Sqlite;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("SchemaWidgetParent")]
public sealed class SchemaWidgetParent { [InquiryKey] public long Id { get; set; } }

[InquiryTable("SchemaWidget")]
[InquiryIndex(nameof(Name), Name = "UX_SchemaWidget_Name", IsUnique = true)]
[InquiryCheck("score >= 0", Name = "CK_SchemaWidget_Score")]
public sealed class SchemaWidget
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("Weight")]
    public double Weight { get; set; }

    [InquiryColumn("Notes")]
    public string? Notes { get; set; }

    [InquiryForeignKey("SchemaWidgetParent", "Id", ConstraintName = "FK_SchemaWidget_Parent", OnDelete = InquiryReferentialAction.Cascade)]
    public long? ParentId { get; set; }

    [InquiryColumn("score", DefaultExpression = "7")] public int Score { get; set; }
    [InquiryColumn(Computed = "score + 1")] public int Total { get; set; }
}

public partial class SchemaWidgetStore : InquiryStore<SchemaWidget>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(SchemaWidget widget, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<SchemaWidget>> AllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Round-trip: the generated <see cref="InquiryGeneratedSchema.Ddl"/> for this assembly executes
/// against a fresh SQLite database (proving every generated CREATE TABLE is valid SQLite), then a store
/// performs a real insert/select round-trip against the generated table.
/// </summary>
public sealed class SchemaDdlIntegrationTests
{
    [Fact]
    public async Task GeneratedManifestRepresentativeTableMatchesLiveCatalog()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(InquiryGeneratedSchema.Ddl, "ManifestCatalog");
        await using var connection = new SqliteConnection(harness.ConnectionString);
        await connection.OpenAsync();

        using var manifest = JsonDocument.Parse(InquiryGeneratedSchema.SchemaManifestJson);
        var table = manifest.RootElement.GetProperty("tables").EnumerateArray()
            .Single(candidate => candidate.GetProperty("name").GetString() == "SchemaWidget");
        var expected = table.GetProperty("columns").EnumerateArray().ToArray();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, type, [notnull], pk FROM pragma_table_xinfo('SchemaWidget') ORDER BY cid";
        await using var reader = await command.ExecuteReaderAsync();
        var ordinal = 0;
        while (await reader.ReadAsync())
        {
            var column = expected[ordinal++];
            Assert.Equal(column.GetProperty("name").GetString(), reader.GetString(0));
            if (column.GetProperty("storeType").ValueKind != JsonValueKind.Null)
                Assert.Equal(column.GetProperty("storeType").GetString(), reader.GetString(1), ignoreCase: true);
            var primaryKeyOrdinal = column.GetProperty("primaryKeyOrdinal");
            var expectedPrimaryKeyOrdinal = primaryKeyOrdinal.ValueKind == JsonValueKind.Null ? 0 : primaryKeyOrdinal.GetInt32() + 1;
            Assert.Equal(expectedPrimaryKeyOrdinal, reader.GetInt64(3));
            // SQLite reports NOT NULL = 0 for an INTEGER PRIMARY KEY even though the key is non-nullable.
            if (expectedPrimaryKeyOrdinal == 0)
                Assert.Equal(!column.GetProperty("nullable").GetBoolean(), reader.GetInt64(2) != 0);
        }
        await reader.DisposeAsync();
        Assert.Equal(expected.Length, ordinal);
        Assert.Equal(new[] { "Id" }, table.GetProperty("primaryKey").EnumerateArray().Select(value => value.GetString()));
        var columns = table.GetProperty("columns").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.Equal("identity", columns["Id"].GetProperty("generation").GetString()); Assert.Equal("7", columns["score"].GetProperty("defaultExpression").GetString());
        Assert.Equal("score + 1", columns["Total"].GetProperty("computedExpression").GetString());
        var index = Assert.Single(table.GetProperty("indexes").EnumerateArray()); Assert.True(index.GetProperty("unique").GetBoolean()); Assert.Empty(index.GetProperty("includeColumns").EnumerateArray());
        Assert.Equal(new[] { "Name" }, index.GetProperty("keyColumns").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("CK_SchemaWidget_Score", Assert.Single(table.GetProperty("checks").EnumerateArray()).GetProperty("name").GetString());
        var foreignKey = Assert.Single(table.GetProperty("foreignKeys").EnumerateArray());
        Assert.Equal("FK_SchemaWidget_Parent", foreignKey.GetProperty("name").GetString());
        Assert.Equal(new[] { "ParentId" }, foreignKey.GetProperty("localColumns").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(JsonValueKind.Null, foreignKey.GetProperty("referencedSchema").ValueKind);
        Assert.Equal("SchemaWidgetParent", foreignKey.GetProperty("referencedTable").GetString());
        Assert.Equal(new[] { "Id" }, foreignKey.GetProperty("referencedColumns").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("cascade", foreignKey.GetProperty("onDelete").GetString());
        Assert.Equal("no-action", foreignKey.GetProperty("onUpdate").GetString());

        await using var facts = connection.CreateCommand();
        facts.CommandText = "SELECT (SELECT count(*) FROM pragma_table_xinfo('SchemaWidget') WHERE name='Id' AND pk=1), (SELECT count(*) FROM pragma_table_xinfo('SchemaWidget') WHERE name='score' AND dflt_value='7'), (SELECT count(*) FROM pragma_table_xinfo('SchemaWidget') WHERE name='Total' AND hidden IN (2,3)), (SELECT count(*) FROM pragma_index_list('SchemaWidget') WHERE name='UX_SchemaWidget_Name' AND [unique]=1), (SELECT count(*) FROM pragma_foreign_key_list('SchemaWidget') WHERE [table]='SchemaWidgetParent' AND [to]='Id' AND on_delete='CASCADE')";
        await using var factsReader = await facts.ExecuteReaderAsync(); Assert.True(await factsReader.ReadAsync());
        for (var i = 0; i < 5; i++) Assert.Equal(1L, factsReader.GetInt64(i));
        await factsReader.DisposeAsync();

        await using var tableSqlCommand = connection.CreateCommand();
        tableSqlCommand.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='SchemaWidget'";
        var tableSql = Assert.IsType<string>(await tableSqlCommand.ExecuteScalarAsync());
        var normalizedTableSql = NormalizeSql(tableSql);
        Assert.Contains("primarykeyautoincrement", normalizedTableSql);
        Assert.Contains(NormalizeSql(columns["score"].GetProperty("defaultExpression").GetString()!), normalizedTableSql);
        Assert.Contains(NormalizeSql(columns["Total"].GetProperty("computedExpression").GetString()!), normalizedTableSql);
        Assert.Contains(NormalizeSql(Assert.Single(table.GetProperty("checks").EnumerateArray()).GetProperty("expression").GetString()!), normalizedTableSql);

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = "SELECT name FROM pragma_index_xinfo('UX_SchemaWidget_Name') WHERE [key]=1 ORDER BY seqno";
        var catalogIndexColumns = new List<string>();
        await using (var indexReader = await indexCommand.ExecuteReaderAsync())
            while (await indexReader.ReadAsync()) catalogIndexColumns.Add(indexReader.GetString(0));
        Assert.Equal(index.GetProperty("keyColumns").EnumerateArray().Select(value => value.GetString()), catalogIndexColumns);

        await using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText = "SELECT [from], [table], [to], on_delete, on_update FROM pragma_foreign_key_list('SchemaWidget') ORDER BY id, seq";
        await using var foreignKeyReader = await foreignKeyCommand.ExecuteReaderAsync();
        Assert.True(await foreignKeyReader.ReadAsync());
        Assert.Equal(Assert.Single(foreignKey.GetProperty("localColumns").EnumerateArray()).GetString(), foreignKeyReader.GetString(0));
        Assert.Equal(foreignKey.GetProperty("referencedTable").GetString(), foreignKeyReader.GetString(1));
        Assert.Equal(Assert.Single(foreignKey.GetProperty("referencedColumns").EnumerateArray()).GetString(), foreignKeyReader.GetString(2));
        Assert.Equal(foreignKey.GetProperty("onDelete").GetString(), foreignKeyReader.GetString(3).Replace(' ', '-').ToLowerInvariant());
        Assert.Equal(foreignKey.GetProperty("onUpdate").GetString(), foreignKeyReader.GetString(4).Replace(' ', '-').ToLowerInvariant());
        Assert.False(await foreignKeyReader.ReadAsync());
    }

    [Fact]
    public async Task GeneratedSchemaExecutesAndRoundTripsCrud()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(InquiryGeneratedSchema.Ddl, "GenSchema");
        var store = harness.GetRequiredService<SchemaWidgetStore>();

        await store.InsertAsync(new SchemaWidget { Name = "Gadget", Weight = 2.5, Notes = null });
        await store.InsertAsync(new SchemaWidget { Name = "Gizmo", Weight = 4.0, Notes = "spare" });

        var all = await store.AllAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, w => w.Name == "Gadget" && w.Weight == 2.5 && w.Notes == null);
        Assert.Contains(all, w => w.Name == "Gizmo" && w.Notes == "spare");
    }

    [Fact]
    public async Task GeneratedSchemaSupportsCyclicAndSelfReferencingForeignKeys()
    {
        var ddl = CyclicForeignKeyDdl.Extract(InquiryGeneratedSchema.Ddl);
        Assert.DoesNotContain("ALTER TABLE \"Cyclic", ddl);
        Assert.Contains("REFERENCES \"CyclicAlpha\"", ddl);
        Assert.Contains("REFERENCES \"CyclicBeta\"", ddl);

        await using var harness = await SqliteTestHarness.CreateAsync(ddl, "GenCycle");
        await using var connection = new SqliteConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, BetaId, ParentId) VALUES (1, NULL, NULL)");
        await ExecuteAsync(connection, "INSERT INTO CyclicBeta (Id, AlphaId) VALUES (1, 1)");
        await ExecuteAsync(connection, "UPDATE CyclicAlpha SET BetaId = 1, ParentId = 1 WHERE Id = 1");

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, BetaId) VALUES (2, 999)"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO CyclicBeta (Id, AlphaId) VALUES (2, 999)"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, ParentId) VALUES (3, 999)"));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string NormalizeSql(string sql)
        => new(sql.Where(character => !char.IsWhiteSpace(character) && character is not '"' and not '[' and not ']' and not '`' and not '(' and not ')')
            .Select(char.ToLowerInvariant).ToArray());
}
