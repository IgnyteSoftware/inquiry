using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Global-query-filter emission tests ([InquiryGlobalFilter]): every SELECT AND-composes the column's
/// keep condition (via the same active-row predicate soft delete uses), <c>KeepWhen = false</c> inverts
/// the literal, multiple filters AND-compose, the filter co-exists with soft delete, and — unlike soft
/// delete — it is NOT dropped by <c>IncludeDeleted</c>. Also verifies the per-dialect literals,
/// projection composition, and the INQ059 validation diagnostics.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ActiveEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TAccount")]
        public sealed class Account
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn("IsActive"), InquiryGlobalFilter]
            public bool IsActive { get; set; }
        }
        """;

    private static string AccountStore(string methods) =>
        ActiveEntity + "\n\npublic partial class AccountStore : Inquiry.Stores.InquiryStore<Demo.Account>\n{\n" + methods + "\n}\n";

    private static string GetAccountStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("AccountStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    private const string AccountCrud = """
        [InquirySelectAll]
        public partial Task<IReadOnlyList<Account>> SelectAllAsync(CancellationToken cancellationToken = default);

        [InquirySelectOneByKey]
        public partial Task<Account?> SelectByKeyAsync(long id, CancellationToken cancellationToken = default);

        [InquirySelectAllByField("Name")]
        public partial Task<IReadOnlyList<Account>> SelectByNameAsync(string name, CancellationToken cancellationToken = default);
        """;

    [Fact]
    public void GlobalFilterComposesIntoEverySelect_Sqlite()
    {
        var result = RunGenerator(AccountStore(AccountCrud));
        AssertNoErrors(result);
        var text = GetAccountStore(result);

        Assert.Contains("private const string _sqlSelectAll = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"IsActive\\\" FROM \\\"TAccount\\\" WHERE \\\"IsActive\\\" = 1\";", text);
        Assert.Contains("_sqlSelectByKey = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"IsActive\\\" FROM \\\"TAccount\\\" WHERE \\\"Id\\\" = @Id AND \\\"IsActive\\\" = 1\";", text);
        Assert.Contains("_sqlSelectBy_Name = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"IsActive\\\" FROM \\\"TAccount\\\" WHERE \\\"Name\\\" = @Name AND \\\"IsActive\\\" = 1\";", text);
    }

    [Fact]
    public void GlobalFilterComposesIntoCountAndAggregate_Sqlite()
    {
        var result = RunGenerator(AccountStore("""
            [InquiryCount]
            public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetAccountStore(result);

        Assert.Contains("SELECT COUNT(*) FROM \\\"TAccount\\\" WHERE \\\"IsActive\\\" = 1", text);
    }

    [Fact]
    public void KeepWhenFalseInvertsLiteral_Sqlite()
    {
        // An IsArchived-style flag: keep the rows where the flag is false (unarchived).
        const string archivedEntity = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TPost")]
            public sealed class Post
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("IsArchived"), InquiryGlobalFilter(KeepWhen = false)]
                public bool IsArchived { get; set; }
            }

            public partial class PostStore : Inquiry.Stores.InquiryStore<Demo.Post>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<Post>> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(archivedEntity);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("PostStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("FROM \\\"TPost\\\" WHERE \\\"IsArchived\\\" = 0", text);
    }

    [Fact]
    public void MultipleGlobalFiltersAreAndComposed_Sqlite()
    {
        const string twoFilterEntity = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TItem")]
            public sealed class Item
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("IsActive"), InquiryGlobalFilter]
                public bool IsActive { get; set; }

                [InquiryColumn("IsPublished"), InquiryGlobalFilter]
                public bool IsPublished { get; set; }
            }

            public partial class ItemStore : Inquiry.Stores.InquiryStore<Demo.Item>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<Item>> SelectAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(twoFilterEntity);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("WHERE \\\"IsActive\\\" = 1 AND \\\"IsPublished\\\" = 1", text);
    }

    [Fact]
    public void GlobalFilterComposesWithPredicateAndPaging_Sqlite()
    {
        var result = RunGenerator(AccountStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Name", Compare.Like)]
            public partial Task<IReadOnlyList<Account>> SearchAsync(string name, CancellationToken cancellationToken = default);

            [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
            public partial Task<IReadOnlyList<Account>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetAccountStore(result);

        Assert.Contains("\\\"Name\\\" LIKE @Name AND \\\"IsActive\\\" = 1", text);
        Assert.Contains("WHERE \\\"IsActive\\\" = 1 ORDER BY \\\"Id\\\" ASC LIMIT @__limit OFFSET @__offset", text);
    }

    private const string ActiveAndSoftDeleteEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TTenantRow")]
        public sealed class TenantRow
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("IsActive"), InquiryGlobalFilter]
            public bool IsActive { get; set; }

            [InquiryColumn("IsDeleted"), InquirySoftDelete]
            public bool IsDeleted { get; set; }
        }

        public partial class TenantRowStore : Inquiry.Stores.InquiryStore<Demo.TenantRow>
        {
            [InquirySelectAll]
            public partial Task<IReadOnlyList<TenantRow>> SelectAllAsync(CancellationToken cancellationToken = default);

            [InquirySelectAll(IncludeDeleted = true)]
            public partial Task<IReadOnlyList<TenantRow>> SelectAllIncludingDeletedAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void GlobalFilterAndSoftDeleteBothCompose_Sqlite()
    {
        var result = RunGenerator(ActiveAndSoftDeleteEntity);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("TenantRowStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // The default select carries both terms (soft-delete first, then the global filter).
        Assert.Contains("WHERE \\\"IsDeleted\\\" = 0 AND \\\"IsActive\\\" = 1", text);
    }

    [Fact]
    public void IncludeDeletedDropsSoftDeleteButKeepsGlobalFilter_Sqlite()
    {
        var result = RunGenerator(ActiveAndSoftDeleteEntity);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("TenantRowStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // IncludeDeleted's per-method const drops the soft-delete term but the tenant filter survives:
        // the const ends right after "IsActive" = 1 with no leading "IsDeleted" term.
        Assert.Contains("FROM \\\"TTenantRow\\\" WHERE \\\"IsActive\\\" = 1\";", text);
    }

    [Fact]
    public void GlobalFilterComposesIntoProjection_Sqlite()
    {
        // A projection's column subset omits the global-filter column, but the active-row filter is
        // still composed into the projection SELECT (the column is passed explicitly for the predicate).
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TAccount")]
            public sealed class Account
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn("IsActive"), InquiryGlobalFilter]
                public bool IsActive { get; set; }
            }

            [InquiryProjection(typeof(Account))]
            public sealed record AccountName
            {
                [InquiryColumn("Name")]
                public string Name { get; init; } = string.Empty;
            }

            public partial class AccountStore : Inquiry.Stores.InquiryStore<Demo.Account>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<AccountName>> SelectNamesAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetAccountStore(result);

        Assert.Contains("SELECT \\\"Name\\\" FROM \\\"TAccount\\\" WHERE \\\"IsActive\\\" = 1", text);
    }

    [Fact]
    public void PostgreSqlUsesTrueLiteralForGlobalFilter()
    {
        var result = RunGenerator(AccountStore(AccountCrud), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetAccountStore(result);

        Assert.Contains("WHERE \\\"IsActive\\\" = TRUE", text);
    }

    [Fact]
    public void OracleComposesGlobalFilterUnquoted()
    {
        var result = RunGenerator(AccountStore(AccountCrud), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetAccountStore(result);

        Assert.Contains("FROM TAccount WHERE IsActive = 1", text);
        Assert.Contains("WHERE Id = :iq1$Idxxxx$30d4cf864d6e68 AND IsActive = 1", text);
    }

    [Theory]
    [InlineData("public bool? IsActive { get; set; }")]       // nullable bool — not allowed
    [InlineData("public int IsActive { get; set; }")]         // non-bool — not allowed
    public void GlobalFilterOnUnsupportedTypeReportsINQ059(string property)
    {
        var source = $$"""
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TBad")]
            public sealed class Bad
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("IsActive"), InquiryGlobalFilter]
                {{property}}
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ059");
    }

    [Fact]
    public void GlobalFilterOnKeyReportsINQ059()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("TBad")]
            public sealed class Bad
            {
                [InquiryKey, InquiryGlobalFilter]
                public bool Id { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ059");
    }

    // ---- Named filters + [InquiryIgnoreFilter] (#82 phase A) ----

    /// <summary>
    /// A named, bypassable publish gate alongside an UNNAMED (never bypassable) active flag, so every
    /// bypass assertion can also prove the unnamed filter survived — dropping too much is the failure
    /// mode that matters.
    /// </summary>
    private const string NamedFilterEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TPost")]
        public sealed class Post
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Title")]
            public string Title { get; set; } = string.Empty;

            [InquiryColumn("IsPublished"), InquiryGlobalFilter(Name = "PublishGate")]
            public bool IsPublished { get; set; }

            [InquiryColumn("IsActive"), InquiryGlobalFilter]
            public bool IsActive { get; set; }
        }
        """;

    private static string PostStoreSource(string methods) =>
        NamedFilterEntity + "\n\npublic partial class PostStore : Inquiry.Stores.InquiryStore<Demo.Post>\n{\n" + methods + "\n}\n";

    private static string GetNamedFilterPostStore(GeneratorTestResult result)
        => Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("PostStore.InquiryStore.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

    [Fact]
    public void IgnoreFilterDropsOnlyTheNamedFilterAndOnlyOnTheAnnotatedMethod_Sqlite()
    {
        var result = RunGenerator(PostStoreSource("""
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Post>> PublishedAsync(CancellationToken cancellationToken = default);

            [InquirySelectAll]
            [InquiryIgnoreFilter("PublishGate")]
            public partial Task<IReadOnlyList<Post>> IncludingDraftsAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetNamedFilterPostStore(result);

        // The unannotated method keeps BOTH filters through the shared const.
        Assert.Contains("_sqlSelectAll = \"SELECT \\\"Id\\\", \\\"Title\\\", \\\"IsPublished\\\", \\\"IsActive\\\" FROM \\\"TPost\\\" WHERE \\\"IsPublished\\\" = 1 AND \\\"IsActive\\\" = 1\";", text);
        // The bypass method gets its OWN const with the named gate dropped and the unnamed flag intact.
        Assert.Contains("_sqlSelectAll_IncludingDraftsAsync = \"SELECT \\\"Id\\\", \\\"Title\\\", \\\"IsPublished\\\", \\\"IsActive\\\" FROM \\\"TPost\\\" WHERE \\\"IsActive\\\" = 1\";", text);
    }

    [Fact]
    public void IgnoreFilterAppliesToKeyAndFieldSelects_Sqlite()
    {
        var result = RunGenerator(PostStoreSource("""
            [InquirySelectOneByKey]
            [InquiryIgnoreFilter("PublishGate")]
            public partial Task<Post?> AnyByKeyAsync(long id, CancellationToken cancellationToken = default);

            [InquirySelectAllByField("Title")]
            [InquiryIgnoreFilter("PublishGate")]
            public partial Task<IReadOnlyList<Post>> AnyByTitleAsync(string title, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetNamedFilterPostStore(result);

        Assert.Contains("WHERE \\\"Id\\\" = @Id AND \\\"IsActive\\\" = 1\";", text);
        Assert.Contains("WHERE \\\"Title\\\" = @Title AND \\\"IsActive\\\" = 1\";", text);
        Assert.DoesNotContain("\\\"IsPublished\\\" = 1 AND \\\"IsActive\\\" = 1\\\" WHERE \\\"Id\\\"", text);
    }

    [Fact]
    public void IgnoreFilterUnknownNameReportsINQ091()
    {
        var result = RunGenerator(PostStoreSource("""
            [InquirySelectAll]
            [InquiryIgnoreFilter("PublishGat")]
            public partial Task<IReadOnlyList<Post>> TypoAsync(CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ091" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void IgnoreFilterCannotBypassAnUnnamedFilterReportsINQ091()
    {
        // "IsActive" is the unnamed filter's COLUMN name — an unnamed filter has no name to match, and
        // guessing the column name must not become a bypass handle.
        var result = RunGenerator(PostStoreSource("""
            [InquirySelectAll]
            [InquiryIgnoreFilter("IsActive")]
            public partial Task<IReadOnlyList<Post>> SneakyAsync(CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ091" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void IgnoreFilterOnANonSelectOperationReportsINQ091()
    {
        var result = RunGenerator(PostStoreSource("""
            [InquiryInsert]
            [InquiryIgnoreFilter("PublishGate")]
            public partial Task<int> InsertAsync(Post post, CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ091" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void IgnoreFilterOnAnEagerSelectReportsINQ091()
    {
        // Eager relation consts are shared per relation, not per method — a bypass there would rewrite
        // every eager method's SQL, so v1 rejects it rather than half-applying it.
        var result = RunGenerator(PostStoreSource("""
            [InquirySelectAllEager]
            [InquiryIgnoreFilter("PublishGate")]
            public partial IAsyncEnumerable<Post> AllEagerAsync(CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ091" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void IgnoreFilterAppliesToCountAndAggregate_Sqlite()
    {
        // Count and Aggregate compose the active-row predicate like any select, so the semantic rule
        // ("bypassable wherever the filter is composed") covers them: Count switches to a per-method
        // const, Aggregate's already-per-method const is built from the bypass context.
        var result = RunGenerator(PostStoreSource("""
            [InquiryCount]
            [InquiryIgnoreFilter("PublishGate")]
            public partial Task<long> CountAllAsync(CancellationToken cancellationToken = default);

            [InquiryAggregate(InquiryAggregateFunction.Max, "Id")]
            [InquiryIgnoreFilter("PublishGate")]
            public partial Task<long> MaxIdAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetNamedFilterPostStore(result);

        Assert.Contains("_sqlCountFor_CountAllAsync = \"SELECT COUNT(*) FROM \\\"TPost\\\" WHERE \\\"IsActive\\\" = 1\";", text);
        Assert.Contains("_sqlAgg_MaxIdAsync = \"SELECT MAX(\\\"Id\\\") FROM \\\"TPost\\\" WHERE \\\"IsActive\\\" = 1\";", text);
        // The method body references the per-method count const, not the shared filtered one.
        Assert.Contains("_sqlCountFor_CountAllAsync,", text.Replace("\r", ""));
    }

    [Fact]
    public void IgnoreFilterNullNameReportsINQ091InsteadOfVanishing()
    {
        // [InquiryIgnoreFilter(null!)] compiles; it must surface as a build error, not a silently
        // dropped attribute that leaves the method reading as bypassed while returning filtered rows.
        var result = RunGenerator(PostStoreSource("""
            [InquirySelectAll]
            [InquiryIgnoreFilter(null!)]
            public partial Task<IReadOnlyList<Post>> NullNameAsync(CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ091" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void IgnoreFilterCacheKeyDistinguishesNamesContainingTheDelimiter_Sqlite()
    {
        // Filter names may contain any non-blank characters, including the cache key's own separator.
        // {"a|b"} and {"a","b"} must build DIFFERENT contexts — a bare join would collide them and
        // hand one method the other's SQL.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TTri")]
            public sealed class Tri
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("A"), InquiryGlobalFilter(Name = "a")]
                public bool A { get; set; }

                [InquiryColumn("B"), InquiryGlobalFilter(Name = "b")]
                public bool B { get; set; }

                [InquiryColumn("C"), InquiryGlobalFilter(Name = "a|b")]
                public bool C { get; set; }
            }

            public partial class TriStore : Inquiry.Stores.InquiryStore<Demo.Tri>
            {
                [InquirySelectAll]
                [InquiryIgnoreFilter("a|b")]
                public partial Task<IReadOnlyList<Tri>> WithoutCAsync(CancellationToken cancellationToken = default);

                [InquirySelectAll]
                [InquiryIgnoreFilter("a")]
                [InquiryIgnoreFilter("b")]
                public partial Task<IReadOnlyList<Tri>> WithoutAAndBAsync(CancellationToken cancellationToken = default);
            }
            """;
        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("TriStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains("_sqlSelectAll_WithoutCAsync = \"SELECT \\\"Id\\\", \\\"A\\\", \\\"B\\\", \\\"C\\\" FROM \\\"TTri\\\" WHERE \\\"A\\\" = 1 AND \\\"B\\\" = 1\";", text);
        Assert.Contains("_sqlSelectAll_WithoutAAndBAsync = \"SELECT \\\"Id\\\", \\\"A\\\", \\\"B\\\", \\\"C\\\" FROM \\\"TTri\\\" WHERE \\\"C\\\" = 1\";", text);
    }

    // ---- Runtime-parameterized filters (#82 phase B) ----

    /// <summary>
    /// A tenant column carrying <c>ContextKey</c> (non-bypassable: no Name) alongside an unnamed
    /// constant-bool filter, so every assertion can prove the two modes compose in one WHERE clause.
    /// </summary>
    private const string TenantEntity = """
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

            [InquiryColumn("TenantId"), InquiryGlobalFilter(ContextKey = "TenantId")]
            public long TenantId { get; set; }

            [InquiryColumn("IsActive"), InquiryGlobalFilter]
            public bool IsActive { get; set; }
        }
        """;

    private static string DocStoreSource(string methods) =>
        TenantEntity + "\n\npublic partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>\n{\n" + methods + "\n}\n";

    private static string GetTenantDocStore(GeneratorTestResult result)
        => Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

    [Fact]
    public void ParameterizedFilterComposesAParameterAndEmitsTheAmbientBinder_Sqlite()
    {
        var result = RunGenerator(DocStoreSource("""
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Doc>> AllAsync(CancellationToken cancellationToken = default);

            [InquirySelectOneByKey]
            public partial Task<Doc?> ByKeyAsync(long id, CancellationToken cancellationToken = default);

            [InquiryCount]
            public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        // The SQL is still a const; the tenant term compares to the ambient parameter and composes
        // with the constant-bool filter.
        Assert.Contains("_sqlSelectAll = \"SELECT \\\"Id\\\", \\\"TenantId\\\", \\\"IsActive\\\" FROM \\\"TDoc\\\" WHERE \\\"TenantId\\\" = @__gf_TenantId AND \\\"IsActive\\\" = 1\";", text);
        Assert.Contains("WHERE \\\"Id\\\" = @Id AND \\\"TenantId\\\" = @__gf_TenantId AND \\\"IsActive\\\" = 1\";", text);
        Assert.Contains("SELECT COUNT(*) FROM \\\"TDoc\\\" WHERE \\\"TenantId\\\" = @__gf_TenantId", text);

        // One shared helper binds the ambient value through the normal parameter machinery…
        Assert.Contains("private static void __BindGlobalFilters(global::System.Data.Common.DbCommand _cmd)", text);
        Assert.Contains("global::Inquiry.InquiryFilterContext.GetRequired<long>(\"TenantId\")", text);
        // …and every read binder calls it, including the previously no-op parameterless commands.
        Assert.Contains("static (_cmd, _) => { __BindGlobalFilters(_cmd); }", text);
        Assert.Contains("__BindGlobalFilters(_cmd);", text);
    }

    [Fact]
    public void ParameterizedFilterOnACompositeKeyComponentIsAllowed_Sqlite()
    {
        // A tenant id inside the composite key is the multi-tenant norm — the constant-bool "not a
        // key" restriction deliberately does not apply to ContextKey mode.
        var result = RunGenerator(DocStoreSource("""
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Doc>> AllAsync(CancellationToken cancellationToken = default);
            """).Replace("[InquiryColumn(\"TenantId\"), InquiryGlobalFilter(ContextKey = \"TenantId\")]", "[InquiryKey, InquiryGlobalFilter(ContextKey = \"TenantId\")]"));
        AssertNoErrors(result);

        Assert.Contains("\\\"TenantId\\\" = @__gf_TenantId", GetTenantDocStore(result));
    }

    [Fact]
    public void ParameterizedFilterConflictsAndInvalidShapesReportINQ093()
    {
        foreach (var mutation in new[]
        {
            // Explicit KeepWhen alongside ContextKey: the two predicate modes conflict.
            "[InquiryColumn(\"TenantId\"), InquiryGlobalFilter(ContextKey = \"TenantId\", KeepWhen = true)]",
            // Blank key.
            "[InquiryColumn(\"TenantId\"), InquiryGlobalFilter(ContextKey = \" \")]",
        })
        {
            var result = RunGenerator(DocStoreSource("""
                [InquirySelectAll]
                public partial Task<IReadOnlyList<Doc>> AllAsync(CancellationToken cancellationToken = default);
                """).Replace("[InquiryColumn(\"TenantId\"), InquiryGlobalFilter(ContextKey = \"TenantId\")]", mutation));

            Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ093" && d.Severity == DiagnosticSeverity.Error);
        }

        // Nullable column: a missing ambient value must fail loudly, not match NULL.
        var nullable = RunGenerator(DocStoreSource("""
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Doc>> AllAsync(CancellationToken cancellationToken = default);
            """).Replace("public long TenantId { get; set; }", "public long? TenantId { get; set; }"));
        Assert.Contains(nullable.RunResult.Diagnostics, d => d.Id == "INQ093" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ParameterizedFilterRejectsEagerMethodsOnRootAndRelatedEntities()
    {
        // Root entity has the ContextKey filter; the eager method must fail rather than emit SQL
        // whose @__gf_ parameter no grid binder fills.
        var result = RunGenerator(DocStoreSource("""
            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Doc> AllEagerAsync(CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ093" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ParameterizedNamedFilterBypassDropsTermAndParameterTogether_Sqlite()
    {
        // A NAMED parameterized filter is bypassable like any named filter; the bypass method's SQL
        // loses the term AND its binder loses the parameter (a reduced helper), while other methods
        // keep both.
        var source = DocStoreSource("""
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Doc>> AllAsync(CancellationToken cancellationToken = default);

            [InquirySelectAll]
            [InquiryIgnoreFilter("Tenant")]
            public partial Task<IReadOnlyList<Doc>> AllTenantsAsync(CancellationToken cancellationToken = default);
            """).Replace("InquiryGlobalFilter(ContextKey = \"TenantId\")", "InquiryGlobalFilter(ContextKey = \"TenantId\", Name = \"Tenant\")");
        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        Assert.Contains("_sqlSelectAll = \"SELECT \\\"Id\\\", \\\"TenantId\\\", \\\"IsActive\\\" FROM \\\"TDoc\\\" WHERE \\\"TenantId\\\" = @__gf_TenantId AND \\\"IsActive\\\" = 1\";", text);
        Assert.Contains("_sqlSelectAll_AllTenantsAsync = \"SELECT \\\"Id\\\", \\\"TenantId\\\", \\\"IsActive\\\" FROM \\\"TDoc\\\" WHERE \\\"IsActive\\\" = 1\";", text);
        // The bypass method's command uses a plain no-op binder — no reduced helper is needed when
        // the reduced set is empty, and binding the dropped parameter would error on strict providers.
        Assert.DoesNotContain("__BindGlobalFilters_AllTenantsAsync", text);
    }

    [Fact]
    public void ParameterizedFilterBinderCallIsPinnedOnEverySeam_Sqlite()
    {
        // One store spanning the distinct binder-emission seams; the exact occurrence counts are the
        // point — deleting the filter call from any one seam (predicate command, paged main command,
        // paged count, parameterless command, set-based update) changes a count and fails here, which
        // the collection-level live tests cannot do because each seam masks the others.
        var result = RunGenerator(DocStoreSource("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Title")]
            public partial Task<IReadOnlyList<Doc>> SearchAsync(string title, CancellationToken cancellationToken = default);

            [InquiryExists]
            [InquiryWhere("Title")]
            public partial Task<bool> ExistsAsync(string title, CancellationToken cancellationToken = default);

            [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
            public partial Task<global::Inquiry.Paging.InquiryPagedResult<Doc>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);

            [InquiryCount]
            public partial Task<long> CountAsync(CancellationToken cancellationToken = default);

            [InquiryUpdateWhere("Title")]
            [InquiryWhere("Id")]
            public partial Task<int> RetitleAsync(string title, long id, CancellationToken cancellationToken = default);
            """).Replace("public long TenantId { get; set; }", "public long TenantId { get; set; }\n\n    [InquiryColumn(\"Title\")]\n    public string Title { get; set; } = string.Empty;\n\n    [InquiryColumn(\"T2\"), InquiryGlobalFilter(ContextKey = \"T2\")]\n    public long T2 { get; set; }"));
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        // _c form: predicate select, exists, paged main command, update-where.
        Assert.Equal(4, CountOccurrences(text, "__BindGlobalFilters(_c);"));
        // _cmd form: the parameterless Count command and the zero-field paged-result count.
        Assert.Equal(2, CountOccurrences(text, "__BindGlobalFilters(_cmd); })"));
        // Exactly one helper body, binding BOTH parameterized filters.
        Assert.Equal(1, CountOccurrences(text, "private static void __BindGlobalFilters(global::System.Data.Common.DbCommand _cmd)"));
        Assert.Contains("GetRequired<long>(\"TenantId\")", text);
        Assert.Contains("GetRequired<long>(\"T2\")", text);
    }

    [Fact]
    public void ParameterizedFilterReducedBypassSetGetsItsOwnHelper_Sqlite()
    {
        // Two named parameterized filters; the method bypasses one. The reduced helper's name encodes
        // the ACTIVE set (length-prefixed property names), so overloads with different bypass sets can
        // never share a body, and the reduced body binds only the surviving filter.
        var source = DocStoreSource("""
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Doc>> AllAsync(CancellationToken cancellationToken = default);

            [InquirySelectAll]
            [InquiryIgnoreFilter("Region")]
            public partial Task<IReadOnlyList<Doc>> AllRegionsAsync(CancellationToken cancellationToken = default);
            """)
            .Replace("InquiryGlobalFilter(ContextKey = \"TenantId\")", "InquiryGlobalFilter(ContextKey = \"TenantId\", Name = \"Tenant\")")
            .Replace("[InquiryColumn(\"IsActive\"), InquiryGlobalFilter]\n    public bool IsActive { get; set; }", "[InquiryColumn(\"RegionId\"), InquiryGlobalFilter(ContextKey = \"RegionId\", Name = \"Region\")]\n    public long RegionId { get; set; }");
        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetTenantDocStore(result);

        // Unannotated method: full helper, both terms.
        Assert.Contains("WHERE \\\"TenantId\\\" = @__gf_TenantId AND \\\"RegionId\\\" = @__gf_RegionId\";", text);
        // Bypass method: Region term gone, Tenant term kept, bound by the set-named reduced helper.
        Assert.Contains("_sqlSelectAll_AllRegionsAsync = \"SELECT \\\"Id\\\", \\\"TenantId\\\", \\\"RegionId\\\" FROM \\\"TDoc\\\" WHERE \\\"TenantId\\\" = @__gf_TenantId\";", text);
        Assert.Contains("private static void __BindGlobalFilters_8_TenantId(global::System.Data.Common.DbCommand _cmd)", text);
        Assert.Contains("__BindGlobalFilters_8_TenantId(_cmd); })", text);
    }

    [Fact]
    public void GlobalFilterBlankNameReportsINQ092()
    {
        var result = RunGenerator(PostStoreSource("""
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Post>> AllAsync(CancellationToken cancellationToken = default);
            """).Replace("Name = \"PublishGate\"", "Name = \"  \""));

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ092" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void GlobalFilterDuplicateNameReportsINQ092()
    {
        var result = RunGenerator(PostStoreSource("""
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Post>> AllAsync(CancellationToken cancellationToken = default);
            """).Replace("[InquiryColumn(\"IsActive\"), InquiryGlobalFilter]", "[InquiryColumn(\"IsActive\"), InquiryGlobalFilter(Name = \"PublishGate\")]"));

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ092" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void GlobalFilterDuplicateNameIsStructurallyNonBypassable()
    {
        // INQ092 is suppressible; the enforcement must not be. Duplicated names are cleared from the
        // model, so a bypass naming the duplicate gets INQ091 (unknown name → method dropped) instead
        // of one attribute silently removing BOTH predicates.
        var result = RunGenerator(PostStoreSource("""
            [InquirySelectAll]
            [InquiryIgnoreFilter("PublishGate")]
            public partial Task<IReadOnlyList<Post>> BypassAsync(CancellationToken cancellationToken = default);
            """).Replace("[InquiryColumn(\"IsActive\"), InquiryGlobalFilter]", "[InquiryColumn(\"IsActive\"), InquiryGlobalFilter(Name = \"PublishGate\")]"));

        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ092");
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ091");
    }

    [Fact]
    public void IgnoreFilterCombinesWithIncludeDeletedAndKeepsUnbypassedTerms_Sqlite()
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

            [InquiryTable("TPost")]
            public sealed class Post
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("IsPublished"), InquiryGlobalFilter(Name = "PublishGate")]
                public bool IsPublished { get; set; }

                [InquiryColumn("IsActive"), InquiryGlobalFilter]
                public bool IsActive { get; set; }

                [InquiryColumn("IsDeleted"), InquirySoftDelete]
                public bool IsDeleted { get; set; }
            }

            public partial class PostStore : Inquiry.Stores.InquiryStore<Demo.Post>
            {
                [InquirySelectAll(IncludeDeleted = true)]
                [InquiryIgnoreFilter("PublishGate")]
                public partial Task<IReadOnlyList<Post>> EverythingButInactiveAsync(CancellationToken cancellationToken = default);
            }
            """;
        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetNamedFilterPostStore(result);

        // Soft delete dropped by IncludeDeleted, PublishGate dropped by name — the unnamed IsActive
        // filter is the only term left.
        Assert.Contains("_sqlSelectAll_EverythingButInactiveAsync = \"SELECT \\\"Id\\\", \\\"IsPublished\\\", \\\"IsActive\\\", \\\"IsDeleted\\\" FROM \\\"TPost\\\" WHERE \\\"IsActive\\\" = 1\";", text);
    }
}
