using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// E2 Oracle provider emission tests: exact const SQL for the Oracle dialect — unquoted
/// (uppercase-folding) identifiers, <c>:name</c> bind parameters, <c>MERGE … USING (… FROM dual)</c>
/// upsert, <c>VALUES (DEFAULT)</c> instead of <c>DEFAULT VALUES</c>, and <c>OFFSET … FETCH</c>
/// pagination. Returning DML is unsupported in v1 (Oracle <c>RETURNING … INTO</c> binds OUT
/// parameters rather than producing a result set the reader pipeline can consume).
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void OracleDialectEmitsUnquotedIdentifiersColonParametersAndMergeUpsert()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrganization")]
            public sealed class Organization
            {
                [InquiryKey]
                public Guid Key { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class OrganizationStore : InquiryStore<Organization>
            {
                [InquirySelectAll]
                public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);

                [InquirySelectOneByKey]
                public partial Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

                [InquirySelectAllByField("Name")]
                public partial IAsyncEnumerable<Organization> SelectByNameAsync(string name, CancellationToken cancellationToken = default);

                [InquiryInsert]
                public partial Task<int> InsertAsync(Organization o, CancellationToken cancellationToken = default);

                [InquiryUpdate]
                public partial Task<bool> UpdateAsync(Organization o, CancellationToken cancellationToken = default);

                [InquiryDeleteOneByKey]
                public partial Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);

                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Organization o, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("OrganizationStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // Unquoted identifiers (Oracle folds unquoted names to uppercase, so the DDL is created
        // unquoted to match) and :name bind parameters in every statement.
        Assert.Contains("private const string _sqlSelectAll = \"SELECT Key, Name FROM TOrganization\";", generatedText);
        Assert.Contains("private const string _sqlSelectByKey = \"SELECT Key, Name FROM TOrganization WHERE Key = :Key\";", generatedText);
        Assert.Contains("private const string _sqlSelectBy_Name = \"SELECT Key, Name FROM TOrganization WHERE Name = :Name\";", generatedText);
        Assert.Contains("private const string _sqlInsert = \"INSERT INTO TOrganization (Key, Name) VALUES (:Key, :Name)\";", generatedText);
        Assert.Contains("private const string _sqlUpdate = \"UPDATE TOrganization SET Name = :Name WHERE Key = :Key\";", generatedText);
        Assert.Contains("private const string _sqlDeleteByKey = \"DELETE FROM TOrganization WHERE Key = :Key\";", generatedText);

        // MERGE … USING (SELECT … FROM dual) upsert; SetClauses excludes the ON-clause key column.
        Assert.Contains(
            "private const string _sqlUpsert = \"MERGE INTO TOrganization target USING (SELECT :Key AS k0 FROM dual) source ON (target.Key = source.k0) WHEN MATCHED THEN UPDATE SET Name = :Name WHEN NOT MATCHED THEN INSERT (Key, Name) VALUES (:Key, :Name)\";",
            generatedText);

        // The runtime binds parameters with the hardcoded '@' prefix (shared emitter); Oracle's
        // BindByName=true matches them by name against the ':'-prefixed SQL.
        Assert.Contains("_p0.ParameterName = \"@Key\";", generatedText);
    }

    [Fact]
    public void OracleDialectRejectsUpsertOnGeneratedKey()
    {
        // KNOWN v1 LIMITATION: an Oracle MERGE joins on the key, which is NULL for a DB-generated
        // key, so it would never match (insert-only) and could not round-trip the generated value.
        // The builder throws rather than emit silently-wrong SQL; the generator degrades gracefully —
        // it reports INQ039 (Warning) and emits a throwing stub for the upsert method, so the rest of
        // the compilation still succeeds rather than the whole generator aborting.
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TWidget")]
            public sealed class Widget
            {
                [InquiryKey(IsGenerated = true)]
                public int? Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class WidgetStore : InquiryStore<Widget>
            {
                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Widget widget, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");

        var allDiagnostics = result.RunResult.Diagnostics.Concat(result.GeneratorDiagnostics).ToArray();
        Assert.Contains(allDiagnostics, d => d.Id == "INQ039" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();
        Assert.Contains("throw new global::System.NotSupportedException(", text);
        Assert.DoesNotContain("_sqlUpsert ", text); // the unsupported upsert const was skipped
    }

    [Fact]
    public void OracleDialectEmitsValuesDefaultForAllGeneratedInsert()
    {
        // Oracle has no DEFAULT VALUES clause; an all-database-supplied insert uses VALUES (DEFAULT).
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TWidget")]
            public sealed class Widget
            {
                [InquiryKey(IsGenerated = true)]
                public int? Id { get; set; }
            }

            public partial class WidgetStore : InquiryStore<Widget>
            {
                [InquiryInsert]
                public partial Task<int> InsertAsync(Widget widget, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        Assert.Contains("private const string _sqlInsert = \"INSERT INTO TWidget VALUES (DEFAULT)\";", generatedText);
        Assert.DoesNotContain("DEFAULT VALUES", generatedText);
    }

    [Fact]
    public void OracleDialectEmitsOffsetFetchPagination()
    {
        // Oracle 12c+ supports the ANSI OFFSET … ROWS FETCH NEXT … ROWS ONLY form (like SQL Server),
        // which requires a preceding ORDER BY (enforced by the generator).
        var source = PagingStore("""
            [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
            public partial Task<IReadOnlyList<User>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetStore(result);

        // The SQL text takes Oracle's ':' sigil for the synthetic paging parameters (the shared generator
        // applies SqlBuilder.ParameterName); the generated paging binder still emits the '@__offset'/
        // '@__limit' runtime parameters, which OracleInquiryConnectionFactory.FinalizeCommand reconciles.
        // Verified live by Inquiry.Oracle.Tests.PaginationIntegrationTests.
        Assert.Contains("ORDER BY Id ASC OFFSET :offset ROWS FETCH NEXT :limit ROWS ONLY", text);
        Assert.Contains("_p0.ParameterName = \"@__offset\";", text);
        Assert.Contains("_p1.ParameterName = \"@__limit\";", text);
    }

    [Fact]
    public void OracleDialectEmitsInSentinelWithColonParameterAndExpansion()
    {
        // The IN sentinel takes Oracle's ':' sigil (via SqlBuilder.ParameterName), and the emitted
        // InquiryInExpansion.Expand call must pass the SAME ':'-prefixed name so its runtime command-text
        // rewrite finds the baked sentinel. (A hardcoded '@CategoryId' would never match the ':CategoryId'
        // sentinel on Oracle, leaving the placeholder unbound — ORA-00936.) The per-element parameters the
        // expansion creates (:CategoryId0, …) are reconciled to bare names by
        // OracleInquiryConnectionFactory.FinalizeCommand under BindByName. Verified live by
        // Inquiry.Oracle.Tests.PredicateSelectIntegrationTests.
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: "Oracle");
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        // Unquoted identifiers (Oracle uppercase-folds) and the ':' bind sigil on the IN sentinel.
        Assert.Contains("WHERE CategoryId IN (:CategoryId)\";", generatedText);
        Assert.Contains("global::Inquiry.Parameters.InquiryInExpansion.Expand(_c, \":CategoryId\", categoryIds);", generatedText);
    }

    [Fact]
    public void OracleInsertReturningEmitsPlSqlRefCursorBlock()
    {
        // Oracle has no result-set RETURNING, so a ReturnEntity insert is emitted as an anonymous PL/SQL
        // block: INSERT, capture the database-generated key into a %TYPE local via RETURNING … INTO, then
        // OPEN a ref cursor (:rc) over the row. No INQ039, no throwing stub — ExecuteReader on the block
        // returns the cursor's reader (the OUT cursor is bound by OracleInquiryConnectionFactory).
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TWidget")]
            public sealed class Widget
            {
                [InquiryKey(IsGenerated = true)]
                public int? Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class WidgetStore : InquiryStore<Widget>
            {
                [InquiryInsert(ReturnEntity = true)]
                public partial Task<Widget?> InsertReturningAsync(Widget widget, CancellationToken cancellationToken = default);

                [InquirySelectOneByKey]
                public partial Task<Widget?> SelectByKeyAsync(int? id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");

        var allDiagnostics = result.RunResult.Diagnostics.Concat(result.GeneratorDiagnostics).ToArray();
        Assert.DoesNotContain(allDiagnostics, d => d.Id == "INQ039");
        Assert.DoesNotContain(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = generatedStore.GetText().ToString();

        // PL/SQL block: generated key captured into a %TYPE local, then OPEN :rc over the re-selected row.
        Assert.Contains(
            "private const string _sqlInsertReturning = \"DECLARE v_key TWidget.Id%TYPE; BEGIN INSERT INTO TWidget (Name) VALUES (:Name) RETURNING Id INTO v_key; OPEN :rc FOR SELECT Id, Name FROM TWidget WHERE Id = v_key; END;\";",
            text);
        Assert.DoesNotContain("throw new global::System.NotSupportedException(", text); // no stub
    }

    [Fact]
    public void OracleDialectBindsDateTimeColumnAsDbTypeDateTime()
    {
        // ODP.NET's OracleParameter.set_DbType rejects DbType.DateTime2 ("Value does not fall within the
        // expected range"), so inserting any entity with a System.DateTime column failed on Oracle. The
        // Oracle SqlBuilder maps System.DateTime to DbType.DateTime (which ODP.NET accepts), while every
        // other dialect keeps DbType.DateTime2 (SqlClient legacy-datetime precision). Verified live by
        // Inquiry.Oracle.Tests.OracleCoverageGapIntegrationTests (Employee/Order carry DateTime columns).
        var result = RunGenerator(DateTimeColumnSource, dialect: "Oracle");
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ReminderStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // The DueAt parameter binds DbType.DateTime (not DateTime2) under the Oracle dialect.
        Assert.Contains("_p0.DbType = global::System.Data.DbType.DateTime;", text);
        Assert.DoesNotContain("DbType.DateTime2", text);
    }

    [Fact]
    public void NonOracleDialectBindsDateTimeColumnAsDbTypeDateTime2()
    {
        // Preservation guard: the DateTime -> DbType.DateTime substitution is Oracle-only. SqlServer (and
        // every other dialect) keeps DbType.DateTime2, so the dialect-aware override cannot regress them.
        var result = RunGenerator(DateTimeColumnSource, dialect: "SqlServer");
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ReminderStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("_p0.DbType = global::System.Data.DbType.DateTime2;", text);
    }

    [Fact]
    public void OracleDialectEmitsInsertAllAndDegradesUpdateAll()
    {
        // Oracle batch InsertAll is real: Oracle has no multi-row VALUES, so the generator emits the
        // set-based `INSERT ALL INTO t (cols) VALUES (...) ... SELECT 1 FROM dual` form (a single INSERT
        // statement, so ExecuteNonQuery returns the inserted-row count) with ':'-sigil parameters. UpdateAll
        // has no portable Oracle multi-row form, so it stays a throwing stub + INQ039; batch DELETE
        // (IN-expansion) works. Verified live by Inquiry.Oracle.Tests.BatchIntegrationTests.
        var result = RunGenerator(BatchStoreSource, dialect: "Oracle");

        var inq039 = result.RunResult.Diagnostics.Concat(result.GeneratorDiagnostics)
            .Where(static d => d.Id == "INQ039" && d.Severity == DiagnosticSeverity.Warning)
            .ToArray();
        // Only UpdateAll degrades; InsertAll is supported (no INQ039 naming it).
        Assert.Contains(inq039, static d => d.GetMessage().Contains("UpdateAllAsync", StringComparison.Ordinal));
        Assert.DoesNotContain(inq039, static d => d.GetMessage().Contains("InsertAllAsync", StringComparison.Ordinal));
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("RegionStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // INSERT ALL shape: `INSERT ALL ` header, per-row `INTO t (cols) VALUES (`, ':' sigil params, and a
        // trailing dual select.
        Assert.Contains("private const string _sqlInsertAllPrefix = \"INSERT ALL \";", text);
        Assert.Contains("private const string _sqlInsertAllRowOpen = \"INTO TRegion (RegionId, Name) VALUES (\";", text);
        Assert.Contains("_sb.Append(\":p\").Append(_r).Append(\"_0\");", text);
        Assert.Contains("_sb.Append(\" SELECT 1 FROM dual\");", text);
        // UpdateAll degrades to a throwing stub; its template const is skipped. DeleteAll still emitted.
        Assert.Contains("throw new global::System.NotSupportedException(", text);
        Assert.DoesNotContain("_sqlUpdateAllRow", text);
        Assert.Contains("_sqlDeleteAll", text);
    }

    [Fact]
    public void NonOracleDialectEmitsMultiRowValuesBatchInsertAndUpdate()
    {
        // Preservation guard: non-Oracle dialects keep the multi-row VALUES InsertAll and the per-row
        // UPDATE-batch UpdateAll, with no throwing stub (the shape hooks default for all of them). SqlServer
        // shown here.
        var result = RunGenerator(BatchStoreSource, dialect: "SqlServer");
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("RegionStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Multi-row VALUES shape: "(" row-open, "@" sigil, no INSERT ALL / dual-select footer.
        Assert.Contains("private const string _sqlInsertAllRowOpen = \"(\";", text);
        Assert.Contains("_sb.Append(\"@p\").Append(_r).Append(\"_0\");", text);
        Assert.DoesNotContain("INSERT ALL", text);
        Assert.DoesNotContain("SELECT 1 FROM dual", text);
        Assert.Contains("_sqlUpdateAllRow", text);
        Assert.DoesNotContain("throw new global::System.NotSupportedException(", text);
    }

    // A client-keyed entity with all three batch operations (InsertAll/UpdateAll/DeleteAll).
    private const string BatchStoreSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TRegion")]
        public sealed class Region
        {
            [InquiryKey]
            public int RegionId { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;
        }

        public partial class RegionStore : InquiryStore<Region>
        {
            [InquiryInsertAll]
            public partial Task<int> InsertAllAsync(IEnumerable<Region> regions, CancellationToken cancellationToken = default);

            [InquiryUpdateAll]
            public partial Task<int> UpdateAllAsync(IEnumerable<Region> regions, CancellationToken cancellationToken = default);

            [InquiryDeleteAll]
            public partial Task<int> DeleteAllAsync(IEnumerable<int> regionIds, CancellationToken cancellationToken = default);
        }
        """;

    // A generated-key entity carrying a single System.DateTime column, plus a store that inserts it.
    // The insert binder excludes the generated key, so the DueAt parameter is _p0.
    private const string DateTimeColumnSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TReminder")]
        public sealed class Reminder
        {
            [InquiryKey(IsGenerated = true)]
            public int? Id { get; set; }

            [InquiryColumn("DueAt")]
            public DateTime DueAt { get; set; }
        }

        public partial class ReminderStore : InquiryStore<Reminder>
        {
            [InquiryInsert]
            public partial Task<int> InsertAsync(Reminder reminder, CancellationToken cancellationToken = default);
        }
        """;
}
