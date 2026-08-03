using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Write-side enforcement of <c>[InquiryGlobalFilter(EnforceOnWrites = true)]</c>: the filter's term
/// AND-composes onto every key-based write (update, delete, hard delete, restore, batch delete) on all
/// six dialects, the emulated-returning follow-up SELECTs carry it too (the cross-tenant read-back
/// leak), the write binder is a distinct helper from the read binder, and upsert is rejected outright
/// (INQ095). The regression that matters most is the negative one: an entity that does NOT opt in must
/// produce byte-identical write SQL to before the feature existed.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string TenantDocEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TDoc")]
        public sealed class Doc
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn("TenantId"), InquiryGlobalFilter(ContextKey = "TenantId", EnforceOnWrites = true)]
            public long TenantId { get; set; }
        }
        """;

    // Normalized to LF for the same reason as DocStoreSource: tests reshape this entity with multi-line
    // string.Replace searches, and a raw string literal carries the checkout's line endings.
    private static string TenantDocEntityLf => TenantDocEntity.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string TenantDocStore(string methods) =>
        TenantDocEntityLf + "\n\npublic partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>\n{\n" + methods + "\n}\n";

    /// <summary>
    /// Extracts the (still C#-escaped) body of a generated <c>private const string</c> so a test can
    /// assert on one statement without matching the whole file.
    /// </summary>
    private static string GeneratedConst(string text, string fieldName)
    {
        var marker = "private const string " + fieldName + " = \"";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"generated const '{fieldName}' was not emitted");
        start += marker.Length;
        var end = text.IndexOf("\";", start, StringComparison.Ordinal);
        Assert.True(end > start, $"generated const '{fieldName}' is unterminated");
        return text.Substring(start, end - start);
    }

    private const string TenantWriteMethods = """
        [InquiryUpdate]
        public partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);

        [InquiryUpdate(ReturnEntity = true)]
        public partial Task<Doc?> UpdateReturningAsync(Doc doc, CancellationToken cancellationToken = default);

        [InquiryDeleteOneByKey]
        public partial Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
        """;

    [Fact]
    public void EnforceOnWritesComposesOntoKeyBasedWrites_Sqlite()
    {
        var result = RunGenerator(TenantDocStore(TenantWriteMethods));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Equal(
            "UPDATE \\\"TDoc\\\" SET \\\"Name\\\" = @Name, \\\"TenantId\\\" = @TenantId WHERE \\\"Id\\\" = @Id AND \\\"TenantId\\\" = @__gf_TenantId",
            GeneratedConst(text, "_sqlUpdate"));
        Assert.Equal(
            "DELETE FROM \\\"TDoc\\\" WHERE \\\"Id\\\" = @Id AND \\\"TenantId\\\" = @__gf_TenantId",
            GeneratedConst(text, "_sqlDeleteByKey"));
        // RETURNING is a suffix on the UPDATE, so the enforced term guards the projection as well.
        Assert.EndsWith(
            "WHERE \\\"Id\\\" = @Id AND \\\"TenantId\\\" = @__gf_TenantId RETURNING \\\"Id\\\", \\\"Name\\\", \\\"TenantId\\\"",
            GeneratedConst(text, "_sqlUpdateReturning"));
    }

    [Fact]
    public void EnforceOnWritesLeavesInsertUnfiltered_Sqlite()
    {
        var result = RunGenerator(TenantDocStore("""
            [InquiryInsert]
            public partial Task<int> InsertAsync(Doc doc, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        // An INSERT has no row to filter and the filter column is never auto-stamped.
        Assert.DoesNotContain("__gf_", GeneratedConst(text, "_sqlInsert"));
        Assert.DoesNotContain("__BindGlobalFiltersWrite", text);
    }

    [Fact]
    public void NoOptInLeavesWriteSqlUnfiltered_Sqlite()
    {
        // The byte-identical invariant: a plain (read-only) global filter must not reach any write.
        var source = TenantDocEntityLf.Replace(
            "InquiryGlobalFilter(ContextKey = \"TenantId\", EnforceOnWrites = true)",
            "InquiryGlobalFilter(ContextKey = \"TenantId\")");
        var result = RunGenerator(source + "\n\npublic partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>\n{\n" + TenantWriteMethods + "\n}\n");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Equal(
            "UPDATE \\\"TDoc\\\" SET \\\"Name\\\" = @Name, \\\"TenantId\\\" = @TenantId WHERE \\\"Id\\\" = @Id",
            GeneratedConst(text, "_sqlUpdate"));
        Assert.Equal(
            "DELETE FROM \\\"TDoc\\\" WHERE \\\"Id\\\" = @Id",
            GeneratedConst(text, "_sqlDeleteByKey"));
        Assert.DoesNotContain("__BindGlobalFiltersWrite", text);
    }

    [Fact]
    public void KeepWhenModeEnforcesWithLiteral_Sqlite()
    {
        var source = TenantDocEntityLf
            .Replace(
                "[InquiryColumn(\"TenantId\"), InquiryGlobalFilter(ContextKey = \"TenantId\", EnforceOnWrites = true)]\n    public long TenantId { get; set; }",
                "[InquiryColumn(\"IsActive\"), InquiryGlobalFilter(EnforceOnWrites = true)]\n    public bool IsActive { get; set; }");
        var result = RunGenerator(source + "\n\npublic partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>\n{\n[InquiryDeleteOneByKey]\npublic partial Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);\n}\n");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Equal(
            "DELETE FROM \\\"TDoc\\\" WHERE \\\"Id\\\" = @Id AND \\\"IsActive\\\" = 1",
            GeneratedConst(text, "_sqlDeleteByKey"));
        // A constant-mode filter needs no ambient value, so no binder helper is emitted.
        Assert.DoesNotContain("__BindGlobalFiltersWrite", text);
    }

    private const string SoftDeleteTenantEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TDoc")]
        public sealed class Doc
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn("TenantId"), InquiryGlobalFilter(ContextKey = "TenantId", EnforceOnWrites = true)]
            public long TenantId { get; set; }

            [InquiryColumn("IsDeleted"), InquirySoftDelete]
            public bool IsDeleted { get; set; }
        }
        """;

    [Fact]
    public void EnforceOnWritesComposesOntoSoftDeleteHardDeleteAndRestore_Sqlite()
    {
        var result = RunGenerator(SoftDeleteTenantEntity + """


            public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
            {
                [InquiryDeleteOneByKey]
                public partial Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);

                [InquiryDeleteOneByKey(HardDelete = true)]
                public partial Task<bool> PurgeAsync(long id, CancellationToken cancellationToken = default);

                [InquiryRestoreOneByKey]
                public partial Task<bool> RestoreAsync(long id, CancellationToken cancellationToken = default);
            }
            """);
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Equal(
            "UPDATE \\\"TDoc\\\" SET \\\"IsDeleted\\\" = 1 WHERE \\\"Id\\\" = @Id AND \\\"TenantId\\\" = @__gf_TenantId",
            GeneratedConst(text, "_sqlDeleteByKey"));
        Assert.Equal(
            "UPDATE \\\"TDoc\\\" SET \\\"IsDeleted\\\" = 0 WHERE \\\"Id\\\" = @Id AND \\\"TenantId\\\" = @__gf_TenantId",
            GeneratedConst(text, "_sqlRestoreByKey"));

        // A hard delete carries the tenant term but NOT the soft-delete activeness term: it must still
        // be able to remove an already-soft-deleted row.
        var hardDelete = GeneratedConst(text, "_sqlHardDeleteByKey");
        Assert.Equal("DELETE FROM \\\"TDoc\\\" WHERE \\\"Id\\\" = @Id AND \\\"TenantId\\\" = @__gf_TenantId", hardDelete);
        Assert.DoesNotContain("IsDeleted", hardDelete);
    }

    [Fact]
    public void EnforceOnWritesComposesOntoBatchDelete_Sqlite()
    {
        var result = RunGenerator(TenantDocStore("""
            [InquiryDeleteAll]
            public partial Task<int> DeleteAllAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Equal(
            "DELETE FROM \\\"TDoc\\\" WHERE \\\"Id\\\" IN (SELECT value FROM json_each(@keys)) AND \\\"TenantId\\\" = @__gf_TenantId",
            GeneratedConst(text, "_sqlDeleteAll"));
        // The collection route binds the chunk through a DbCommand, so the DbCommand overload is called.
        Assert.Contains("__BindGlobalFiltersWrite(_c);", text);
    }

    [Fact]
    public void EnforceOnWritesComposesOntoHardDeleteByPredicate_Sqlite()
    {
        var result = RunGenerator(TenantDocStore("""
            [InquiryDeleteWhere]
            [InquiryWhere("Name")]
            public partial Task<int> DeleteByNameAsync(string name, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        // A set-based hard DELETE drops the activeness predicate but is still inside the boundary.
        Assert.Equal(
            "DELETE FROM \\\"TDoc\\\" WHERE \\\"Name\\\" = @Name AND \\\"TenantId\\\" = @__gf_TenantId",
            GeneratedConst(text, "_sqlDeleteWhere_DeleteByNameAsync"));
    }

    [Fact]
    public void WriteFilterBindsBeforeCollectionExpansionInPredicateDelete_Sqlite()
    {
        // NOT IN always routes through the InquiryInExpansion sentinel (never the array bind), and
        // ExpandCore budgets its element count and bucket padding against command.Parameters.Count at
        // entry. The write filter parameter must therefore be on the command BEFORE the expansion runs,
        // or a maximally-packed list plus the filter parameter would land one past the configured cap.
        var result = RunGenerator(TenantDocStore("""
            [InquiryDeleteWhere]
            [InquiryWhere("Name", Compare.NotIn)]
            public partial Task<int> DeleteExceptNamesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        var binderCall = text.IndexOf("__BindGlobalFiltersWrite(_c);", StringComparison.Ordinal);
        var expansionCall = text.IndexOf("InquiryInExpansion.ExpandNotIn(_c", StringComparison.Ordinal);
        Assert.True(binderCall >= 0, "the write filter binder is not called in the predicate delete");
        Assert.True(expansionCall >= 0, "the NOT IN sentinel expansion was not emitted");
        Assert.True(binderCall < expansionCall, "the write filter parameter must bind before the IN expansion measures its budget");
    }

    [Fact]
    public void DeleteReturningOnWriteEnforcedEntityDegradesCleanly_MySql()
    {
        // MySQL has no DELETE ... RETURNING, so the method degrades to an INQ039 stub. The write filter
        // machinery must not change that: no partially-emitted returning const, and the stub never calls
        // the write binder (which would bind parameters no SQL references).
        var result = RunGenerator(TenantDocStore("""
            [InquiryDeleteOneByKey(ReturnEntity = true)]
            public partial Task<Doc?> DeleteReturningAsync(long id, CancellationToken cancellationToken = default);
            """), dialect: "MySql", unsupportedOperationSeverity: ReportDiagnostic.Warn);

        Assert.Contains(result.RunResult.Diagnostics,
            static d => d.Id == "INQ039" && d.Severity == DiagnosticSeverity.Warning);
        var text = GetTenantDocStore(result);
        Assert.DoesNotContain("_sqlDeleteReturning", text);
        Assert.Contains("throw new global::System.NotSupportedException", text);
        Assert.DoesNotContain("__BindGlobalFiltersWrite(_cmd);", text);
    }

    [Fact]
    public void EnforceOnWritesComposesWithConcurrencyToken_Sqlite()
    {
        var source = TenantDocEntityLf.Replace(
            "    [InquiryColumn(\"TenantId\")",
            "    [InquiryColumn(\"Version\"), InquiryConcurrencyToken]\n    public int Version { get; set; }\n\n    [InquiryColumn(\"TenantId\")");
        var result = RunGenerator(source + "\n\npublic partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>\n{\n[InquiryUpdate]\npublic partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);\n}\n");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        // Key, then token, then the enforced term — one composition order for all three.
        Assert.EndsWith(
            "WHERE \\\"Id\\\" = @Id AND \\\"Version\\\" = @Version AND \\\"TenantId\\\" = @__gf_TenantId",
            GeneratedConst(text, "_sqlUpdate"));
    }

    [Fact]
    public void WriteBinderIsSeparateFromReadBinderAndEmitsBothTargets_Sqlite()
    {
        // The entity carries two ContextKey filters, only one of which enforces on writes: reads bind
        // both, writes bind only the enforced one. A shared binder would over-bind the write command.
        var source = TenantDocEntityLf.Replace(
            "    [InquiryColumn(\"TenantId\")",
            "    [InquiryColumn(\"Region\"), InquiryGlobalFilter(ContextKey = \"Region\")]\n    public long Region { get; set; }\n\n    [InquiryColumn(\"TenantId\")");
        var result = RunGenerator(source + """


            public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<Doc>> SelectAllAsync(CancellationToken cancellationToken = default);

                [InquiryUpdateAll]
                public partial Task<int> UpdateAllAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default);
            }
            """);
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        // Read SQL carries both filters; write SQL only the enforced one.
        Assert.Contains("\\\"Region\\\" = @__gf_Region", GeneratedConst(text, "_sqlSelectAll"));
        var update = GeneratedConst(text, "_sqlUpdate");
        Assert.Contains("\\\"TenantId\\\" = @__gf_TenantId", update);
        Assert.DoesNotContain("@__gf_Region", update);

        // Both helper overloads exist; the batch row binder goes through InquiryParameterTarget.
        Assert.Contains("private static void __BindGlobalFiltersWrite(global::System.Data.Common.DbCommand _cmd)", text);
        Assert.Contains("private static void __BindGlobalFiltersWrite(global::Inquiry.Commands.InquiryParameterTarget _cmd)", text);
        Assert.Contains("__BindGlobalFiltersWrite(_t);", text);
        Assert.Contains("global::Inquiry.InquiryFilterContext.GetRequired<long>(\"TenantId\")", text);
    }

    [Fact]
    public void EmulatedUpdateReturningGuardsReadBackWithRowCountOrTerm_MySql()
    {
        var result = RunGenerator(TenantDocStore("""
            [InquiryUpdate(ReturnEntity = true)]
            public partial Task<Doc?> UpdateReturningAsync(Doc doc, CancellationToken cancellationToken = default);
            """), dialect: "MySql");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        var sql = GeneratedConst(text, "_sqlUpdateReturning");
        var halves = sql.Split(new[] { "; " }, StringSplitOptions.None);
        Assert.Equal(2, halves.Length);

        // The UPDATE carries the term outright — that is what blocks the cross-tenant write.
        Assert.EndsWith("WHERE `Id` = @Id AND `TenantId` = @__gf_TenantId", halves[0]);

        // The follow-up SELECT re-reads AFTER the write, so it must NOT simply re-test the term: an
        // update that legitimately changes the filter column would then return null. ROW_COUNT() proves
        // the update passed the predicate; the OR-ed term covers a no-op update, which reports zero
        // when the connection uses changed-row rather than found-row semantics. A cross-tenant attempt
        // fails both operands.
        Assert.StartsWith("SELECT", halves[1]);
        Assert.EndsWith(
            "WHERE `Id` = @Id AND (ROW_COUNT() > 0 OR `TenantId` = @__gf_TenantId)",
            halves[1]);
    }

    [Fact]
    public void EmulatedUpdateReturningWithTokenGuardsReadBackWithRowCountAlone_MySql()
    {
        // A version bump guarantees a successful update changed a column, so ROW_COUNT() > 0 alone
        // already proves the enforced predicate passed — and re-testing the term would break an
        // update that changes the filter column. The read-back is byte-identical to the no-filter case.
        var source = TenantDocEntityLf.Replace(
            "    [InquiryColumn(\"TenantId\")",
            "    [InquiryColumn(\"Version\"), InquiryConcurrencyToken]\n    public int Version { get; set; }\n\n    [InquiryColumn(\"TenantId\")");
        var result = RunGenerator(
            source + "\n\npublic partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>\n{\n[InquiryUpdate(ReturnEntity = true)]\npublic partial Task<Doc?> UpdateReturningAsync(Doc doc, CancellationToken cancellationToken = default);\n}\n",
            dialect: "MySql");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        var halves = GeneratedConst(text, "_sqlUpdateReturning").Split(new[] { "; " }, StringSplitOptions.None);
        Assert.EndsWith("WHERE `Id` = @Id AND `Version` = @Version AND `TenantId` = @__gf_TenantId", halves[0]);
        Assert.EndsWith("WHERE `Id` = @Id AND ROW_COUNT() > 0", halves[1]);
        Assert.DoesNotContain("__gf_", halves[1]);
    }

    [Fact]
    public void EmulatedDeleteReturningCarriesEnforcedTerm_MariaDb()
    {
        var result = RunGenerator(TenantDocStore("""
            [InquiryDeleteOneByKey(ReturnEntity = true)]
            public partial Task<Doc?> DeleteReturningAsync(long id, CancellationToken cancellationToken = default);
            """), dialect: "MariaDb");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.StartsWith(
            "DELETE FROM `TDoc` WHERE `Id` = @Id AND `TenantId` = @__gf_TenantId RETURNING",
            GeneratedConst(text, "_sqlDeleteReturning"));
        Assert.Contains("__BindGlobalFiltersWrite(_cmd);", text);
    }

    [Fact]
    public void UpdateReturningElseBranchReselectsByKeyOnly_Oracle()
    {
        var result = RunGenerator(TenantDocStore("""
            [InquiryUpdate(ReturnEntity = true)]
            public partial Task<Doc?> UpdateReturningAsync(Doc doc, CancellationToken cancellationToken = default);
            """), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        var sql = GeneratedConst(text, "_sqlUpdateReturning");
        Assert.StartsWith("BEGIN UPDATE ", sql);
        Assert.EndsWith("; END IF; END;", sql);

        var elseIndex = sql.IndexOf("ELSE OPEN :rc FOR SELECT", StringComparison.Ordinal);
        Assert.True(elseIndex > 0, "the update-returning block has no ELSE re-select branch");

        // Oracle encodes bind names (OracleBindName), so the term appears under its encoded stem.
        const string encodedFilterBind = ":iq1$gfTena$";

        // The UPDATE carries the term — that is what blocks the cross-tenant write.
        Assert.Contains(encodedFilterBind, sql.Substring(0, elseIndex));

        // The ELSE branch deliberately does NOT. It runs only when SQL%ROWCOUNT != 0, which already
        // proves the UPDATE passed the predicate, so the term adds no protection — and would return
        // null for an update that legitimately changed the filter column.
        Assert.DoesNotContain(encodedFilterBind, sql.Substring(elseIndex));
    }

    [Fact]
    public void BatchDeleteArrayBindRepeatsFilterValueAcrossTheArray_Oracle()
    {
        var result = RunGenerator(TenantDocStore("""
            [InquiryDeleteAll]
            public partial Task<int> DeleteAllAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);
            """), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        // ArrayBindCount = N requires EVERY parameter to be an N-element array, so the ambient value is
        // read once and repeated rather than bound as a scalar.
        Assert.Contains("private static void __BindGlobalFiltersWrite(global::System.Data.Common.DbCommand _cmd, int _count)", text);
        Assert.Contains("var _fv0 = new object?[_count];", text);
        Assert.Contains("__BindGlobalFiltersWrite(_cmd, _keys.Count);", text);
        // The per-item route (no array binding) still binds a scalar through the target overload.
        Assert.Contains("__BindGlobalFiltersWrite(_t);", text);
    }

    [Fact]
    public void KeyWritesCarryEnforcedTerm_SqlServer()
    {
        var result = RunGenerator(TenantDocStore(TenantWriteMethods), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.EndsWith("WHERE [Id] = @Id AND [TenantId] = @__gf_TenantId", GeneratedConst(text, "_sqlUpdate"));
        Assert.EndsWith("WHERE [Id] = @Id AND [TenantId] = @__gf_TenantId", GeneratedConst(text, "_sqlDeleteByKey"));
        // OUTPUT only emits rows the UPDATE actually touched, so guarding its WHERE is sufficient.
        Assert.Contains("WHERE [Id] = @Id AND [TenantId] = @__gf_TenantId; SELECT", GeneratedConst(text, "_sqlUpdateReturning"));
    }

    [Fact]
    public void KeyWritesCarryEnforcedTerm_PostgreSql()
    {
        var result = RunGenerator(TenantDocStore(TenantWriteMethods), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.EndsWith("WHERE \\\"Id\\\" = @Id AND \\\"TenantId\\\" = @__gf_TenantId", GeneratedConst(text, "_sqlUpdate"));
        Assert.EndsWith("WHERE \\\"Id\\\" = @Id AND \\\"TenantId\\\" = @__gf_TenantId", GeneratedConst(text, "_sqlDeleteByKey"));
    }

    [Fact]
    public void SetBasedUpdateAllQualifiesEnforcedTermWithTargetAlias_MySql()
    {
        var result = RunGenerator(TenantDocStore("""
            [InquiryUpdateAll]
            public partial Task<int> UpdateAllAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default);
            """), dialect: "MySql");
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        // Qualified with the TARGET alias: matching `_v` would test the caller's own payload and
        // enforce nothing.
        Assert.Contains("WHERE `_t`.`TenantId` = @__gf_TenantId", text);
        Assert.DoesNotContain("`_v`.`TenantId` = @__gf_TenantId", text);
        // The chunk statement references the parameter once per command, not once per row.
        Assert.Contains("__BindGlobalFiltersWrite(_cmd);", text);
    }

    [Fact]
    public void SetBasedUpdateAllChunkBudgetLeavesRoomForTheFilterParameter_MySql()
    {
        // Doc binds 3 parameters per item (Id, Name, TenantId) and MySQL's ceiling is 65535 — which 3
        // divides exactly. Without reserving the once-per-command filter parameter the descriptor would
        // admit 21845 items and bind 65536 parameters, one over the protocol limit.
        var enforced = RunGenerator(TenantDocStore("""
            [InquiryUpdateAll]
            public partial Task<int> UpdateAllAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default);
            """), dialect: "MySql");
        AssertNoErrors(enforced);
        Assert.Contains("maxItemsPerCommand: 21844);", GetTenantDocStore(enforced));

        // Without an enforced filter there is no per-command parameter to reserve, so the budget — and
        // the emitted SQL around it — is unchanged.
        var plain = RunGenerator(
            TenantDocEntityLf.Replace(", EnforceOnWrites = true", string.Empty)
                + "\n\npublic partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>\n{\n[InquiryUpdateAll]\npublic partial Task<int> UpdateAllAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default);\n}\n",
            dialect: "MySql");
        AssertNoErrors(plain);
        Assert.Contains("maxItemsPerCommand: 21845);", GetTenantDocStore(plain));
    }

    [Fact]
    public void SuppressedInvalidConstantFilterStillEnforcesWrites_Sqlite()
    {
        // A nullable bool fails INQ059. Suppressing that error must not silently drop the write
        // boundary the author declared — otherwise they get neither the diagnostic nor the protection,
        // and the INQ095 upsert guard goes quiet too.
        var source = TenantDocEntityLf.Replace(
            "[InquiryColumn(\"TenantId\"), InquiryGlobalFilter(ContextKey = \"TenantId\", EnforceOnWrites = true)]\n    public long TenantId { get; set; }",
            "[InquiryColumn(\"IsActive\"), InquiryGlobalFilter(EnforceOnWrites = true)]\n    public bool? IsActive { get; set; }");
        var result = RunGenerator(
            source + "\n\npublic partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>\n{\n[InquiryDeleteOneByKey]\npublic partial Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);\n\n[InquiryUpsert]\npublic partial Task<int> UpsertAsync(Doc doc, CancellationToken cancellationToken = default);\n}\n",
            additionalDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["INQ059"] = ReportDiagnostic.Suppress });

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ059");
        // The write still carries the term, and the upsert is still rejected.
        var text = GetTenantDocStore(result);
        Assert.Equal(
            "DELETE FROM \\\"TDoc\\\" WHERE \\\"Id\\\" = @Id AND \\\"IsActive\\\" = 1",
            GeneratedConst(text, "_sqlDeleteByKey"));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ095");
    }

    [Fact]
    public void UpsertOnWriteEnforcedEntityIsRejected()
    {
        var result = RunGenerator(TenantDocStore("""
            [InquiryUpsert]
            public partial Task<int> UpsertAsync(Doc doc, CancellationToken cancellationToken = default);
            """));

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ095");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("insert branch cannot be filtered", diagnostic.GetMessage());
    }

    [Fact]
    public void UpsertRejectionFailsClosedWhenTheDiagnosticIsSuppressed()
    {
        var result = RunGenerator(
            TenantDocStore("""
                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Doc doc, CancellationToken cancellationToken = default);
                """),
            additionalDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["INQ095"] = ReportDiagnostic.Suppress });

        // Suppressing the error must not buy back an unenforced statement. This store's ONLY method is
        // the upsert, so every method was rejected and the whole store degrades to throwing stubs.
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ095");
        var text = GetTenantDocStore(result);
        Assert.DoesNotContain("_sqlUpsert", text);
        Assert.Contains("UpsertAsync", text);
        Assert.Contains("throw new global::System.NotSupportedException", text);
    }

    [Fact]
    public void SuppressedUpsertRejectionOnAMultiMethodStoreOmitsTheMethodEntirely()
    {
        var result = RunGenerator(
            TenantDocStore("""
                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Doc doc, CancellationToken cancellationToken = default);

                [InquiryUpdate]
                public partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);
                """),
            additionalDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["INQ095"] = ReportDiagnostic.Suppress });

        // The stub-only fallback fires only when EVERY method is rejected. With a surviving sibling the
        // upsert is simply not emitted, so the user's `partial` declaration has no implementation and
        // the build fails on CS8795 — still fail-closed, but by a different mechanism than the stub.
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ095");
        var text = GetTenantDocStore(result);
        Assert.DoesNotContain("_sqlUpsert", text);
        Assert.DoesNotContain("UpsertAsync", text);
        Assert.Contains("UpdateAsync", text);
        Assert.Contains(result.Compilation.GetDiagnostics(), static d => d.Id == "CS8795");
    }
}
