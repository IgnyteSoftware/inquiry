using System.Text.Json;
using Inquiry.Entities;
using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.PostgreSql.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace Inquiry.PostgreSql.Tests;

[InquiryTable("ManifestParent")]
public sealed class ManifestParent { [InquiryKey] public int Id { get; set; } }

[InquiryTable("ManifestWidget")]
[InquiryIndex(nameof(Name), Name = "UX_ManifestWidget_Name", IsUnique = true, Include = [nameof(Notes)])]
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

[Collection(PostgreSqlCollection.Name)]
public sealed class SchemaManifestCatalogIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public SchemaManifestCatalogIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task GeneratedManifestMatchesLiveCatalog()
    {
        var ddl = GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, "ManifestParent", "ManifestWidget");
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "manifest");
        await using var connection = new NpgsqlConnection(harness.ConnectionString);
        await connection.OpenAsync();

        using var manifest = JsonDocument.Parse(InquiryGeneratedSchema.SchemaManifestJson);
        var table = manifest.RootElement.GetProperty("tables").EnumerateArray()
            .Single(candidate => candidate.GetProperty("name").GetString() == "ManifestWidget");
        var expected = table.GetProperty("columns").EnumerateArray().ToArray();

        await using var command = connection.CreateCommand();
        // int2vector retains PostgreSQL's zero array lower bound when cast to smallint[]. Subtract
        // that actual bound rather than assuming a one-based array; array_position remains NULL for
        // non-key columns, preserving the manifest's null primaryKeyOrdinal contract.
        command.CommandText = """
            SELECT a.attname,
                   CASE WHEN a.atttypid = 'varchar'::regtype THEN 'VARCHAR(' || (a.atttypmod - 4) || ')' ELSE format_type(a.atttypid, a.atttypmod) END,
                   NOT a.attnotnull,
                   array_position(i.indkey::smallint[], a.attnum::smallint)
                       - array_lower(i.indkey::smallint[], 1)
            FROM pg_attribute a
            JOIN pg_class t ON t.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            LEFT JOIN pg_index i ON i.indrelid = t.oid AND i.indisprimary
            WHERE n.nspname = 'public' AND t.relname = 'ManifestWidget' AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var ordinal = 0;
        while (await reader.ReadAsync())
        {
            var column = expected[ordinal++];
            Assert.Equal(column.GetProperty("name").GetString(), reader.GetString(0));
            Assert.Equal(column.GetProperty("storeType").GetString(), reader.GetString(1), ignoreCase: true);
            Assert.Equal(column.GetProperty("nullable").GetBoolean(), reader.GetBoolean(2));
            var pk = column.GetProperty("primaryKeyOrdinal");
            Assert.Equal(pk.ValueKind == JsonValueKind.Null ? (int?)null : pk.GetInt32(), reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3));
        }
        await reader.DisposeAsync();
        Assert.Equal(expected.Length, ordinal);
        Assert.Equal(new[] { "Id" }, table.GetProperty("primaryKey").EnumerateArray().Select(value => value.GetString()));
        var columns = table.GetProperty("columns").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.Equal("identity", columns["Id"].GetProperty("generation").GetString());
        Assert.Equal("7", columns["score"].GetProperty("defaultExpression").GetString());
        Assert.Equal("score + 1", columns["Total"].GetProperty("computedExpression").GetString());
        var index = Assert.Single(table.GetProperty("indexes").EnumerateArray());
        Assert.True(index.GetProperty("unique").GetBoolean());
        Assert.Equal(new[] { "Name" }, index.GetProperty("keyColumns").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "Notes" }, index.GetProperty("includeColumns").EnumerateArray().Select(x => x.GetString()));
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
        checkExpression.CommandText = "SELECT pg_get_constraintdef(c.oid) FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname='public' AND t.relname='ManifestWidget' AND c.conname='CK_ManifestWidget_Score' AND c.contype='c'";
        Assert.Equal(
            NormalizeCheckExpression(Assert.Single(table.GetProperty("checks").EnumerateArray()).GetProperty("expression").GetString()!),
            NormalizeCheckExpression((string)(await checkExpression.ExecuteScalarAsync())!));
        await using var facts = connection.CreateCommand();
        // The provider intentionally emits SERIAL for generated integer keys. PostgreSQL exposes that
        // as a nextval(...) default rather than information_schema.columns.is_identity = YES.
        facts.CommandText = "SELECT count(*) FILTER (WHERE column_name='Id' AND (is_identity='YES' OR column_default LIKE 'nextval(%')), count(*) FILTER (WHERE column_default='7'::text), count(*) FILTER (WHERE is_generated='ALWAYS'), (SELECT count(*) FROM pg_indexes WHERE schemaname='public' AND tablename='ManifestWidget' AND indexname='UX_ManifestWidget_Name' AND indexdef LIKE 'CREATE UNIQUE%INCLUDE%Notes%'), (SELECT count(*) FROM information_schema.table_constraints WHERE table_schema='public' AND table_name='ManifestWidget' AND constraint_name='CK_ManifestWidget_Score' AND constraint_type='CHECK'), (SELECT count(*) FROM information_schema.referential_constraints WHERE constraint_schema='public' AND constraint_name='FK_ManifestWidget_Parent' AND delete_rule='CASCADE' AND update_rule='NO ACTION') FROM information_schema.columns WHERE table_schema='public' AND table_name='ManifestWidget'";
        await using var factsReader = await facts.ExecuteReaderAsync(); Assert.True(await factsReader.ReadAsync());
        Assert.Equal(1L, factsReader.GetInt64(0)); Assert.Equal(1L, factsReader.GetInt64(1)); Assert.Equal(1L, factsReader.GetInt64(2));
        Assert.Equal(1L, factsReader.GetInt64(3)); Assert.Equal(1L, factsReader.GetInt64(4)); Assert.Equal(1L, factsReader.GetInt64(5));
        await factsReader.DisposeAsync();

        await using var structures = connection.CreateCommand();
        structures.CommandText = "SELECT a.attname, x.ordinality > i.indnkeyatts FROM pg_class t JOIN pg_namespace n ON n.oid=t.relnamespace JOIN pg_index i ON i.indrelid=t.oid JOIN pg_class ix ON ix.oid=i.indexrelid CROSS JOIN LATERAL unnest(i.indkey) WITH ORDINALITY x(attnum, ordinality) JOIN pg_attribute a ON a.attrelid=t.oid AND a.attnum=x.attnum WHERE n.nspname='public' AND t.relname='ManifestWidget' AND ix.relname='UX_ManifestWidget_Name' ORDER BY x.ordinality; SELECT kcu.column_name,ccu.table_schema,ccu.table_name,ccu.column_name,rc.delete_rule,rc.update_rule FROM information_schema.key_column_usage kcu JOIN information_schema.referential_constraints rc ON rc.constraint_schema=kcu.constraint_schema AND rc.constraint_name=kcu.constraint_name JOIN information_schema.constraint_column_usage ccu ON ccu.constraint_schema=rc.unique_constraint_schema AND ccu.constraint_name=rc.unique_constraint_name WHERE kcu.table_schema='public' AND kcu.table_name='ManifestWidget' AND kcu.constraint_name='FK_ManifestWidget_Parent' ORDER BY kcu.ordinal_position";
        await using var structureReader = await structures.ExecuteReaderAsync();
        var catalogKeys = new List<string>(); var catalogIncludes = new List<string>();
        while (await structureReader.ReadAsync()) (structureReader.GetBoolean(1) ? catalogIncludes : catalogKeys).Add(structureReader.GetString(0));
        Assert.Equal(index.GetProperty("keyColumns").EnumerateArray().Select(x => x.GetString()), catalogKeys);
        Assert.Equal(index.GetProperty("includeColumns").EnumerateArray().Select(x => x.GetString()), catalogIncludes);
        Assert.True(await structureReader.NextResultAsync()); Assert.True(await structureReader.ReadAsync());
        Assert.Equal(Assert.Single(foreignKey.GetProperty("localColumns").EnumerateArray()).GetString(), structureReader.GetString(0));
        Assert.Equal("public", structureReader.GetString(1));
        Assert.Equal(foreignKey.GetProperty("referencedTable").GetString(), structureReader.GetString(2));
        Assert.Equal(Assert.Single(foreignKey.GetProperty("referencedColumns").EnumerateArray()).GetString(), structureReader.GetString(3));
        Assert.Equal(foreignKey.GetProperty("onDelete").GetString(), structureReader.GetString(4).Replace(' ', '-').ToLowerInvariant());
        Assert.Equal(foreignKey.GetProperty("onUpdate").GetString(), structureReader.GetString(5).Replace(' ', '-').ToLowerInvariant());
        Assert.False(await structureReader.ReadAsync());
    }

    // pg_get_constraintdef adds CHECK, identifier quotes, whitespace, and wrapper parentheses.
    private static string NormalizeCheckExpression(string expression)
    {
        var normalized = new string(expression.Where(character => !char.IsWhiteSpace(character) && character is not '"' and not '(' and not ')').Select(char.ToUpperInvariant).ToArray());
        return normalized.StartsWith("CHECK", StringComparison.Ordinal) ? normalized[5..] : normalized;
    }
}
