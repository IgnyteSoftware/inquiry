using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Inquiry.Generators;
using Inquiry.Generators.Models;
using Json.Schema;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private static readonly Lazy<JsonSchema> s_manifestSchema = new(() =>
        JsonSchema.FromText(File.ReadAllText(RepositoryFile("docs", "schema-manifest-v1.schema.json"))));
    private const string ManifestSource = """
        using Inquiry.Entities; namespace Demo;
        [InquiryTable("Parent")] public sealed class Parent { [InquiryKey] public int Id {get;set;} }
        [InquiryTable("Child"), InquiryIndex(nameof(Code), Name="IX_Code"), InquiryCheck("Code <> ''", Name="CK_Code")]
        public sealed class Child
        {
            [InquiryKey] public int Id {get;set;}
            [InquiryForeignKey("ParentId", "Parent", "Id", ConstraintName="FK_Parent", OnDelete=InquiryReferentialAction.Cascade)] public int ParentId {get;set;}
            [InquiryColumn(Length=32, DefaultExpression="'x'")] public string Code {get;set;} = "";
            [InquiryColumn(Computed="Id + ParentId")] public int Total {get;set;}
        }
        """;

    [Fact]
    public void CheckedInManifestSchemaFullySpecifiesV1ObjectContractAndAdditivePolicy()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryFile("docs", "schema-manifest-v1.schema.json")));
        var root = document.RootElement;
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
        Assert.True(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("ignore", root.GetProperty("description").GetString()!, StringComparison.OrdinalIgnoreCase);

        var definitions = root.GetProperty("$defs");
        foreach (var name in new[] { "table", "column", "index", "check", "foreignKey", "providerArtifact" })
        {
            var definition = definitions.GetProperty(name);
            Assert.True(definition.GetProperty("additionalProperties").GetBoolean());
            Assert.NotEmpty(definition.GetProperty("required").EnumerateArray());
            Assert.NotEmpty(definition.GetProperty("properties").EnumerateObject());
        }

        AssertJsonEnum(definitions.GetProperty("column").GetProperty("properties").GetProperty("typeInference"), "explicit", "database");
        AssertJsonEnum(definitions.GetProperty("column").GetProperty("properties").GetProperty("generation"), "none", "identity", "rowversion", "computed", "default");
        AssertJsonEnum(definitions.GetProperty("column").GetProperty("properties").GetProperty("concurrency"), "none", "application", "database");
        AssertJsonEnum(definitions.GetProperty("referentialAction"), "no-action", "restrict", "cascade", "set-null", "set-default");
    }

    [Fact]
    public void CheckedInManifestSampleCoversEveryV1ObjectAndSemanticVariant()
    {
        var sampleText = File.ReadAllText(RepositoryFile("docs", "schema-manifest-v1.sample.json"));
        AssertValidAgainstCheckedInSchema(sampleText);
        using var document = JsonDocument.Parse(sampleText);
        AssertManifestShape(document.RootElement);
        var child = document.RootElement.GetProperty("tables").EnumerateArray().Single(table => table.GetProperty("name").GetString() == "Child");
        var columns = child.GetProperty("columns").EnumerateArray().ToArray();
        Assert.Contains(columns, column => column.GetProperty("generation").GetString() == "identity");
        Assert.Contains(columns, column => column.GetProperty("generation").GetString() == "default");
        Assert.Contains(columns, column => column.GetProperty("generation").GetString() == "computed" && column.GetProperty("typeInference").GetString() == "database");
        Assert.Contains(columns, column => column.GetProperty("generation").GetString() == "rowversion" && column.GetProperty("concurrency").GetString() == "database");
        Assert.Single(child.GetProperty("indexes").EnumerateArray());
        Assert.Single(child.GetProperty("checks").EnumerateArray());
        Assert.Single(child.GetProperty("foreignKeys").EnumerateArray());
        Assert.Single(document.RootElement.GetProperty("providerArtifacts").EnumerateArray());
    }

    [Theory]
    [InlineData("Sqlite", "sqlite")]
    [InlineData("SqlServer", "sqlserver")]
    [InlineData("PostgreSql", "postgresql")]
    [InlineData("MySql", "mysql")]
    [InlineData("MariaDb", "mariadb")]
    [InlineData("Oracle", "oracle")]
    public void SchemaManifestIsCanonicalAndHasExactHashAndTransport(string dialect, string providerId)
    {
        var result = RunGenerator(ManifestSource, dialect: dialect);
        AssertNoErrors(result);
        var source = Assert.Single(result.RunResult.GeneratedTrees, tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        var json = ExtractVerbatimConstant(source, "SchemaManifestJson");
        AssertValidAgainstCheckedInSchema(json);
        using var document = JsonDocument.Parse(json);
        AssertManifestShape(document.RootElement);
        Assert.Equal(1, document.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(providerId, document.RootElement.GetProperty("providerId").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("tables").GetArrayLength());
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        Assert.Contains($"SchemaManifestSha256 = \"{expectedHash}\"", source);
        Assert.Contains("Inquiry.SchemaManifest.Chunk.0000", source);
        Assert.Contains("\"onDelete\":\"cascade\"", json);
        Assert.Contains("\"defaultExpression\":\"'x'\"", json);
        Assert.Contains("\"computedExpression\":\"Id + ParentId\"", json);
    }

    private static void AssertValidAgainstCheckedInSchema(string instanceJson)
    {
        using var instance = JsonDocument.Parse(instanceJson);
        var result = s_manifestSchema.Value.Evaluate(instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(result.IsValid, result.ToString());
    }

    [Fact]
    public void SqlServerManifestUsesFinalRowversionAndRenderedComputedExpression()
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} [InquiryConcurrencyToken(DatabaseGenerated=true)] public byte[] Version {get;set;} = []; [InquiryColumn(Computed=\"Id || 1\")] public int C {get;set;} }";
        var result = RunGenerator(source, dialect: "SqlServer");
        var json = ExtractVerbatimConstant(Assert.Single(result.RunResult.GeneratedTrees, tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString(), "SchemaManifestJson");
        Assert.Contains("\"storeType\":\"ROWVERSION\"", json);
        Assert.Contains("\"generation\":\"rowversion\"", json);
        Assert.Contains("\"computedExpression\":\"Id + 1\"", json);
        Assert.Contains("\"storeType\":null,\"typeInference\":\"database\"", json);
    }

    [Fact]
    public void ManifestExcludesOptedOutAndSuppressedFactsButRetainsExternalForeignKey()
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"Ignored\",GenerateDdl=false)] public sealed class Ignored { [InquiryKey] public int Id {get;set;} } [InquiryTable(\"Local\")] public sealed class Local { [InquiryKey] public int Id {get;set;} [InquiryForeignKey(\"ExternalId\",\"External\",\"Id\",ConstraintName=\"FK_Ext\")] public int ExternalId {get;set;} }";
        var result = RunGenerator(source, dialect: "Sqlite");
        var json = ExtractVerbatimConstant(Assert.Single(result.RunResult.GeneratedTrees, tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString(), "SchemaManifestJson");
        Assert.DoesNotContain("Ignored", json);
        Assert.Contains("\"referencedTable\":\"External\"", json);
    }

    [Fact]
    public void ManifestIsStableUnderEntityDiscoveryReorder()
    {
        const string prelude = "using Inquiry.Entities; namespace Demo;";
        const string a = "[InquiryTable(\"A\")] public sealed class A { [InquiryKey] public int Id {get;set;} }";
        const string b = "[InquiryTable(\"B\")] public sealed class B { [InquiryKey] public int Id {get;set;} }";
        var left = ExtractVerbatimConstant(Assert.Single(RunGenerator(prelude + a + b).RunResult.GeneratedTrees, tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString(), "SchemaManifestJson");
        var right = ExtractVerbatimConstant(Assert.Single(RunGenerator(prelude + b + a).RunResult.GeneratedTrees, tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString(), "SchemaManifestJson");
        Assert.Equal(left, right);
    }

    [Theory]
    [InlineData("Sqlite", "a", "B")]
    [InlineData("SqlServer", "a", "B")]
    [InlineData("PostgreSql", "B", "a")]
    [InlineData("MySql", "a", "B")]
    [InlineData("MariaDb", "a", "B")]
    [InlineData("Oracle", "a", "B")]
    public void ManifestTableOrderingUsesProviderPhysicalIdentifierPolicy(string dialect, string first, string second)
    {
        const string prelude = "using Inquiry.Entities; namespace Demo;";
        const string a = "[InquiryTable(\"a\")] public sealed class Lower { [InquiryKey] public int Id {get;set;} }";
        const string b = "[InquiryTable(\"B\")] public sealed class Upper { [InquiryKey] public int Id {get;set;} }";
        var left = ManifestTableNames(RunGenerator(prelude + a + b, dialect: dialect));
        var right = ManifestTableNames(RunGenerator(prelude + b + a, dialect: dialect));
        Assert.Equal(new[] { first, second }, left);
        Assert.Equal(left, right);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void ManifestCaseOnlyNamesHaveStableRawOrdinalTieBreak(string dialect)
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"a\")] public sealed class Lower { [InquiryKey] public int Id {get;set;} } [InquiryTable(\"A\")] public sealed class Upper { [InquiryKey] public int Id {get;set;} }";
        Assert.Equal(new[] { "A", "a" }, ManifestTableNames(RunGenerator(source, dialect: dialect)));
    }

    [Fact]
    public void OracleRequiredQuotedNamesPreserveExactOrdinalOrdering()
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"a name\")] public sealed class Lower { [InquiryKey] public int Id {get;set;} } [InquiryTable(\"B name\")] public sealed class Upper { [InquiryKey] public int Id {get;set;} }";
        Assert.Equal(new[] { "B name", "a name" }, ManifestTableNames(RunGenerator(source, dialect: "Oracle")));
    }

    [Fact]
    public void OracleDdlPlacesDefaultBeforeNotNullConstraint()
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} [InquiryColumn(\"score\", DefaultExpression=\"7\")] public int Score {get;set;} }";
        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var generated = Assert.Single(result.RunResult.GeneratedTrees,
            tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        var ddl = ExtractVerbatimConstant(generated, "Ddl");
        Assert.Contains("score NUMBER(10) DEFAULT 7 NOT NULL", ddl);
        Assert.DoesNotContain("score NUMBER(10) NOT NULL DEFAULT 7", ddl);
    }

    [Fact]
    public void OracleDdlPlacesKeyDefaultBeforeAllInlineConstraints()
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey(DefaultExpression=\"7\")] public int Id {get;set;} }";
        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var generated = Assert.Single(result.RunResult.GeneratedTrees,
            tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        var ddl = ExtractVerbatimConstant(generated, "Ddl");
        Assert.Contains("Id NUMBER(10) DEFAULT 7 PRIMARY KEY NOT NULL", ddl);
        Assert.DoesNotContain("PRIMARY KEY DEFAULT", ddl);
    }

    private static string[] ManifestTableNames(GeneratorTestResult result)
    {
        var source = Assert.Single(result.RunResult.GeneratedTrees,
            tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        using var document = JsonDocument.Parse(ExtractVerbatimConstant(source, "SchemaManifestJson"));
        return document.RootElement.GetProperty("tables").EnumerateArray()
            .Select(table => table.GetProperty("name").GetString()!).ToArray();
    }

    [Fact]
    public void ManifestWriterChunksAtExactUtf8BoundariesAndEscapesUnpairedSurrogates()
    {
        var json = new string('a', SchemaManifestWriter.ChunkByteLimit - 1) + "😀" + "b";
        var chunks = SchemaManifestWriter.Chunk(json);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(json, string.Concat(chunks));
        Assert.All(chunks, chunk => Assert.True(Encoding.UTF8.GetByteCount(chunk) <= SchemaManifestWriter.ChunkByteLimit));
        var manifest = new SchemaManifestData("x", Array.Empty<SchemaManifestTableData>(),
            new[] { new SchemaManifestArtifactData("", "bad\ud800", "kind", "sig") });
        Assert.Contains("bad\\ud800", SchemaManifestWriter.Write(manifest));
        Assert.False(SchemaManifestWriter.TryBuildTransport(new string('x', SchemaManifestWriter.ChunkByteLimit + 1), 1, out var overflow, out var required));
        Assert.Equal(2, required);
        Assert.Empty(overflow);
        Assert.Equal("INQ073", Inquiry.Generators.Diagnostics.InquiryDiagnosticDescriptors.SchemaManifestTooLarge.Id);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void GeneratedKeyRejectsOwnedPhysicalFacetsAcrossProviders(string dialect)
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey(IsGenerated=true, SqlType=\"INTEGER\", DefaultExpression=\"1\", UseDatabaseDefault=true)] public int Id {get;set;} }";
        var result = RunGenerator(source, dialect: dialect);
        Assert.Equal(3, result.RunResult.Diagnostics.Count(diagnostic => diagnostic.Id == "INQ074"));
        Assert.DoesNotContain(result.RunResult.GeneratedTrees,
            tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void ReservedManifestAssemblyMetadataSuppressesGeneratedTransport()
    {
        const string source = "using System.Reflection; using Inquiry.Entities; [assembly: AssemblyMetadata(\"Inquiry.SchemaManifest.Sha256\", \"user\")] namespace Demo; [InquiryTable(\"T\")] public sealed class T { [InquiryKey] public int Id {get;set;} }";
        var result = RunGenerator(source, dialect: "Sqlite");
        Assert.Contains(result.RunResult.Diagnostics, diagnostic => diagnostic.Id == "INQ075");
        var generated = Assert.Single(result.RunResult.GeneratedTrees,
            tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.DoesNotContain("[assembly:", generated);
        Assert.Contains("SchemaManifestJson", generated);
    }

    [Fact]
    public void AssemblyMetadataReassemblesExactManifestWithoutLoadingAssembly()
    {
        var result = RunGenerator(ManifestSource, dialect: "Sqlite");
        using var pe = new MemoryStream();
        var emit = result.Compilation.Emit(pe);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        pe.Position = 0;
        using var reader = new PEReader(pe);
        var metadata = reader.GetMetadataReader();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var member = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (member.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (metadata.GetString(type.Name) != "AssemblyMetadataAttribute") continue;
            var blob = metadata.GetBlobReader(attribute.Value);
            Assert.Equal(1, blob.ReadUInt16());
            values[blob.ReadSerializedString()!] = blob.ReadSerializedString()!;
        }
        var count = int.Parse(values["Inquiry.SchemaManifest.ChunkCount"], System.Globalization.CultureInfo.InvariantCulture);
        var json = string.Concat(Enumerable.Range(0, count).Select(i => values[$"Inquiry.SchemaManifest.Chunk.{i:D4}"]));
        Assert.Equal(values["Inquiry.SchemaManifest.Sha256"], Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant());
        Assert.Equal("1", values["Inquiry.SchemaManifest.FormatVersion"]);
    }

    [Fact]
    public void ArtifactOnlyAssemblyEmitsEmptyTableManifest()
    {
        const string source = """
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks; using Inquiry; using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("Items", GenerateDdl=false)] public sealed class Item { [InquiryKey] public int Id {get;set;} }
            public partial class Store : InquiryStore<Item> { [InquirySelectAllByPredicate, InquiryWhere("Id", Compare.In)] public partial Task<IReadOnlyList<Item>> ByIds(IReadOnlyList<int> ids, CancellationToken cancellationToken=default); }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var generated = Assert.Single(result.RunResult.GeneratedTrees, tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        var json = ExtractVerbatimConstant(generated, "SchemaManifestJson");
        Assert.Contains("\"tables\":[]", json);
        Assert.Contains("\"providerArtifacts\":[{", json);
        Assert.Contains("\"kind\":\"tvp\"", json);
    }

    [Fact]
    public void DeferredCyclicForeignKeysRemainSemanticManifestFacts()
    {
        const string source = "using Inquiry.Entities; namespace Demo; [InquiryTable(\"A\")] public sealed class A { [InquiryKey] public int Id {get;set;} [InquiryForeignKey(\"BId\",\"B\",\"Id\",ConstraintName=\"FK_A_B\")] public int BId {get;set;} } [InquiryTable(\"B\")] public sealed class B { [InquiryKey] public int Id {get;set;} [InquiryForeignKey(\"AId\",\"A\",\"Id\",ConstraintName=\"FK_B_A\")] public int AId {get;set;} }";
        var result = RunGenerator(source, dialect: "SqlServer");
        var generated = Assert.Single(result.RunResult.GeneratedTrees, tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        var json = ExtractVerbatimConstant(generated, "SchemaManifestJson");
        Assert.Contains("\"name\":\"FK_A_B\"", json);
        Assert.Contains("\"name\":\"FK_B_A\"", json);
        Assert.Contains("ALTER TABLE", ExtractSchemaDdl(result));
    }

    private static string ExtractVerbatimConstant(string source, string name)
    {
        var marker = name + " = @\"";
        var start = source.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = source.IndexOf("\";", start, StringComparison.Ordinal);
        return source.Substring(start, end - start).Replace("\"\"", "\"");
    }

    private static void AssertManifestShape(JsonElement manifest)
    {
        Assert.Equal(new[] { "formatVersion", "providerId", "tables", "providerArtifacts" }, manifest.EnumerateObject().Select(property => property.Name));
        Assert.Matches("^[a-z][a-z0-9.-]{0,63}$", manifest.GetProperty("providerId").GetString()!);
        foreach (var table in manifest.GetProperty("tables").EnumerateArray())
        {
            Assert.Equal(new[] { "schema", "name", "columns", "primaryKey", "indexes", "checks", "foreignKeys" }, table.EnumerateObject().Select(property => property.Name));
            Assert.NotEmpty(table.GetProperty("name").GetString()!);
            foreach (var column in table.GetProperty("columns").EnumerateArray())
                Assert.Equal(new[] { "name", "storeType", "typeInference", "typeClass", "nullable", "primaryKeyOrdinal", "generation", "defaultExpression", "computedExpression", "concurrency" }, column.EnumerateObject().Select(property => property.Name));
            foreach (var index in table.GetProperty("indexes").EnumerateArray())
                Assert.Equal(new[] { "name", "unique", "keyColumns", "includeColumns" }, index.EnumerateObject().Select(property => property.Name));
            foreach (var check in table.GetProperty("checks").EnumerateArray())
                Assert.Equal(new[] { "name", "expression" }, check.EnumerateObject().Select(property => property.Name));
            foreach (var foreignKey in table.GetProperty("foreignKeys").EnumerateArray())
                Assert.Equal(new[] { "name", "localColumns", "referencedSchema", "referencedTable", "referencedColumns", "onDelete", "onUpdate" }, foreignKey.EnumerateObject().Select(property => property.Name));
        }
        foreach (var artifact in manifest.GetProperty("providerArtifacts").EnumerateArray())
            Assert.Equal(new[] { "schema", "name", "kind", "signature" }, artifact.EnumerateObject().Select(property => property.Name));
    }

    private static void AssertJsonEnum(JsonElement schema, params string[] expected)
        => Assert.Equal(expected, schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));

    private static string RepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Inquiry.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }
}
