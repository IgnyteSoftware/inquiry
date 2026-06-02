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
    public void OracleInsertReturningDegradesToThrowingStubWithWarning()
    {
        // Oracle cannot emit INSERT … RETURNING as a result set (v1 limitation). Rather than the builder's
        // NotSupportedException aborting the whole compilation, the generator must report INQ039 (Warning)
        // and emit a throwing stub for the offending method while still emitting the rest of the store.
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

                [InquiryInsert]
                public partial Task<int> InsertAsync(Widget widget, CancellationToken cancellationToken = default);

                [InquirySelectOneByKey]
                public partial Task<Widget?> SelectByKeyAsync(int? id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");

        // No generator crash, and the compilation has no errors (the stub satisfies the partial decl).
        var allDiagnostics = result.RunResult.Diagnostics.Concat(result.GeneratorDiagnostics).ToArray();
        Assert.Contains(allDiagnostics, d => d.Id == "INQ039" && d.Severity == DiagnosticSeverity.Warning);
        Assert.DoesNotContain(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = generatedStore.GetText().ToString();

        // The insert-returning method is a throwing stub; the other methods still emit normally.
        Assert.Contains("InsertReturningAsync", text);
        Assert.Contains("throw new global::System.NotSupportedException(", text);
        Assert.Contains("_sqlInsert ", text);          // plain insert const still emitted
        Assert.Contains("_sqlSelectByKey ", text);     // select-by-key still emitted
        Assert.DoesNotContain("_sqlInsertReturning", text); // the unsupported const was skipped
    }
}
