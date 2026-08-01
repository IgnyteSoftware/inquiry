using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Inquiry.Generators;
using Inquiry.Generators.Abstractions;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>InquiryGeneratedSchema.Ddl</c> emission — CREATE TABLE per entity with inferred
/// types, identity keys, NOT NULL inference, DEFAULT, foreign keys, and topological (referenced-first)
/// ordering, across dialects.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string CyclicSchemaSource = """
        using Inquiry.Entities;
        namespace Demo;

        [InquiryTable("CycleRoot")]
        public sealed class CycleRoot
        {
            [InquiryKey] public long Id { get; set; }
        }

        [InquiryTable("CycleA")]
        public sealed class CycleA
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryForeignKey("BId", "CycleB", "Id")] public long? BId { get; set; }
            [InquiryColumn(IsIndexed = true)] public int Sort { get; set; }
        }

        [InquiryTable("CycleB")]
        public sealed class CycleB
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryForeignKey("AId", "CycleA", "Id")] public long? AId { get; set; }
            [InquiryForeignKey("RootId", "CycleRoot", "Id")] public long RootId { get; set; }
        }
        """;

    private const string AuthorBookSource = """
        using Inquiry.Entities;

        namespace Demo;

        [InquiryTable("Book")]
        public sealed class Book
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Title")]
            public string Title { get; set; } = string.Empty;

            [InquiryColumn("Pages")]
            public int? Pages { get; set; }

            [InquiryForeignKey("AuthorId", "Author", "Id")]
            public long AuthorId { get; set; }
        }

        [InquiryTable("Author")]
        public sealed class Author
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;
        }
        """;

    private static string ExtractSchemaDdl(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();
        const string marker = "public const string Ddl = @\"";
        var start = text.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = text.LastIndexOf("\";", StringComparison.Ordinal);
        // Un-double the verbatim-string quotes to recover the raw DDL.
        return text.Substring(start, end - start).Replace("\"\"", "\"");
    }

    [Theory]
    [InlineData("SqlServer", "[", "]")]
    [InlineData("PostgreSql", "\"", "\"")]
    [InlineData("MySql", "`", "`")]
    [InlineData("MariaDb", "`", "`")]
    [InlineData("Oracle", "", "")]
    public void MultiTableCycleDefersOnlySccEdges(string dialect, string open, string close)
    {
        var result = RunGenerator(CyclicSchemaSource, dialect: dialect);
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Equal(2, Regex.Matches(ddl, "ALTER TABLE", RegexOptions.CultureInvariant).Count);
        Assert.Contains($"FOREIGN KEY ({open}RootId{close}) REFERENCES {open}CycleRoot{close}({open}Id{close})", ddl);

        var finalCreate = ddl.LastIndexOf("CREATE TABLE", StringComparison.Ordinal);
        var firstAlter = ddl.IndexOf("ALTER TABLE", StringComparison.Ordinal);
        var firstIndex = ddl.IndexOf("CREATE INDEX", StringComparison.Ordinal);
        Assert.True(finalCreate < firstAlter);
        Assert.True(firstAlter < firstIndex);

        var cycleABody = TableCreateBody(ddl, open + "CycleA" + close);
        var cycleBBody = TableCreateBody(ddl, open + "CycleB" + close);
        Assert.DoesNotContain("FOREIGN KEY (" + open + "BId" + close + ")", cycleABody);
        Assert.DoesNotContain("FOREIGN KEY (" + open + "AId" + close + ")", cycleBBody);
        Assert.Contains("FOREIGN KEY (" + open + "RootId" + close + ")", cycleBBody);
    }

    [Fact]
    public void SqliteKeepsMultiTableCycleInline()
    {
        var result = RunGenerator(CyclicSchemaSource);
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.DoesNotContain("ALTER TABLE", ddl);
        Assert.Contains("FOREIGN KEY (\"BId\") REFERENCES \"CycleB\"(\"Id\")", ddl);
        Assert.Contains("FOREIGN KEY (\"AId\") REFERENCES \"CycleA\"(\"Id\")", ddl);
        Assert.Contains("FOREIGN KEY (\"RootId\") REFERENCES \"CycleRoot\"(\"Id\")", ddl);
    }

    [Fact]
    public void GenerateForeignKeysFalseDoesNotCreateCycleOrAlter()
    {
        var source = CyclicSchemaSource.Replace("[InquiryTable(\"CycleA\")]", "[InquiryTable(\"CycleA\", GenerateForeignKeys = false)]");
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.DoesNotContain("ALTER TABLE", ddl);
        Assert.DoesNotContain("FOREIGN KEY ([BId])", ddl);
        Assert.Contains("FOREIGN KEY ([AId]) REFERENCES [CycleA]([Id])", ddl);
    }

    [Fact]
    public void DeferredConstraintNamesAreStableHashSuffixedAndUtf8Bounded()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("非常に長いテーブル名前非常に長いテーブル名前A")]
            public sealed class A
            {
                [InquiryKey] public long Id { get; set; }
                [InquiryForeignKey("非常に長い外部キーカラム名前A", "非常に長いテーブル名前非常に長いテーブル名前B", "Id")] public long? BId { get; set; }
            }
            [InquiryTable("非常に長いテーブル名前非常に長いテーブル名前B")]
            public sealed class B
            {
                [InquiryKey] public long Id { get; set; }
                [InquiryForeignKey("非常に長い外部キーカラム名前B", "非常に長いテーブル名前非常に長いテーブル名前A", "Id")] public long? AId { get; set; }
            }
            """;

        var first = ExtractSchemaDdl(RunGenerator(source, dialect: "SqlServer"));
        var second = ExtractSchemaDdl(RunGenerator(source, dialect: "SqlServer"));
        var names = Regex.Matches(first, @"ADD CONSTRAINT \[([^]]+)\]")
            .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(2, names.Length);
        Assert.All(names, name =>
        {
            Assert.True(Encoding.UTF8.GetByteCount(name) <= 63);
            Assert.Matches("_[0-9a-f]{16}$", name);
        });
        Assert.Equal(names, Regex.Matches(second, @"ADD CONSTRAINT \[([^]]+)\]").Cast<Match>().Select(m => m.Groups[1].Value));
    }

    [Fact]
    public void CollisionExtensionTerminalNameNeverExceedsUtf8Limit()
    {
        var extended = SchemaEmitter.BuildForeignKeyName("非常に長いテーブル名前", "非常に長いカラム名前", "canonical-a", 9);
        var terminal = SchemaEmitter.BuildForeignKeyName("非常に長いテーブル名前", "非常に長いカラム名前", "canonical-a", 31);
        Assert.Matches("^_[0-9a-f]{62}$", terminal);
        Assert.Equal(63, Encoding.UTF8.GetByteCount(terminal));
        Assert.True(Encoding.UTF8.GetByteCount(extended) <= 63);
        Assert.Throws<ArgumentOutOfRangeException>(() => SchemaEmitter.BuildForeignKeyName("T", "C", "canonical", 32));
    }

    [Fact]
    public void IdenticalDuplicateMappingsParticipateInCyclicForeignKeysOnce()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("CycleA")]
            public sealed class A1 { [InquiryKey] public long Id { get; set; } [InquiryForeignKey("BId", "CycleB", "Id")] public long? BId { get; set; } }
            [InquiryTable("CycleA")]
            public sealed class A2 { [InquiryKey] public long Id { get; set; } [InquiryForeignKey("BId", "CycleB", "Id")] public long? BId { get; set; } }
            [InquiryTable("CycleB")]
            public sealed class B { [InquiryKey] public long Id { get; set; } [InquiryForeignKey("AId", "CycleA", "Id")] public long? AId { get; set; } }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ070");
        var ddl = ExtractSchemaDdl(result);
        Assert.Equal(2, ddl.Split("CREATE TABLE [CycleA]").Length);
        Assert.Equal(2, ddl.Split("FOREIGN KEY ([BId])").Length);
        Assert.Equal(2, ddl.Split("FOREIGN KEY ([AId])").Length);
    }

    [Fact]
    public void PostgreSqlCaseDistinctTablesAndSchemasRemainDistinct()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("Node", Schema = "Upper")]
            public sealed class UpperNode { [InquiryKey] public long Id { get; set; } [InquiryForeignKey("OtherId", "node", "Id", ReferencedSchema = "upper")] public long? OtherId { get; set; } }
            [InquiryTable("node", Schema = "upper")]
            public sealed class LowerNode { [InquiryKey] public long Id { get; set; } [InquiryForeignKey("OtherId", "Node", "Id", ReferencedSchema = "Upper")] public long? OtherId { get; set; } }
            """;
        var result = RunGenerator(source, dialect: "PostgreSql");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"Upper\".\"Node\"", ddl);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"upper\".\"node\"", ddl);
        Assert.Equal(2, Regex.Matches(ddl, "ALTER TABLE").Count);
    }

    [Fact]
    public void ReorderedDeclarationsProduceIdenticalDeferredSection()
    {
        var marker = "[InquiryTable(\"CycleA\")]";
        var root = CyclicSchemaSource.Substring(0, CyclicSchemaSource.IndexOf(marker, StringComparison.Ordinal));
        var aStart = CyclicSchemaSource.IndexOf(marker, StringComparison.Ordinal);
        var bStart = CyclicSchemaSource.IndexOf("[InquiryTable(\"CycleB\")]", StringComparison.Ordinal);
        var a = CyclicSchemaSource.Substring(aStart, bStart - aStart);
        var b = CyclicSchemaSource.Substring(bStart);
        var first = ExtractSchemaDdl(RunGenerator(root + a + b, dialect: "SqlServer"));
        var second = ExtractSchemaDdl(RunGenerator(root + b + a, dialect: "SqlServer"));
        Assert.Equal(first.Substring(first.IndexOf("ALTER TABLE", StringComparison.Ordinal)), second.Substring(second.IndexOf("ALTER TABLE", StringComparison.Ordinal)));
    }

    [Fact]
    public void DuplicatePhysicalTablesWithoutCyclicForeignKeysRemainCompatible()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("Same")] public sealed class A { [InquiryKey] public long Id { get; set; } }
            [InquiryTable("Same")] public sealed class B { [InquiryKey] public long Id { get; set; } }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ070");
        Assert.DoesNotContain("ALTER TABLE", ExtractSchemaDdl(result));
    }

    [Fact]
    public void SafeFallbackReportsEachCycleEdgeAndSuppressesConstraints()
    {
        var result = RunGenerator(CyclicSchemaSource, dialect: "Fallback", includeFallbackGenerator: true);
        var diagnostics = result.RunResult.Diagnostics.Where(d => d.Id == "INQ069").ToArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.NotEqual(Microsoft.CodeAnalysis.Location.None, diagnostic.Location));
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("Table 'CycleA' foreign-key column 'BId'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("Table 'CycleB' foreign-key column 'AId'", StringComparison.Ordinal));
        var ddl = ExtractSchemaDdl(result);
        Assert.DoesNotContain("ALTER TABLE", ddl);
        Assert.DoesNotContain("FOREIGN KEY (\"BId\")", ddl);
        Assert.DoesNotContain("FOREIGN KEY (\"AId\")", ddl);
        Assert.Contains("FOREIGN KEY (\"RootId\")", ddl);
    }

    private static string TableCreateBody(string ddl, string quotedTable)
    {
        var start = ddl.IndexOf(quotedTable, StringComparison.Ordinal);
        var end = ddl.IndexOf(';', start);
        return ddl.Substring(start, end - start);
    }

    [Fact]
    public void SqlServerDatabaseGeneratedByteArrayTokenUsesRowversionButOrdinaryBytesRemainVarbinaryMax()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Document")]
            public sealed class Document
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryConcurrencyToken(DatabaseGenerated = true)]
                public byte[] Version { get; set; } = System.Array.Empty<byte>();

                [InquiryColumn]
                public byte[] Payload { get; set; } = System.Array.Empty<byte>();
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("[Version] ROWVERSION NOT NULL", ddl);
        Assert.Contains("[Payload] VARBINARY(MAX) NOT NULL", ddl);
    }

    [Fact]
    public void SqliteSchemaEmitsTypesIdentityNullabilityAndForeignKeys()
    {
        var result = RunGenerator(AuthorBookSource);
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("CREATE TABLE IF NOT EXISTS \"Author\" (", ddl);
        Assert.Contains("\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT", ddl);
        Assert.Contains("\"Name\" TEXT NOT NULL", ddl);
        // Nullable int? → no NOT NULL; non-nullable FK long → NOT NULL.
        Assert.Contains("\"Pages\" INTEGER,", ddl);
        Assert.Contains("\"AuthorId\" INTEGER NOT NULL", ddl);
        Assert.Contains("FOREIGN KEY (\"AuthorId\") REFERENCES \"Author\"(\"Id\")", ddl);

        // Topological order: Author (referenced) is created before Book (referencing).
        Assert.True(
            ddl.IndexOf("\"Author\"", StringComparison.Ordinal) < ddl.IndexOf("\"Book\"", StringComparison.Ordinal),
            "Referenced table Author must be created before Book.");
    }

    [Fact]
    public void GenerateForeignKeysFalseOmitsConstraint()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Book", GenerateForeignKeys = false)]
            public sealed class Book
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey("AuthorId", "Author", "Id")]
                public long AuthorId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("\"AuthorId\" INTEGER NOT NULL", ddl);
        Assert.DoesNotContain("FOREIGN KEY", ddl);
    }

    [Fact]
    public void CompositeKeyColumnsCanAlsoBeForeignKeys()
    {
        // A bridge table's composite-key columns are also foreign keys: [InquiryKey] + [InquiryForeignKey]
        // on the same property. The generated DDL emits BOTH the composite PK and a FOREIGN KEY per column.
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Employee")]
            public sealed class Employee
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }
            }

            [InquiryTable("Territory")]
            public sealed class Territory
            {
                [InquiryKey("Code", Length = 8)]
                public string Code { get; set; } = string.Empty;
            }

            [InquiryTable("EmployeeTerritory")]
            public sealed class EmployeeTerritory
            {
                [InquiryKey("EmpId")]
                [InquiryForeignKey("EmpId", "Employee", "Id")]
                public long EmpId { get; set; }

                [InquiryKey("TerrCode", Length = 8)]
                [InquiryForeignKey("TerrCode", "Territory", "Code")]
                public string TerrCode { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("PRIMARY KEY (\"EmpId\", \"TerrCode\")", ddl);
        Assert.Contains("FOREIGN KEY (\"EmpId\") REFERENCES \"Employee\"(\"Id\")", ddl);
        Assert.Contains("FOREIGN KEY (\"TerrCode\") REFERENCES \"Territory\"(\"Code\")", ddl);
    }

    [Fact]
    public void SelfReferencingForeignKeyDoesNotDeadlockAndEmits()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Employee")]
            public sealed class Employee
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey("ManagerId", "Employee", "Id")]
                public long? ManagerId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("CREATE TABLE IF NOT EXISTS \"Employee\" (", ddl);
        Assert.Contains("FOREIGN KEY (\"ManagerId\") REFERENCES \"Employee\"(\"Id\")", ddl);
        // Nullable self-FK → nullable column.
        Assert.Contains("\"ManagerId\" INTEGER,", ddl);
    }

    [Fact]
    public void CompositeKeyEmitsTableLevelPrimaryKey()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("OrderLine")]
            public sealed class OrderLine
            {
                [InquiryKey("OrderId")]
                public long OrderId { get; set; }

                [InquiryKey("ProductId")]
                public long ProductId { get; set; }

                [InquiryColumn("Qty", DefaultExpression = "1")]
                public int Qty { get; set; }
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("PRIMARY KEY (\"OrderId\", \"ProductId\")", ddl);
        Assert.Contains("\"Qty\" INTEGER NOT NULL DEFAULT 1", ddl);
    }

    [Fact]
    public void SqlServerSchemaUsesIdentityBoundedStringAndObjectIdGuard()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Widget")]
            public sealed class Widget
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Code", Length = 16)]
                public string Code { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("IF OBJECT_ID(N'[Widget]', N'U') IS NULL", ddl);
        Assert.Contains("[Id] BIGINT IDENTITY(1,1) PRIMARY KEY", ddl);
        Assert.Contains("[Code] NVARCHAR(16) NOT NULL", ddl);
    }

    [Fact]
    public void GeneratedNonIntegerKeyReportsDiagnostic()
    {
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey(IsGenerated = true)]
                public Guid Id { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ030");
    }

    [Fact]
    public void UnboundedStringKeyOnSqlServerReportsDiagnostic()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey("Code")]
                public string Code { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ031");

        // SQLite keys on unbounded TEXT fine — no diagnostic there.
        var sqlite = RunGenerator(source);
        Assert.DoesNotContain(sqlite.RunResult.Diagnostics, d => d.Id == "INQ031");
    }

    private const string IndexedUserSource = """
        using Inquiry.Entities;

        namespace Demo;

        [InquiryTable("AppUser")]
        public sealed class AppUser
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Email", Length = 128, IsUnique = true)]
            public string Email { get; set; } = string.Empty;

            [InquiryColumn("Name", Length = 64, IsIndexed = true)]
            public string Name { get; set; } = string.Empty;
        }
        """;

    [Fact]
    public void SqliteSchemaEmitsUniqueAndPlainIndexesWithIfNotExists()
    {
        var result = RunGenerator(IndexedUserSource);
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"UX_AppUser_Email\" ON \"AppUser\" (\"Email\");", ddl);
        Assert.Contains("CREATE INDEX IF NOT EXISTS \"IX_AppUser_Name\" ON \"AppUser\" (\"Name\");", ddl);
        // Indexes follow the table.
        Assert.True(
            ddl.IndexOf("CREATE TABLE", StringComparison.Ordinal) < ddl.IndexOf("CREATE UNIQUE INDEX", StringComparison.Ordinal),
            "Indexes must be emitted after the table.");
    }

    [Fact]
    public void SqlServerSchemaEmitsIndexesWithoutIfNotExists()
    {
        var result = RunGenerator(IndexedUserSource, dialect: "SqlServer");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        // SQL Server CREATE INDEX has no IF NOT EXISTS guard.
        Assert.Contains("CREATE UNIQUE INDEX [UX_AppUser_Email] ON [AppUser] ([Email]);", ddl);
        Assert.Contains("CREATE INDEX [IX_AppUser_Name] ON [AppUser] ([Name]);", ddl);
        Assert.DoesNotContain("INDEX IF NOT EXISTS", ddl);
    }

    [Fact]
    public void PostgreSqlSchemaEmitsIndexesWithIfNotExists()
    {
        var result = RunGenerator(IndexedUserSource, dialect: "PostgreSql");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"UX_AppUser_Email\" ON \"AppUser\" (\"Email\");", ddl);
        Assert.Contains("CREATE INDEX IF NOT EXISTS \"IX_AppUser_Name\" ON \"AppUser\" (\"Name\");", ddl);
    }

    [Fact]
    public void ExplicitIndexNameOverridesDefault()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("AppUser")]
            public sealed class AppUser
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Email", Length = 128, IsUnique = true, IndexName = "UX_Users_Email_Lower")]
                public string Email { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"UX_Users_Email_Lower\" ON \"AppUser\" (\"Email\");", ddl);
        Assert.DoesNotContain("UX_AppUser_Email", ddl);
    }

    [Fact]
    public void IndexKeyThatIsNotAMappedColumnReportsINQ094()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("AppUser")]
            [InquiryIndex("Emial")]
            public sealed class AppUser
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Email", Length = 128)]
                public string Email { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ094" && d.Severity == DiagnosticSeverity.Error);
        // The unresolved key must not reach the DDL as a blank identifier.
        Assert.DoesNotContain("(\"\")", ExtractSchemaDdl(result));
    }

    [Fact]
    public void IndexIncludeThatIsNotAMappedColumnReportsINQ094()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("AppUser")]
            [InquiryIndex("Email", Include = new[] { "Nikname" })]
            public sealed class AppUser
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Email", Length = 128)]
                public string Email { get; set; } = string.Empty;

                [InquiryColumn("Nickname", Length = 64)]
                public string Nickname { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ094" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("INCLUDE ([])", ExtractSchemaDdl(result));
    }

    [Fact]
    public void IndexOverMappedPropertiesReportsNoINQ094()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("AppUser")]
            [InquiryIndex("Email", Include = new[] { "Nickname" })]
            public sealed class AppUser
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Email", Length = 128)]
                public string Email { get; set; } = string.Empty;

                [InquiryColumn("Nickname", Length = 64)]
                public string Nickname { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");

        AssertNoErrors(result);
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ094");
    }

    [Fact]
    public void OracleSchemaQuotesOnlyIdentifiersThatRequireIt()
    {
        // Oracle leaves identifiers unquoted (it folds them to uppercase; the fidelity check matches
        // case-insensitively). The one exception is an identifier that is not a valid *unquoted* Oracle
        // identifier — e.g. "Order Details", whose embedded space yields ORA-00903 "invalid table name"
        // if emitted bare. QuoteIdentifier is the single chokepoint for DDL and DML, so quoting it here
        // keeps the CREATE TABLE and every reference in lockstep.
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Order Details")]
            public sealed class OrderDetail
            {
                [InquiryKey("OrderId")]
                public long OrderId { get; set; }

                [InquiryKey("ProductId")]
                public long ProductId { get; set; }

                [InquiryColumn("Unit Price")]
                public decimal UnitPrice { get; set; }

                [InquiryColumn("Quantity")]
                public int Quantity { get; set; }
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        // The spaced table AND column names are double-quoted (per-char check); clean identifiers
        // (Quantity, composite PK) stay unquoted.
        Assert.Contains("CREATE TABLE \"Order Details\" (", ddl);
        Assert.Contains("\"Unit Price\" NUMBER", ddl);
        Assert.Contains("Quantity NUMBER(10)", ddl);
        Assert.Contains("PRIMARY KEY (OrderId, ProductId)", ddl);
        Assert.DoesNotContain("\"OrderId\"", ddl);
        Assert.DoesNotContain("\"Quantity\"", ddl);
    }

    [Fact]
    public void OracleSchemaSkipsIndexOnUnboundedStringButKeepsBoundedOne()
    {
        // A bounded-key dialect cannot index an unbounded string (Oracle CLOB → ORA-02327). The skip now
        // lives in the base BuildCreateIndexSql (gated by RequiresBoundedStringKeys), so Oracle inherits
        // the same behavior MySQL/SQL Server already had — the index on the unbounded column is skipped,
        // the bounded one is kept.
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Body", IsIndexed = true)]
                public string Body { get; set; } = string.Empty;

                [InquiryColumn("Code", Length = 16, IsIndexed = true)]
                public string Code { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("CREATE INDEX IX_Doc_Code ON Doc (Code)", ddl);
        Assert.DoesNotContain("IX_Doc_Body", ddl); // unbounded CLOB column: index skipped, not emitted
        // The skip is surfaced, not silent: INQ032 warns for the unbounded indexed column.
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ032");
    }

    [Fact]
    public void ForeignKeyStringColumnInheritsReferencedKeyLength()
    {
        // A string FK with no declared Length inherits the referenced PK's Length, so a bounded dialect
        // emits a valid bounded VARCHAR instead of an unindexable/unkeyable LOB. Resolved across entities
        // in SchemaEmitter (the referenced table is a different entity).
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Customer")]
            public sealed class Customer
            {
                [InquiryKey("Code", Length = 5)]
                public string Code { get; set; } = string.Empty;
            }

            [InquiryTable("Ord")]
            public sealed class Ord
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey("CustomerCode", "Customer", "Code")]
                public string CustomerCode { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        // CustomerCode declared no Length but inherits Customer.Code's 5 → NVARCHAR(5), not NVARCHAR(MAX).
        Assert.Contains("[CustomerCode] NVARCHAR(5)", ddl);
        Assert.DoesNotContain("NVARCHAR(MAX)", ddl);
    }

    [Fact]
    public void IndexedForeignKeyStringInheritsLengthAndKeepsIndexWithoutWarning()
    {
        // The three A2 changes intersect on a string FK that is also indexed with no Length: derivation
        // bounds it (NVARCHAR(5)), so its index is kept (not skipped) and it does NOT warn (INQ032).
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Customer")]
            public sealed class Customer
            {
                [InquiryKey("Code", Length = 5)]
                public string Code { get; set; } = string.Empty;
            }

            [InquiryTable("Ord")]
            public sealed class Ord
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey("CustomerCode", "Customer", "Code", IsIndexed = true)]
                public string CustomerCode { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ032");
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("[CustomerCode] NVARCHAR(5)", ddl);            // derived from Customer.Code
        Assert.Contains("CREATE INDEX [IX_Ord_CustomerCode] ON [Ord] ([CustomerCode])", ddl); // index kept
    }

    [Fact]
    public void SchemaQualifiedForeignKeyReferencesSchemaAndDerivesLength()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Customer", Schema = "crm")]
            public sealed class Customer
            {
                [InquiryKey("Code", Length = 5)]
                public string Code { get; set; } = string.Empty;
            }

            [InquiryTable("Ord", Schema = "sales")]
            public sealed class Ord
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryForeignKey("CustomerCode", "Customer", "Code", ReferencedSchema = "crm")]
                public string CustomerCode { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("[CustomerCode] NVARCHAR(5)", ddl);
        Assert.Contains("FOREIGN KEY ([CustomerCode]) REFERENCES [crm].[Customer]([Code])", ddl);
    }

    [Fact]
    public void IndexedUnboundedStringReportsInq032OnBoundedDialectOnly()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Title", IsIndexed = true)]
                public string Title { get; set; } = string.Empty;
            }
            """;

        // Bounded dialect: an indexed unbounded string warns (INQ032) because its index is skipped.
        var sqlServer = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(sqlServer.RunResult.Diagnostics, d => d.Id == "INQ032" && d.Severity == DiagnosticSeverity.Warning);

        // SQLite indexes unbounded TEXT fine → no warning.
        var sqlite = RunGenerator(source);
        Assert.DoesNotContain(sqlite.RunResult.Diagnostics, d => d.Id == "INQ032");

        // A bounded indexed string does not warn (the index is created).
        var bounded = RunGenerator(source.Replace("IsIndexed = true", "IsIndexed = true, Length = 64"), dialect: "SqlServer");
        Assert.DoesNotContain(bounded.RunResult.Diagnostics, d => d.Id == "INQ032");
    }

    [Fact]
    public void PostgreSqlSchemaUsesSerialAndQuotedIdentifiers()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Widget")]
            public sealed class Widget
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source, dialect: "PostgreSql");
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        Assert.Contains("CREATE TABLE IF NOT EXISTS \"Widget\" (", ddl);
        Assert.Contains("\"Id\" BIGSERIAL PRIMARY KEY", ddl);
        Assert.Contains("\"Name\" TEXT NOT NULL", ddl);
    }
}

internal sealed class FallbackInquiryGenerator : InquiryGeneratorBase
{
    protected override string Dialect => "Fallback";
    protected override SqlBuilder CreateSqlBuilder() => new FallbackSqlBuilder();
}

internal sealed class FallbackSqlBuilder : SqlBuilder
{
    public override string DialectName => "Fallback";
    public override string ProviderId => "fallback";
    public override string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    public override string BuildSelectByKeySql(SqlBuildContext context) => throw new NotSupportedException();
    public override string BuildInsertSql(SqlBuildContext context) => throw new NotSupportedException();
    public override string BuildInsertReturningSql(SqlBuildContext context) => throw new NotSupportedException();
    public override string BuildUpdateSql(SqlBuildContext context) => throw new NotSupportedException();
    public override string BuildUpdateReturningSql(SqlBuildContext context) => throw new NotSupportedException();
    public override string BuildDeleteByKeySql(SqlBuildContext context) => throw new NotSupportedException();
    public override string BuildUpsertSql(SqlBuildContext context) => throw new NotSupportedException();
    public override string BuildUpsertReturningSql(SqlBuildContext context) => throw new NotSupportedException();
    protected override string MapColumnType(IColumn column) => column.TypeClass == DbTypeClass.Int64 ? "INTEGER" : "TEXT";
    protected override string GeneratedKeyClause(IColumn column) => "INTEGER PRIMARY KEY";
}
