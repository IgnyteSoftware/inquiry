using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Server-computed columns: <c>[InquiryColumn(Computed = "expr")]</c> renders the dialect's
/// computed-column DDL, is excluded from generated INSERT/UPDATE, and is still selected/materialized.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ComputedSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Person")]
        public sealed class Person
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("FirstName", Length = 50)]
            public string FirstName { get; set; } = string.Empty;

            [InquiryColumn("LastName", Length = 50)]
            public string LastName { get; set; } = string.Empty;

            [InquiryColumn("FullName", Length = 101, Computed = "FirstName || ' ' || LastName")]
            public string FullName { get; set; } = string.Empty;
        }

        public partial class PersonStore : InquiryStore<Demo.Person>
        {
            [InquiryInsert]
            public partial Task<int> InsertAsync(Person person, CancellationToken cancellationToken = default);

            [InquiryUpdate]
            public partial Task<bool> UpdateAsync(Person person, CancellationToken cancellationToken = default);

            [InquirySelectAll]
            public partial Task<IReadOnlyList<Person>> AllAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void ComputedColumnExcludedFromInsertAndUpdateButSelected()
    {
        var result = RunGenerator(ComputedSource);
        AssertNoErrors(result);

        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("PersonStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        // INSERT and UPDATE omit FullName; SELECT includes it.
        Assert.Contains("_sqlInsert = \"INSERT INTO \\\"Person\\\" (\\\"Id\\\", \\\"FirstName\\\", \\\"LastName\\\") VALUES (@Id, @FirstName, @LastName)\";", text);
        Assert.Contains("_sqlUpdate = \"UPDATE \\\"Person\\\" SET \\\"FirstName\\\" = @FirstName, \\\"LastName\\\" = @LastName WHERE \\\"Id\\\" = @Id\";", text);
        Assert.Contains("_sqlSelectAll = \"SELECT \\\"Id\\\", \\\"FirstName\\\", \\\"LastName\\\", \\\"FullName\\\" FROM \\\"Person\\\"\";", text);
        // FullName is not bound anywhere.
        Assert.DoesNotContain("@FullName", text);
    }

    [Fact]
    public void ComputedColumnDdlExpressionFormDialectsUseAsExpr()
    {
        // SQLite, SQL Server, and Oracle all use the base expression form `AS (<expr>)`.
        foreach (var dialect in new[] { "Sqlite", "SqlServer", "Oracle" })
        {
            var result = RunGenerator(ComputedSource, dialect: dialect);
            AssertNoErrors(result);
            var ddl = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
            // The standard expression form, with no type / NOT NULL on the computed column.
            Assert.Contains("AS (FirstName || ' ' || LastName)", ddl);
            Assert.DoesNotContain("GENERATED ALWAYS", ddl);
        }
    }

    [Fact]
    public void ComputedColumnDdlPostgreSqlAndMySqlUseStoredGeneratedForm()
    {
        var pg = RunGenerator(ComputedSource, dialect: "PostgreSql");
        AssertNoErrors(pg);
        var pgDdl = Assert.Single(pg.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("GENERATED ALWAYS AS (FirstName || ' ' || LastName) STORED", pgDdl);

        var mysql = RunGenerator(ComputedSource, dialect: "MySql");
        AssertNoErrors(mysql);
        var mysqlDdl = Assert.Single(mysql.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("GENERATED ALWAYS AS (FirstName || ' ' || LastName) STORED", mysqlDdl);

        var mariadb = RunGenerator(ComputedSource, dialect: "MariaDb");
        AssertNoErrors(mariadb);
        var mariadbDdl = Assert.Single(mariadb.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("GENERATED ALWAYS AS (FirstName || ' ' || LastName) STORED", mariadbDdl);
    }

    [Fact]
    public void ComputedKeyColumnReportsINQ057()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("X")]
            public sealed class X
            {
                [InquiryKey("Id", Computed = "1")]
                public long Id { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ057");
    }
}
