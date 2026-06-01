using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// W7 Phase A: <c>InquiryGeneratedSchema.Ddl</c> emission — CREATE TABLE per entity with inferred
/// types, identity keys, NOT NULL inference, DEFAULT, foreign keys, and topological (referenced-first)
/// ordering, across dialects.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
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

        Assert.Contains("IF OBJECT_ID(N'Widget', N'U') IS NULL", ddl);
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

            [InquiryColumn("Name", IsIndexed = true)]
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
