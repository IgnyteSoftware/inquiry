using System;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string LockModeEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TProduct")]
        public sealed class Product
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;
        }
        """;

    private static string LockModeStore(string methods) =>
        LockModeEntity + "\n\npublic partial class LockModeStore : Inquiry.Stores.InquiryStore<Demo.Product>\n{\n" + methods + "\n}\n";

    private static string GetLockModeStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("LockModeStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void LockMode_SelectByKeyForUpdate_PostgreSql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE", text);
        Assert.Contains("_sqlSelectByKey_GetForUpdateAsync", text);
    }

    [Fact]
    public void LockMode_SelectAllForUpdate_PostgreSql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectAll(LockMode = InquiryLockMode.Update)]
            public partial IAsyncEnumerable<Product> SelectForUpdateAsync(CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE", text);
        Assert.Contains("_sqlSelectAll_SelectForUpdateAsync", text);
    }

    [Fact]
    public void LockMode_SelectByFieldForShare_PostgreSql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectAllByField("Name", LockMode = InquiryLockMode.Share)]
            public partial Task<IReadOnlyList<Product>> SelectByNameForShareAsync(string name, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR SHARE", text);
    }

    [Fact]
    public void LockMode_SelectByPredicateForUpdateNoWait_PostgreSql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectAllByPredicate(LockMode = InquiryLockMode.UpdateNoWait)]
            [InquiryWhere("Name")]
            public partial Task<IReadOnlyList<Product>> SearchForUpdateAsync(string name, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE NOWAIT", text);
    }

    [Fact]
    public void LockMode_UpdateSkipLocked_PostgreSql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.UpdateSkipLocked)]
            public partial Task<Product?> GetSkipLockedAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE SKIP LOCKED", text);
    }

    [Fact]
    public void LockMode_SelectByKeyForUpdate_SqlServer()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("WITH (UPDLOCK, ROWLOCK)", text);
        Assert.DoesNotContain("FOR UPDATE", text);
    }

    [Fact]
    public void LockMode_Share_SqlServer()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Share)]
            public partial Task<Product?> GetForShareAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("WITH (HOLDLOCK, ROWLOCK)", text);
    }

    [Fact]
    public void LockMode_UpdateNoWait_SqlServer()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.UpdateNoWait)]
            public partial Task<Product?> GetNoWaitAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("WITH (UPDLOCK, ROWLOCK, NOWAIT)", text);
    }

    [Fact]
    public void LockMode_UpdateSkipLocked_SqlServer()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.UpdateSkipLocked)]
            public partial Task<Product?> GetSkipLockedAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("WITH (UPDLOCK, ROWLOCK, READPAST)", text);
    }

    [Fact]
    public void LockMode_Sqlite_ReportsINQ039()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
    }

    [Fact]
    public void LockMode_ForUpdate_MySql()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "MySql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE", text);
    }

    [Fact]
    public void LockMode_ForShare_MariaDb_EmitsLockInShareMode()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Share)]
            public partial Task<Product?> GetForShareAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "MariaDb");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("LOCK IN SHARE MODE", text);
        Assert.DoesNotContain("FOR SHARE", text);
    }

    [Fact]
    public void LockMode_ForUpdate_Oracle()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("FOR UPDATE", text);
    }

    [Fact]
    public void LockMode_ForShare_Oracle_ReportsINQ039()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Share)]
            public partial Task<Product?> GetForShareAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "Oracle");
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ039");
    }

    [Fact]
    public void LockMode_None_DoesNotEmitLockClause()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey]
            public partial Task<Product?> GetAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.DoesNotContain("FOR UPDATE", text);
        Assert.DoesNotContain("FOR SHARE", text);
    }

    [Fact]
    public void LockMode_SqlServerTableHintInsertedAfterTable()
    {
        var result = RunGenerator(LockModeStore("""
            [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
            public partial Task<Product?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetLockModeStore(result);

        Assert.Contains("[TProduct] WITH (UPDLOCK, ROWLOCK) WHERE", text);
    }
}
