using System;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Optimistic-concurrency emission tests: an ORM-managed numeric token makes generated UPDATE
/// add <c>SET …, "Version" = "Version" + 1</c> and AND-compose <c>"Version" = @Version</c> onto the
/// key WHERE, DELETE AND-composes the same predicate, RETURNING/OUTPUT keeps projecting the token, a
/// database-managed token (SqlServer rowversion) is absent from INSERT but present in the UPDATE
/// WHERE + OUTPUT, the conflict-throw branch is emitted only for token entities, and the diagnostics
/// (INQ028 dup, INQ029 token==key, DB-managed-on-unsupported-dialect, upsert+token) fire.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string TokenEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TWidget")]
        public sealed class Widget
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            [InquiryConcurrencyToken]
            public int Version { get; set; }
        }
        """;

    private static string TokenStore(string methods) =>
        TokenEntity + "\n\npublic partial class WidgetStore : Inquiry.Stores.InquiryStore<Demo.Widget>\n{\n" + methods + "\n}\n";

    private static string GetTokenStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    private const string TokenCrud = """
        [InquiryUpdate]
        public partial Task<bool> UpdateAsync(Widget widget, CancellationToken cancellationToken = default);

        [InquiryDeleteOneByKey]
        public partial Task<bool> DeleteAsync(Widget widget, CancellationToken cancellationToken = default);
        """;

    [Fact]
    public void OrmTokenBumpsVersionAndComposesPredicate_Sqlite()
    {
        var result = RunGenerator(TokenStore(TokenCrud));
        AssertNoErrors(result);
        var text = GetTokenStore(result);

        Assert.Contains("_sqlUpdate = \"UPDATE \\\"TWidget\\\" SET \\\"Name\\\" = @Name, \\\"Version\\\" = \\\"Version\\\" + 1 WHERE \\\"Id\\\" = @Id AND \\\"Version\\\" = @Version\";", text);
        Assert.Contains("_sqlDeleteByKey = \"DELETE FROM \\\"TWidget\\\" WHERE \\\"Id\\\" = @Id AND \\\"Version\\\" = @Version\";", text);
        // The concurrency DELETE takes the entity and binds BOTH the key and the token, otherwise
        // @Version in the DELETE WHERE would be unbound at runtime.
        Assert.Contains("_p0.ParameterName = \"@Id\";", text);
        Assert.Contains("_p1.ParameterName = \"@Version\";", text);
    }

    [Fact]
    public void OrmTokenUpdateReturningKeepsTokenProjected_Sqlite()
    {
        var result = RunGenerator(TokenStore("""
            [InquiryUpdate(ReturnEntity = true)]
            public partial Task<Widget?> UpdateAsync(Widget widget, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetTokenStore(result);

        // RETURNING projects all columns, so the incremented version flows back free.
        Assert.Contains("_sqlUpdateReturning = \"UPDATE \\\"TWidget\\\" SET \\\"Name\\\" = @Name, \\\"Version\\\" = \\\"Version\\\" + 1 WHERE \\\"Id\\\" = @Id AND \\\"Version\\\" = @Version RETURNING \\\"Id\\\", \\\"Name\\\", \\\"Version\\\"\";", text);
    }

    [Fact]
    public void ConflictThrowBranchEmittedForTokenEntity_Sqlite()
    {
        var result = RunGenerator(TokenStore(TokenCrud));
        AssertNoErrors(result);
        var text = GetTokenStore(result);

        // The non-returning UPDATE/DELETE gate a throw on the runtime option, gated on a 0-row result.
        Assert.Contains("if (_rows == 0 && Inquiry.ThrowOnConcurrencyConflict) throw new global::Inquiry.InquiryConcurrencyException();", text);
    }

    [Fact]
    public void NonTokenEntityEmitsNoThrowBranch_Sqlite()
    {
        // Regression: an entity without a token emits byte-identical mutation code (no throw, no _rows).
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TPlain")]
            public sealed class Plain
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class PlainStore : Inquiry.Stores.InquiryStore<Demo.Plain>
            {
                [InquiryUpdate]
                public partial Task<bool> UpdateAsync(Plain plain, CancellationToken cancellationToken = default);

                [InquiryDeleteOneByKey]
                public partial Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("PlainStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.DoesNotContain("InquiryConcurrencyException", text);
        Assert.DoesNotContain("ThrowOnConcurrencyConflict", text);
        Assert.Contains("_sqlUpdate = \"UPDATE \\\"TPlain\\\" SET \\\"Name\\\" = @Name WHERE \\\"Id\\\" = @Id\";", text);
    }

    [Fact]
    public void DatabaseGeneratedTokenAbsentFromInsertPresentInUpdate_SqlServer()
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

            [InquiryTable("TDoc")]
            public sealed class Doc
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn("RowVer"), InquiryConcurrencyToken(DatabaseGenerated = true)]
                public byte[] RowVer { get; set; } = System.Array.Empty<byte>();
            }

            public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
            {
                [InquiryInsert]
                public partial Task<int> InsertAsync(Doc doc, CancellationToken cancellationToken = default);

                [InquiryUpdate(ReturnEntity = true)]
                public partial Task<Doc?> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // INSERT omits the rowversion (database supplies it); no version bump in SET (DB advances it);
        // WHERE still composes the token; OUTPUT returns the new value.
        // The rowversion is absent from INSERT (database supplies it); the key is client-supplied here
        // so it is insertable, but [RowVer] is not (and is not bound in the INSERT binder).
        Assert.Contains("_sqlInsert = \"INSERT INTO [TDoc] ([Id], [Name]) VALUES (@Id, @Name)\";", text);
        // No version bump in SET (DB advances it); WHERE composes the token; OUTPUT returns the new value.
        Assert.Contains("_sqlUpdateReturning = \"UPDATE [TDoc] SET [Name] = @Name OUTPUT INSERTED.[Id], INSERTED.[Name], INSERTED.[RowVer] WHERE [Id] = @Id AND [RowVer] = @RowVer\";", text);
        // The rowversion IS bound for the UPDATE (its WHERE compares the original value) but never SET.
        Assert.Contains("_p2.ParameterName = \"@RowVer\";", text);
    }

    [Fact]
    public void MultipleTokensReportsInq028()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TBad")]
            public sealed class Bad
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryConcurrencyToken]
                public int V1 { get; set; }

                [InquiryConcurrencyToken]
                public int V2 { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ028");
    }

    [Fact]
    public void TokenOnKeyReportsInq029()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TBad")]
            public sealed class Bad
            {
                [InquiryKey, InquiryConcurrencyToken]
                public long Id { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ029");
    }

    [Fact]
    public void DatabaseGeneratedTokenOnSqliteIsRejected()
    {
        const string source = """
            using System;
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

                [InquiryColumn("RowVer"), InquiryConcurrencyToken(DatabaseGenerated = true)]
                public byte[] RowVer { get; set; } = System.Array.Empty<byte>();
            }

            public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
            {
                [InquiryUpdate]
                public partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ006");
    }

    [Fact]
    public void UpsertOnTokenEntityIsRejected()
    {
        var result = RunGenerator(TokenStore("""
            [InquiryUpsert]
            public partial Task<int> UpsertAsync(Widget widget, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ006");
    }

    [Fact]
    public void BatchUpdateOnTokenEntityIsRejected()
    {
        var result = RunGenerator(TokenStore("""
            [InquiryUpdateAll]
            public partial Task<int> UpdateAllAsync(IEnumerable<Widget> widgets, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ022");
    }

    [Fact]
    public void BatchDeleteOnTokenEntityIsRejected()
    {
        var result = RunGenerator(TokenStore("""
            [InquiryDeleteAll]
            public partial Task<int> DeleteAllAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ022");
    }

    [Fact]
    public void OrmTokenComposesAcrossDialects()
    {
        var pg = GetTokenStore(RunGenerator(TokenStore(TokenCrud), dialect: "PostgreSql"));
        Assert.Contains("SET \\\"Name\\\" = @Name, \\\"Version\\\" = \\\"Version\\\" + 1 WHERE \\\"Id\\\" = @Id AND \\\"Version\\\" = @Version", pg);
        Assert.Contains("DELETE FROM \\\"TWidget\\\" WHERE \\\"Id\\\" = @Id AND \\\"Version\\\" = @Version", pg);

        var mysql = GetTokenStore(RunGenerator(TokenStore(TokenCrud), dialect: "MySql"));
        Assert.Contains("SET `Name` = @Name, `Version` = `Version` + 1 WHERE `Id` = @Id AND `Version` = @Version", mysql);
        Assert.Contains("DELETE FROM `TWidget` WHERE `Id` = @Id AND `Version` = @Version", mysql);

        var oracle = GetTokenStore(RunGenerator(TokenStore(TokenCrud), dialect: "Oracle"));
        Assert.Contains("SET Name = :Name, Version = Version + 1 WHERE Id = :Id AND Version = :Version", oracle);
        Assert.Contains("DELETE FROM TWidget WHERE Id = :Id AND Version = :Version", oracle);
    }
}
