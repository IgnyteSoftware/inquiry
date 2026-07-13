using System.Text.Json;
using Inquiry.Entities;
using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.MariaDb.Tests.Fixtures;
using MySqlConnector;
using Xunit;

namespace Inquiry.MariaDb.Tests;

[InquiryTable("ManifestParent")]
public sealed class ManifestParent { [InquiryKey] public int Id { get; set; } }

[InquiryTable("ManifestWidget")]
[InquiryIndex(nameof(Name), Name = "UX_ManifestWidget_Name", IsUnique = true)]
[InquiryCheck("score >= 0", Name = "CK_ManifestWidget_Score")]
public sealed class ManifestWidget
{
    [InquiryKey(IsGenerated = true)] public int Id { get; set; }
    [InquiryForeignKey("ManifestParent", "Id", ConstraintName = "FK_ManifestWidget_Parent", OnDelete = InquiryReferentialAction.Cascade)] public int ParentId { get; set; }
    [InquiryColumn(Length = 64)] public string Name { get; set; } = string.Empty;
    [InquiryColumn(Length = 128)] public string? Notes { get; set; }
    [InquiryColumn("score", DefaultExpression = "7")] public int Score { get; set; }
    [InquiryColumn(Computed = "score + 1")] public int Total { get; set; }
}

[Collection(MariaDbCollection.Name)]
public sealed class SchemaManifestCatalogIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public SchemaManifestCatalogIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GeneratedManifestMatchesLiveCatalog()
    {
        var ddl = GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "ManifestParent", "ManifestWidget");
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "manifest");
        await using var connection = new MySqlConnection(harness.ConnectionString); await connection.OpenAsync();
        using var manifest = JsonDocument.Parse(InquiryGeneratedSchema.SchemaManifestJson);
        var table = manifest.RootElement.GetProperty("tables").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "ManifestWidget");
        var expected = table.GetProperty("columns").EnumerateArray().ToArray();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT c.column_name,c.column_type,c.is_nullable,(k.ordinal_position-1) FROM information_schema.columns c LEFT JOIN information_schema.key_column_usage k ON k.constraint_schema=c.table_schema AND k.table_name=c.table_name AND k.column_name=c.column_name AND k.constraint_name='PRIMARY' WHERE c.table_schema=DATABASE() AND c.table_name='ManifestWidget' ORDER BY c.ordinal_position";
        await using var reader = await command.ExecuteReaderAsync();
        var ordinal = 0;
        while (await reader.ReadAsync())
        {
            var column = expected[ordinal++];
            Assert.Equal(column.GetProperty("name").GetString(), reader.GetString(0));
            var expectedStoreType = column.GetProperty("storeType").GetString()!;
            Assert.Equal(expectedStoreType, NormalizeCatalogStoreType(reader.GetString(1), expectedStoreType), ignoreCase: true);
            Assert.Equal(column.GetProperty("nullable").GetBoolean(), reader.GetString(2) == "YES");
            var pk = column.GetProperty("primaryKeyOrdinal");
            Assert.Equal(pk.ValueKind == JsonValueKind.Null ? (int?)null : pk.GetInt32(), reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3));
        }
        await reader.DisposeAsync();
        Assert.Equal(expected.Length, ordinal);
        Assert.Equal(new[] { "Id" }, table.GetProperty("primaryKey").EnumerateArray().Select(x => x.GetString()));
        var columns = table.GetProperty("columns").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.Equal("identity", columns["Id"].GetProperty("generation").GetString());
        Assert.Equal("7", columns["score"].GetProperty("defaultExpression").GetString());
        Assert.Equal("score + 1", columns["Total"].GetProperty("computedExpression").GetString());
        var index = Assert.Single(table.GetProperty("indexes").EnumerateArray());
        Assert.True(index.GetProperty("unique").GetBoolean());
        Assert.Equal(new[] { "Name" }, index.GetProperty("keyColumns").EnumerateArray().Select(x => x.GetString()));
        Assert.Empty(index.GetProperty("includeColumns").EnumerateArray());
        Assert.Equal("CK_ManifestWidget_Score", Assert.Single(table.GetProperty("checks").EnumerateArray()).GetProperty("name").GetString());
        var foreignKey = Assert.Single(table.GetProperty("foreignKeys").EnumerateArray());
        Assert.Equal("FK_ManifestWidget_Parent", foreignKey.GetProperty("name").GetString());
        Assert.Equal(new[] { "ParentId" }, foreignKey.GetProperty("localColumns").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(JsonValueKind.Null, foreignKey.GetProperty("referencedSchema").ValueKind);
        Assert.Equal("ManifestParent", foreignKey.GetProperty("referencedTable").GetString());
        Assert.Equal(new[] { "Id" }, foreignKey.GetProperty("referencedColumns").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("cascade", foreignKey.GetProperty("onDelete").GetString());
        Assert.Equal("no-action", foreignKey.GetProperty("onUpdate").GetString());
        await using var checkExpression = connection.CreateCommand();
        checkExpression.CommandText = "SELECT check_clause FROM information_schema.check_constraints WHERE constraint_schema=DATABASE() AND constraint_name='CK_ManifestWidget_Score'";
        Assert.Equal(
            NormalizeCheckExpression(Assert.Single(table.GetProperty("checks").EnumerateArray()).GetProperty("expression").GetString()!),
            NormalizeCheckExpression((string)(await checkExpression.ExecuteScalarAsync())!));
        await using var facts = connection.CreateCommand();
        facts.CommandText = "SELECT (SELECT SUM(extra LIKE '%auto_increment%') FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='ManifestWidget'), (SELECT SUM(column_default='7') FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='ManifestWidget'), (SELECT SUM(generation_expression LIKE '%Score%') FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='ManifestWidget'), (SELECT count(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='ManifestWidget' AND index_name='UX_ManifestWidget_Name' AND non_unique=0), (SELECT count(*) FROM information_schema.table_constraints WHERE constraint_schema=DATABASE() AND table_name='ManifestWidget' AND constraint_name='CK_ManifestWidget_Score' AND constraint_type='CHECK'), (SELECT count(*) FROM information_schema.referential_constraints WHERE constraint_schema=DATABASE() AND table_name='ManifestWidget' AND constraint_name='FK_ManifestWidget_Parent' AND delete_rule='CASCADE' AND update_rule IN ('NO ACTION','RESTRICT'))";
        await using var factsReader = await facts.ExecuteReaderAsync(); Assert.True(await factsReader.ReadAsync());
        Assert.Equal(1, factsReader.GetInt32(0)); Assert.Equal(1, factsReader.GetInt32(1)); Assert.Equal(1, factsReader.GetInt32(2));
        Assert.Equal(1L, factsReader.GetInt64(3)); Assert.Equal(1L, factsReader.GetInt64(4)); Assert.Equal(1L, factsReader.GetInt64(5));
        await factsReader.DisposeAsync();

        await using var structures = connection.CreateCommand();
        structures.CommandText = "SELECT s.column_name FROM information_schema.statistics s WHERE s.table_schema=DATABASE() AND s.table_name='ManifestWidget' AND s.index_name='UX_ManifestWidget_Name' ORDER BY s.seq_in_index; SELECT k.column_name,k.referenced_table_schema,k.referenced_table_name,k.referenced_column_name,r.delete_rule,r.update_rule FROM information_schema.key_column_usage k JOIN information_schema.referential_constraints r ON r.constraint_schema=k.constraint_schema AND r.table_name=k.table_name AND r.constraint_name=k.constraint_name WHERE k.constraint_schema=DATABASE() AND k.table_name='ManifestWidget' AND k.constraint_name='FK_ManifestWidget_Parent' ORDER BY k.ordinal_position";
        await using var structureReader = await structures.ExecuteReaderAsync();
        var catalogKeys = new List<string>();
        while (await structureReader.ReadAsync()) catalogKeys.Add(structureReader.GetString(0));
        Assert.Equal(index.GetProperty("keyColumns").EnumerateArray().Select(x => x.GetString()), catalogKeys);
        Assert.True(await structureReader.NextResultAsync());
        Assert.True(await structureReader.ReadAsync());
        Assert.Equal(Assert.Single(foreignKey.GetProperty("localColumns").EnumerateArray()).GetString(), structureReader.GetString(0));
        Assert.Equal(connection.Database, structureReader.GetString(1));
        Assert.Equal(foreignKey.GetProperty("referencedTable").GetString(), structureReader.GetString(2));
        Assert.Equal(Assert.Single(foreignKey.GetProperty("referencedColumns").EnumerateArray()).GetString(), structureReader.GetString(3));
        Assert.Equal(foreignKey.GetProperty("onDelete").GetString(), structureReader.GetString(4).Replace(' ', '-').ToLowerInvariant());
        Assert.Equal(foreignKey.GetProperty("onUpdate").GetString(), NormalizeCatalogReferentialAction(structureReader.GetString(5)));
        Assert.False(await structureReader.ReadAsync());
    }

    // The catalog adds only identifier delimiters, whitespace, and wrapper parentheses around this predicate.
    private static string NormalizeCheckExpression(string expression)
        => new(expression.Where(character => !char.IsWhiteSpace(character) && character is not '`' and not '(' and not ')').Select(char.ToUpperInvariant).ToArray());

    private static string NormalizeCatalogStoreType(string catalogStoreType, string expectedStoreType)
    {
        // MariaDB reports the legacy signed INT display width as int(11). It is not a storage-width
        // facet. Match only that exact spelling so unsigned and every other physical facet remain visible.
        return expectedStoreType.Equals("INT", StringComparison.OrdinalIgnoreCase)
            && catalogStoreType.Equals("int(11)", StringComparison.OrdinalIgnoreCase)
                ? "INT"
                : catalogStoreType;
    }

    private static string NormalizeCatalogReferentialAction(string catalogAction)
    {
        // MariaDB records an omitted ON UPDATE clause as RESTRICT. For this provider RESTRICT and
        // NO ACTION have the same immediate enforcement semantics; retain every other action exactly.
        return catalogAction.Equals("RESTRICT", StringComparison.OrdinalIgnoreCase)
            ? "no-action"
            : catalogAction.Replace(' ', '-').ToLowerInvariant();
    }
}
