using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// #47: a generated-key upsert on an entity with no updatable non-key columns (the SET clause resolves
/// to empty) must still emit syntactically valid SQL — DO NOTHING / a no-op self-assign / IF NOT EXISTS —
/// instead of an empty <c>DO UPDATE SET </c> (and, on PostgreSQL, an empty <c>() SELECT</c>).
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

            [InquiryUpsert(ReturnEntity = true)]
            public partial Task<Ledger?> UpsertReturningAsync(Ledger ledger, CancellationToken cancellationToken = default);
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
    public void EmptySetUpsertEmitsIfNotExistsOnSqlServer()
    {
        var text = LedgerUpsertSql("SqlServer");
        Assert.DoesNotContain("MERGE", text);
        Assert.Contains("IF NOT EXISTS", text);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void EmptySetGeneratedKeyUpsertAssignsLastInsertIdOnceOnMySql(string dialect)
    {
        var text = LedgerUpsertSql(dialect);
        // The LAST_INSERT_ID assignment is both the required non-empty update and the returning-key
        // capture. Do not prepend a redundant key self-assignment for this key-only shape.
        Assert.Contains("ON DUPLICATE KEY UPDATE `Id` = LAST_INSERT_ID(`Id`)", text);
        Assert.DoesNotContain("`Id` = `Id`, `Id` = LAST_INSERT_ID(`Id`)", text);
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

    [Fact]
    public void SqlServerKeyOnlyGeneratedUpsertsUseAmbientSafeGuardedIdentityStateMachine()
    {
        var result = RunGenerator(KeyOnlySource, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("TagStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        var nonReturning = SqlConstant(text, "_sqlUpsert");
        var returning = SqlConstant(text, "_sqlUpsertReturning");

        AssertStateMachineOrdering(nonReturning, returning: false);
        AssertStateMachineOrdering(returning, returning: true);
        Assert.Contains("INSERT INTO @_out ([Id]) SELECT [Id] FROM [Tag] WITH (UPDLOCK, SERIALIZABLE) WHERE [Id] = @Id; IF @@ROWCOUNT = 0", returning);
        Assert.Contains("OUTPUT INSERTED.[Id] INTO @_out ([Id])", returning);
        Assert.DoesNotContain("IF NOT EXISTS", returning);
    }

    [Fact]
    public void SqlServerGeneratedEmptySetReturningProjectionUsesIdenticalExplicitColumnOrder()
    {
        var text = LedgerUpsertSql("SqlServer");
        var returning = SqlConstant(text, "_sqlUpsertReturning");

        Assert.Contains("DECLARE @_out TABLE ([Id] BIGINT, [CreatedAt] DATETIME2(7));", text);
        Assert.Contains("INSERT INTO @_out ([Id], [CreatedAt]) SELECT [Id], [CreatedAt] FROM [Ledger] WITH (UPDLOCK, SERIALIZABLE)", returning);
        Assert.Contains("OUTPUT INSERTED.[Id], INSERTED.[CreatedAt] INTO @_out ([Id], [CreatedAt])", returning);
        Assert.Contains("SELECT [Id], [CreatedAt] FROM @_out", returning);
        AssertStateMachineOrdering(SqlConstant(text, "_sqlUpsert"), returning: false);
        AssertStateMachineOrdering(returning, returning: true);
    }

    [Fact]
    public void SqlServerGeneratedKeyUpsertWithWritableColumnsRemainsUpdateFirst()
    {
        const string source = """
            using System.Threading; using System.Threading.Tasks;
            using Inquiry; using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("Widget")]
            public sealed class Widget
            {
                [InquiryKey(IsGenerated = true)] public int? Id { get; set; }
                [InquiryColumn] public string Name { get; set; } = "";
            }
            public partial class WidgetStore : InquiryStore<Widget>
            {
                [InquiryUpsert(ReturnEntity = true)] public partial Task<Widget?> UpsertAsync(Widget item, CancellationToken ct = default);
            }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        var method = SqlConstant(text, "_sqlUpsertReturning");
        Assert.True(method.IndexOf("UPDATE [Widget] WITH (UPDLOCK, SERIALIZABLE)", StringComparison.Ordinal)
            < method.IndexOf("IF @@ROWCOUNT = 0 INSERT INTO [Widget]", StringComparison.Ordinal));
        Assert.DoesNotContain("INSERT INTO @_out ([Id], [Name]) SELECT", method);
    }

    private static void AssertStateMachineOrdering(string method, bool returning)
    {
        var savepointFlag = method.IndexOf("DECLARE @_inquiry_savepoint_created bit = 0", StringComparison.Ordinal);
        var setup = method.IndexOf("IF @@TRANCOUNT = 0", StringComparison.Ordinal);
        var savepoint = method.IndexOf("SAVE TRANSACTION @_inquiry_savepoint", StringComparison.Ordinal);
        var savepointCreated = method.IndexOf("SET @_inquiry_savepoint_created = 1", StringComparison.Ordinal);
        var lockStep = method.IndexOf(returning ? "INSERT INTO @_out" : "IF NOT EXISTS", StringComparison.Ordinal);
        var missing = method.IndexOf(returning ? "IF @@ROWCOUNT = 0" : "BEGIN SET IDENTITY_INSERT", StringComparison.Ordinal);
        var identityOn = method.IndexOf("SET IDENTITY_INSERT", StringComparison.Ordinal);
        var insert = method.IndexOf("INSERT INTO [", identityOn, StringComparison.Ordinal);
        var identityOff = method.IndexOf("SET IDENTITY_INSERT", identityOn + 1, StringComparison.Ordinal);
        var commit = method.IndexOf("IF @_inquiry_started_transaction = 1 COMMIT TRANSACTION", StringComparison.Ordinal);
        var catchBlock = method.IndexOf("BEGIN CATCH", StringComparison.Ordinal);
        var ownedRollback = method.IndexOf("IF @_inquiry_started_transaction = 1 BEGIN IF XACT_STATE() <> 0 ROLLBACK TRANSACTION", StringComparison.Ordinal);
        var savepointRollback = method.IndexOf("ELSE IF @_inquiry_savepoint_created = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION @_inquiry_savepoint", StringComparison.Ordinal);
        var rethrow = method.IndexOf("THROW", StringComparison.Ordinal);

        Assert.True(savepointFlag >= 0 && savepointFlag < setup && setup < savepoint && savepoint < savepointCreated && savepointCreated < lockStep && lockStep < missing);
        Assert.True(missing <= identityOn && identityOn < insert && insert < identityOff && identityOff < commit);
        Assert.True(commit < catchBlock && catchBlock < ownedRollback && ownedRollback < savepointRollback && savepointRollback < rethrow);
        Assert.Equal(1, CountOccurrences(method, "COMMIT TRANSACTION"));
        Assert.Equal(1, CountOccurrences(method, "ROLLBACK TRANSACTION;"));
        Assert.Equal(1, CountOccurrences(method, "ROLLBACK TRANSACTION @_inquiry_savepoint"));
        Assert.DoesNotContain("ELSE IF XACT_STATE() = 1", method);
        Assert.DoesNotContain("ELSE COMMIT TRANSACTION", method);
        Assert.DoesNotContain("ELSE ROLLBACK TRANSACTION", method);
        Assert.DoesNotContain("COMMIT TRANSACTION; SELECT", method);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
        {
            count++;
        }

        return count;
    }

    private static string SqlConstant(string generated, string name)
    {
        var marker = "private const string " + name + " = \"";
        var start = generated.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Generated SQL constant {name} was not found.");
        var end = generated.IndexOf("\";", start + marker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Generated SQL constant {name} had no terminator.");
        return generated[start..(end + 2)];
    }
}
