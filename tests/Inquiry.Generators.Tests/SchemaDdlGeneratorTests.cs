using System;
using Microsoft.CodeAnalysis;

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
