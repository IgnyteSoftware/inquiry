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
        // The builder throws rather than emit silently-wrong SQL; Roslyn surfaces that as a
        // generator diagnostic (a loud build failure), not a runtime data bug.
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

        Assert.NotEmpty(result.GeneratorDiagnostics);
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

        // KNOWN v1 LIMITATION (see OracleSqlBuilder remarks): the synthetic paging parameters keep
        // the shared '@__offset'/'@__limit' names baked by StoreProcessor. Oracle's SQL parser does
        // NOT treat '@__offset' as a bind placeholder (Oracle uses ':'), so a paged/keyset query does
        // not run against a live Oracle yet — the proper fix is a dialect-aware synthetic-parameter
        // prefix in the shared generator, deferred to avoid colliding with in-flight workstreams.
        // This test pins the CURRENT emitted text, not a working live contract.
        Assert.Contains("ORDER BY Id ASC OFFSET @__offset ROWS FETCH NEXT @__limit ROWS ONLY", text);
        Assert.Contains("_p0.ParameterName = \"@__offset\";", text);
        Assert.Contains("_p1.ParameterName = \"@__limit\";", text);
    }
}
