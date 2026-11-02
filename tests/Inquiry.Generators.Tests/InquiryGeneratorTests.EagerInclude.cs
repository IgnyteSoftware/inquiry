using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// #496: <c>[InquiryInclude]</c> limits an eager method to the named relations. #424:
/// <c>[InquirySelectAllEager]</c> also accepts a buffered <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> return.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private static string IncludePostSource(string extraPostMembers, string storeMembers) => $$"""
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Author")]
        public sealed class Author
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public string Name { get; set; } = string.Empty;
        }

        [InquiryTable("Tag")]
        public sealed class Tag
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public int PostId { get; set; }
            [InquiryColumn] public string Label { get; set; } = string.Empty;
        }

        [InquiryTable("Note")]
        public sealed class Note
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public int PostId { get; set; }
        }

        [InquiryTable("Post")]
        public sealed class Post
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public int AuthorId { get; set; }

            [InquiryRelation(nameof(AuthorId))]
            public Author? Author { get; set; }

            [InquiryRelation(nameof(Tag.PostId))]
            public IReadOnlyList<Tag> Tags { get; set; } = new List<Tag>();
        {{extraPostMembers}}
        }

        public partial class PostStore : InquiryStore<Post>
        {
        {{storeMembers}}
        }
        """;

    private const string SelectiveStoreMembers = """
            [InquirySelectOneByKeyEager]
            [InquiryInclude(nameof(Post.Author))]
            public partial Task<Post?> GetWithAuthorAsync(int id, CancellationToken ct = default);

            [InquirySelectAllEager]
            [InquiryInclude(nameof(Post.Tags))]
            public partial IAsyncEnumerable<Post> SelectAllWithTagsAsync(CancellationToken ct = default);
        """;

    private const string UnrelatedNoteRelation = """
            [InquiryRelation(nameof(Note.PostId))]
            public IReadOnlyList<Note> Notes { get; set; } = new List<Note>();
        """;

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void Include_BatchesOnlyTheNamedRelations(string dialect)
    {
        var result = RunGenerator(IncludePostSource(UnrelatedNoteRelation, SelectiveStoreMembers), dialect: dialect);
        AssertCompiles(result);
        var text = GetPostStoreText(result);

        if (dialect == "Oracle")
        {
            Assert.Contains("var _sql = \"DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR \" + _sqlSelectByKey + \"; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR \" + _sql_Author_ByKey + \"; DBMS_SQL.RETURN_RESULT(c); END;\";", text);
            Assert.Contains("var _sql = \"DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR \" + _sql_Tags_All + \"; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR \" + _sqlSelectAll + \"; DBMS_SQL.RETURN_RESULT(c); END;\";", text);
        }
        else
        {
            Assert.Contains("var _sql = _sqlSelectByKey + \";\" + _sql_Author_ByKey;", text);
            Assert.Contains("var _sql = _sql_Tags_All + \";\" + _sqlSelectAll;", text);
        }

        // The key lookup binds only the parent key: the collection's foreign-key parameter is gone.
        Assert.DoesNotContain("_p1.ParameterName", text);

        // No method loads Notes, so the store has no Notes SQL and never stitches it.
        Assert.DoesNotContain("_sql_Notes", text);
        Assert.DoesNotContain(".Notes =", text);
        Assert.DoesNotContain("Note\"", text);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void Include_UnrelatedRelationDoesNotChangeTheGeneratedStore(string dialect)
    {
        var without = RunGenerator(IncludePostSource(string.Empty, SelectiveStoreMembers), dialect: dialect);
        var with = RunGenerator(IncludePostSource(UnrelatedNoteRelation, SelectiveStoreMembers), dialect: dialect);
        AssertCompiles(without);
        AssertCompiles(with);

        Assert.Equal(GetPostStoreText(without), GetPostStoreText(with));
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void Include_NamingEveryRelationMatchesTheDefaultCommand(string dialect)
    {
        var result = RunGenerator(IncludePostSource(string.Empty, """
                [InquirySelectAllEager]
                public partial IAsyncEnumerable<Post> SelectAllAsync(CancellationToken ct = default);

                [InquirySelectAllEager]
                [InquiryInclude(nameof(Post.Tags))]
                [InquiryInclude(nameof(Post.Author))]
                public partial IAsyncEnumerable<Post> SelectAllNamedAsync(CancellationToken ct = default);
            """), dialect: dialect);
        AssertCompiles(result);
        var text = GetPostStoreText(result);

        // Relations keep the entity's declaration order, not the attribute order.
        Assert.Equal(GridCommandLine(GetMethodBody(text, "SelectAllAsync")), GridCommandLine(GetMethodBody(text, "SelectAllNamedAsync")));
        Assert.Contains("_sql_Author_All", GridCommandLine(GetMethodBody(text, "SelectAllAsync")));
    }

    [Theory]
    [InlineData("[InquirySelectAllEager]", "[InquiryInclude(\"Tag\")]", "Tag")]
    [InlineData("[InquirySelectAllEager]", "[InquiryInclude(nameof(Post.AuthorId))]", "AuthorId")]
    [InlineData("[InquirySelectAllEager]", "[InquiryInclude(nameof(Post.Tags))] [InquiryInclude(nameof(Post.Tags))]", "Tags")]
    [InlineData("[InquirySelectAll]", "[InquiryInclude(nameof(Post.Tags))]", "Tags")]
    public void Include_InvalidSelectionReportsINQ107NamingTheProperty(string operation, string include, string named)
    {
        var returnType = operation == "[InquirySelectAll]" ? "Task<IReadOnlyList<Post>>" : "IAsyncEnumerable<Post>";
        var result = RunGenerator(IncludePostSource(string.Empty, $"""
                {operation}
                {include}
                public partial {returnType} BadAsync(CancellationToken ct = default);
            """));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static d => d.Id == "INQ107");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"[InquiryInclude(\"{named}\")]", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void SelectAllEager_BufferedListUsesTheStreamedCommand(string dialect)
    {
        var result = RunGenerator(IncludePostSource(string.Empty, """
                [InquirySelectAllEager]
                public partial IAsyncEnumerable<Post> StreamAsync(CancellationToken ct = default);

                [InquirySelectAllEager]
                public partial Task<IReadOnlyList<Post>> ListAsync(CancellationToken ct = default);
            """), dialect: dialect);
        AssertCompiles(result);
        var text = GetPostStoreText(result);

        var streamed = GetMethodBody(text, "StreamAsync");
        var buffered = GetMethodBody(text, "ListAsync");
        Assert.Equal(GridCommandLine(streamed), GridCommandLine(buffered));

        // The buffered method reads the same grid, then returns a list instead of yielding.
        Assert.Contains("await using var _grid", buffered);
        Assert.Contains("_result.Add(_entity);", buffered);
        Assert.Contains("return _result;", buffered);
        Assert.DoesNotContain("yield", buffered);
        Assert.DoesNotContain("EnumeratorCancellation", buffered);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("Oracle")]
    public void SelectAllEager_BufferedListCompilesOnTheSeparateQueryPath(string dialect)
    {
        var result = RunGenerator(NoRelationEagerSource.Replace(
            "public partial IAsyncEnumerable<Region> SelectAllEagerAsync(",
            "public partial System.Threading.Tasks.Task<IReadOnlyList<Region>> SelectAllEagerAsync(",
            StringComparison.Ordinal), dialect: dialect);
        AssertCompiles(result);

        var text = GetRegionStoreText(result);
        Assert.Contains("return _entities;", text);
        Assert.DoesNotContain("yield", text);
    }

    private static void AssertCompiles(GeneratorTestResult result)
    {
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    private static string GetMethodBody(string storeText, string methodName)
    {
        var start = storeText.IndexOf(" " + methodName + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Generated store has no method {methodName}.");
        var end = storeText.IndexOf("\n    }", start, StringComparison.Ordinal);
        return storeText.Substring(start, end - start);
    }

    private static string GridCommandLine(string methodBody)
        => Assert.Single(methodBody.Split('\n'), static line => line.TrimStart().StartsWith("var _sql = ", StringComparison.Ordinal)).Trim();
}
