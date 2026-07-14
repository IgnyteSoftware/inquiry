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
}
