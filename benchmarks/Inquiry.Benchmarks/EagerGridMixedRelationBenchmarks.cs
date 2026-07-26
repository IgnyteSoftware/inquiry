using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Inquiry.Benchmarks;

/// <summary>
/// The 1-parent + 2-relations eager shape (#70): <c>MixedBenchPost</c> carries both a to-one
/// <c>Author</c> reference and a to-many <c>Tags</c> collection, so Inquiry batches three SELECTs into a
/// single grid command. Density is parameterized the same way as <see cref="EagerGridBenchmarks"/>:
/// <list type="bullet">
///   <item>Sparse (<c>PostCount=100</c>): many parents, few tags each.</item>
///   <item>Dense (<c>PostCount=4</c>): few parents, many tags each.</item>
/// </list>
/// ADO.NET and Dapper run three separate queries and stitch in memory — the separate-query alternative,
/// matching how the other eager benchmark classes frame their baselines. EF Core is omitted for the same
/// reason as <see cref="EagerLoadingBenchmarks"/>: <c>Include</c> is a JOIN or split query, not a
/// like-for-like stitch.
/// </summary>
/// <remarks>
/// Deliberately a separate class rather than extra <c>[Params]</c> on <see cref="EagerGridBenchmarks"/>.
/// The #87 regression gate matches baseline cases by exact <c>FullName</c> including parameters, and a
/// miss is reported as Skip rather than Fail — so adding a parameter there would silently rename all
/// twelve committed cases and disable that class's gate with no failure signal.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EagerGridMixedRelationBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;

    /// <summary>Total tag (child collection) rows.</summary>
    [Params(1000, 100000)] public int Rows;

    /// <summary>Parent count: 4 is dense (Rows/4 tags each), 100 is sparse.</summary>
    [Params(4, 100)] public int PostCount;

    private const int AuthorCount = 8;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // CreateAsync(0): this class queries only the MixedBench* tables, so seeding Rows Customers +
        // Products + Shippers would be ~300k inserts of setup cost that no leg ever reads.
        _db = BenchmarkDatabase.CreateAsync(seedRows: 0).GetAwaiter().GetResult();
        try
        {
            _db.SeedMixedRelationAsync(PostCount, Rows, AuthorCount).GetAwaiter().GetResult();
            _connectionString = _db.ConnectionString;
            AssertLegsAgree();
        }
        catch
        {
            // BenchmarkDotNet does not call [GlobalCleanup] when [GlobalSetup] throws, so the temp
            // SQLite file would leak. Dispose here and rethrow.
            _db.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>
    /// Fails setup unless all three legs stitch the same number of relations.
    /// </summary>
    /// <remarks>
    /// Without this a regression that stopped populating <c>Author</c> or <c>Tags</c> would make the
    /// Inquiry leg do strictly less work and report as <em>faster</em> with better allocation — hiding
    /// exactly the defect #70 exists to prevent.
    /// </remarks>
    private void AssertLegsAgree()
    {
        var expected = Rows + PostCount;   // every tag maps to a live post; every post to a live author
        var adoNet = GridMixed_AdoNet().GetAwaiter().GetResult();
        var dapper = GridMixed_Dapper().GetAwaiter().GetResult();
        var inquiry = GridMixed_Inquiry().GetAwaiter().GetResult();

        if (adoNet != expected || dapper != expected || inquiry != expected)
        {
            throw new InvalidOperationException(
                $"Benchmark legs disagree (expected {expected}): AdoNet={adoNet}, Dapper={dapper}, Inquiry={inquiry}.");
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private const string AllPostsSql = "SELECT Id, AuthorId, Title FROM MixedBenchPost;";
    private const string AllAuthorsSql = "SELECT Id, Name FROM MixedBenchAuthor;";
    private const string AllTagsSql = "SELECT Id, PostId, Label FROM MixedBenchTag;";

    private static MixedBenchPost ReadPost(System.Data.Common.DbDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        AuthorId = reader.GetInt32(1),
        Title = reader.GetString(2),
    };

    private static MixedBenchAuthor ReadAuthor(System.Data.Common.DbDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
    };

    private static MixedBenchTag ReadTag(System.Data.Common.DbDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        PostId = reader.GetInt32(1),
        Label = reader.GetString(2),
    };

    // Counts both relations so neither stitch can be dead-code-eliminated.
    private static int Score(IEnumerable<MixedBenchPost> posts)
    {
        var count = 0;
        foreach (var post in posts)
        {
            count += post.Tags?.Count ?? 0;
            if (post.Author is not null) count++;
        }

        return count;
    }

    [BenchmarkCategory("EagerGridMixed"), Benchmark(Baseline = true)]
    public async Task<int> GridMixed_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var authorsById = new Dictionary<int, MixedBenchAuthor>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AllAuthorsSql;
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
            while (await reader.ReadAsync())
            {
                var author = ReadAuthor(reader);
                authorsById[author.Id] = author;
            }
        }

        var postsById = new Dictionary<int, MixedBenchPost>();
        var posts = new List<MixedBenchPost>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AllPostsSql;
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
            while (await reader.ReadAsync())
            {
                var post = ReadPost(reader);
                post.Tags = new List<MixedBenchTag>();
                post.Author = authorsById.TryGetValue(post.AuthorId, out var author) ? author : null;
                postsById[post.Id] = post;
                posts.Add(post);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AllTagsSql;
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
            while (await reader.ReadAsync())
            {
                var tag = ReadTag(reader);
                if (postsById.TryGetValue(tag.PostId, out var post))
                    post.Tags!.Add(tag);
            }
        }

        return Score(posts);
    }

    [BenchmarkCategory("EagerGridMixed"), Benchmark]
    public async Task<int> GridMixed_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var authorsById = (await connection.QueryAsync<MixedBenchAuthor>(AllAuthorsSql))
            .ToDictionary(static a => a.Id);

        var posts = (await connection.QueryAsync<MixedBenchPost>(AllPostsSql)).AsList();
        var postsById = new Dictionary<int, MixedBenchPost>(posts.Count);
        foreach (var post in posts)
        {
            post.Tags = new List<MixedBenchTag>();
            post.Author = authorsById.TryGetValue(post.AuthorId, out var author) ? author : null;
            postsById[post.Id] = post;
        }

        foreach (var tag in await connection.QueryAsync<MixedBenchTag>(AllTagsSql))
        {
            if (postsById.TryGetValue(tag.PostId, out var post))
                post.Tags!.Add(tag);
        }

        return Score(posts);
    }

    [BenchmarkCategory("EagerGridMixed"), Benchmark]
    public async Task<int> GridMixed_Inquiry()
    {
        var count = 0;
        await foreach (var post in _db.MixedPosts.SelectAllWithAuthorAndTagsAsync())
        {
            count += post.Tags?.Count ?? 0;
            if (post.Author is not null) count++;
        }

        return count;
    }
}
