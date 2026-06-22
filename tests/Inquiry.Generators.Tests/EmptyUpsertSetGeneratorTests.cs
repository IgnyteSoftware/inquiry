using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// #47: a generated-key upsert on an entity with no updatable non-key columns (the SET clause resolves
/// to empty) must still emit syntactically valid SQL — DO NOTHING / a no-op self-assign / an omitted
/// WHEN MATCHED — instead of an empty <c>DO UPDATE SET </c> (and, on PostgreSQL, an empty <c>() SELECT</c>).
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    // Generated key + a created-at audit column: the audit column is inserted once but excluded from the
    // conflict/update set, so SetClauses is empty while InsertColumns is not.
    private const string LedgerSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Ledger")]
        public sealed class Ledger
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryCreatedAt]
            public DateTime CreatedAt { get; set; }
        }

        public partial class LedgerStore : Inquiry.Stores.InquiryStore<Demo.Ledger>
        {
            [InquiryUpsert]
            public partial Task<int> UpsertAsync(Ledger ledger, CancellationToken cancellationToken = default);
        }
        """;

    private static string LedgerUpsertSql(string dialect)
    {
        var result = RunGenerator(LedgerSource, dialect: dialect);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("LedgerStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void EmptySetUpsertEmitsDoNothingOnSqlite()
    {
        var text = LedgerUpsertSql("Sqlite");
        Assert.Contains("DO NOTHING", text);
        Assert.DoesNotContain("DO UPDATE SET \"", text); // no `DO UPDATE SET <col>` and no empty `DO UPDATE SET `
        Assert.DoesNotContain("DO UPDATE SET ;", text);
    }

    [Fact]
    public void EmptySetUpsertEmitsDoNothingOnPostgreSql()
    {
        var text = LedgerUpsertSql("PostgreSql");
        Assert.Contains("DO NOTHING", text);
        Assert.DoesNotContain("DO UPDATE SET ;", text);
        Assert.DoesNotContain("DO UPDATE SET  ", text);
    }

    [Fact]
    public void EmptySetUpsertOmitsWhenMatchedOnSqlServer()
    {
        var text = LedgerUpsertSql("SqlServer");
        Assert.DoesNotContain("WHEN MATCHED", text);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", text);
    }

    [Fact]
    public void EmptySetUpsertEmitsKeySelfAssignNoOpOnMySql()
    {
        var text = LedgerUpsertSql("MySql");
        // ON DUPLICATE KEY UPDATE requires an assignment; a key-only update set self-assigns the key.
        Assert.Contains("ON DUPLICATE KEY UPDATE `Id` = `Id`", text);
        Assert.DoesNotContain("ON DUPLICATE KEY UPDATE ;", text);
    }

    // Oracle's generated-key upsert is unsupported (throws), so the empty-SET defect lives on the
    // CLIENT-key MERGE path. A client key plus a created-at column yields an empty SET clause.
    private const string OracleClientKeyLedgerSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("OracleLedger")]
        public sealed class OracleLedger
        {
            [InquiryKey]
            public int Code { get; set; }

            [InquiryCreatedAt]
            public DateTime CreatedAt { get; set; }
        }

        public partial class OracleLedgerStore : Inquiry.Stores.InquiryStore<Demo.OracleLedger>
        {
            [InquiryUpsert]
            public partial Task<int> UpsertAsync(OracleLedger ledger, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void EmptySetUpsertOmitsWhenMatchedOnOracle()
    {
        var result = RunGenerator(OracleClientKeyLedgerSource, dialect: "Oracle");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("OracleLedgerStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();
        Assert.DoesNotContain("WHEN MATCHED", text);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", text);
    }

    // A truly key-only entity additionally has an empty INSERT column list, which broke PostgreSQL's
    // generated-key insert (`() SELECT`). The null-key generate branch is dropped (it is unreachable —
    // a null key routes to the plain insert) leaving a valid explicit-key INSERT ... DO NOTHING.
    private const string KeyOnlySource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Tag")]
        public sealed class Tag
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }
        }

        public partial class TagStore : Inquiry.Stores.InquiryStore<Demo.Tag>
        {
            [InquiryUpsert]
            public partial Task<int> UpsertAsync(Tag tag, CancellationToken cancellationToken = default);

            [InquiryUpsert(ReturnEntity = true)]
            public partial Task<Tag?> UpsertReturningAsync(Tag tag, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void KeyOnlyEntityUpsertEmitsValidSqlOnPostgreSql()
    {
        var result = RunGenerator(KeyOnlySource, dialect: "PostgreSql");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("TagStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // No empty column list (`() SELECT`) and no empty DO UPDATE SET; the conflict is a no-op.
        Assert.DoesNotContain("() SELECT", text);
        Assert.DoesNotContain("DO UPDATE SET", text);
        Assert.Contains("DO NOTHING", text);
    }
}
