using Inquiry.Entities;
using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.Sqlite;

namespace Inquiry.Sqlite.Tests;

// #258: every name differs. The parent key property RegionId is column region_key, and the child
// foreign-key property RegionRef is column region_id. The relation names the property.
[InquiryTable("RkRegion")]
public sealed class RkRegion
{
    [InquiryKey("region_key")] public int RegionId { get; set; }
    [InquiryColumn("Name")] public string Name { get; set; } = string.Empty;

    [InquiryRelation(nameof(RkTerritory.RegionRef))]
    public List<RkTerritory> Territories { get; set; } = new();
}

[InquiryTable("RkTerritory")]
public sealed class RkTerritory
{
    [InquiryKey("territory_key")] public int TerritoryId { get; set; }
    [InquiryColumn("region_id")] public int RegionRef { get; set; }
    [InquiryColumn("Name")] public string Name { get; set; } = string.Empty;

    [InquiryRelation(nameof(RegionRef))]
    public RkRegion? Region { get; set; }
}

public partial class RkRegionStore : InquiryStore<RkRegion>
{
    [InquirySelectOneByKeyEager]
    public partial Task<RkRegion?> GetWithTerritoriesAsync(int regionId, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<RkRegion> AllWithTerritoriesAsync(CancellationToken cancellationToken = default);
}

public partial class RkTerritoryStore : InquiryStore<RkTerritory>
{
    [InquirySelectOneByKeyEager]
    public partial Task<RkTerritory?> GetWithRegionAsync(int territoryId, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<RkTerritory> AllWithRegionAsync(CancellationToken cancellationToken = default);
}

// #277: byte[] keys on an ordinary collection, a reference, and a many-to-many association. Every row is
// materialized separately, so equal keys arrive as different array instances.
[InquiryTable("BkDoc")]
public sealed class BkDoc
{
    [InquiryKey(Length = 4)] public byte[] Hash { get; set; } = Array.Empty<byte>();
    [InquiryColumn("Title")] public string Title { get; set; } = string.Empty;

    [InquiryRelation(nameof(BkNote.DocHash))]
    public List<BkNote> Notes { get; set; } = new();

    [InquiryManyToMany(typeof(BkDocTag), nameof(BkDocTag.DocHash), nameof(BkDocTag.TagHash))]
    public List<BkTag> Tags { get; set; } = new();
}

[InquiryTable("BkNote")]
public sealed class BkNote
{
    [InquiryKey] public int Id { get; set; }
    [InquiryColumn("DocHash", Length = 4)] public byte[]? DocHash { get; set; }

    [InquiryRelation(nameof(DocHash))]
    public BkDoc? Doc { get; set; }
}

[InquiryTable("BkTag")]
public sealed class BkTag
{
    [InquiryKey(Length = 4)] public byte[] Hash { get; set; } = Array.Empty<byte>();
    [InquiryColumn("Label")] public string Label { get; set; } = string.Empty;
}

[InquiryTable("BkDocTag")]
public sealed class BkDocTag
{
    [InquiryKey(Length = 4)] public byte[] DocHash { get; set; } = Array.Empty<byte>();
    [InquiryKey(Length = 4)] public byte[] TagHash { get; set; } = Array.Empty<byte>();
}

public partial class BkDocStore : InquiryStore<BkDoc>
{
    [InquirySelectAllEager]
    public partial IAsyncEnumerable<BkDoc> AllAsync(CancellationToken cancellationToken = default);
}

public partial class BkNoteStore : InquiryStore<BkNote>
{
    [InquirySelectAllEager]
    public partial IAsyncEnumerable<BkNote> AllAsync(CancellationToken cancellationToken = default);
}

public sealed class RelationshipKeyIntegrationTests
{
    private const string RegionDdl =
        "CREATE TABLE RkRegion (region_key INTEGER NOT NULL PRIMARY KEY, Name TEXT NOT NULL);" +
        "CREATE TABLE RkTerritory (territory_key INTEGER NOT NULL PRIMARY KEY, region_id INTEGER NOT NULL, Name TEXT NOT NULL);" +
        "INSERT INTO RkRegion VALUES (1, 'North'), (2, 'South'), (3, 'Empty');" +
        "INSERT INTO RkTerritory VALUES (10, 1, 'N1'), (11, 1, 'N2'), (20, 2, 'S1');";

    private const string ByteKeyDdl =
        "CREATE TABLE BkDoc (Hash BLOB NOT NULL PRIMARY KEY, Title TEXT NOT NULL);" +
        "CREATE TABLE BkNote (Id INTEGER NOT NULL PRIMARY KEY, DocHash BLOB NULL);" +
        "CREATE TABLE BkTag (Hash BLOB NOT NULL PRIMARY KEY, Label TEXT NOT NULL);" +
        "CREATE TABLE BkDocTag (DocHash BLOB NOT NULL, TagHash BLOB NOT NULL, PRIMARY KEY (DocHash, TagHash));" +
        "INSERT INTO BkDoc VALUES (X'0A0B0C0D', 'Linked'), (X'01020304', 'Alone');" +
        "INSERT INTO BkTag VALUES (X'FFEEDDCC', 'Blue'), (X'00000000', 'Unused');" +
        "INSERT INTO BkDocTag VALUES (X'0A0B0C0D', X'FFEEDDCC');" +
        "INSERT INTO BkNote VALUES (1, X'0A0B0C0D'), (2, NULL);";

    [Fact]
    public async Task RelationForeignKeyWithADifferentColumnNameLoadsBothDirections()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(RegionDdl, "RkNames");
        var regions = harness.GetRequiredService<RkRegionStore>();
        var territories = harness.GetRequiredService<RkTerritoryStore>();

        var north = await regions.GetWithTerritoriesAsync(1);
        Assert.Equal(["N1", "N2"], north!.Territories.Select(static t => t.Name).OrderBy(static n => n));

        var all = new List<RkRegion>();
        await foreach (var region in regions.AllWithTerritoriesAsync())
        {
            all.Add(region);
        }

        Assert.Equal(
            ["North:N1,N2", "South:S1", "Empty:"],
            all.OrderBy(static r => r.RegionId).Select(static r => r.Name + ":" + string.Join(",", r.Territories.Select(static t => t.Name).OrderBy(static n => n))));

        Assert.Equal("South", (await territories.GetWithRegionAsync(20))!.Region!.Name);
        var allTerritories = new List<RkTerritory>();
        await foreach (var territory in territories.AllWithRegionAsync())
        {
            allTerritories.Add(territory);
        }

        Assert.Equal(["North", "North", "South"], allTerritories.OrderBy(static t => t.TerritoryId).Select(static t => t.Region!.Name));
    }

    [Fact]
    public async Task ByteArrayKeysReturnLinksTheDatabaseMatched()
    {
        // Before #277 every list below was empty: the dictionaries compared the arrays by reference.
        await using var harness = await SqliteTestHarness.CreateAsync(ByteKeyDdl, "BkKeys");

        var docs = new List<BkDoc>();
        await foreach (var doc in harness.GetRequiredService<BkDocStore>().AllAsync())
        {
            docs.Add(doc);
        }

        var linked = Assert.Single(docs, static d => d.Title == "Linked");
        Assert.Equal("Blue", Assert.Single(linked.Tags).Label);
        Assert.Equal(1, Assert.Single(linked.Notes).Id);
        var alone = Assert.Single(docs, static d => d.Title == "Alone");
        Assert.Empty(alone.Tags);
        Assert.Empty(alone.Notes);

        var notes = new List<BkNote>();
        await foreach (var note in harness.GetRequiredService<BkNoteStore>().AllAsync())
        {
            notes.Add(note);
        }

        Assert.Equal("Linked", notes.Single(static n => n.Id == 1).Doc!.Title);
        Assert.Null(notes.Single(static n => n.Id == 2).Doc);
    }

    private static string Quote(string identifier) => "\"" + identifier + "\"";

    private static async Task<(SqliteTestHarness Harness, SqliteConnection Connection)> AutoJunctionHarnessAsync(string name)
    {
        var harness = await SqliteTestHarness.CreateAsync(
            GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, AutoJunctionKeyFacetAssertions.Tables), name);
        var connection = new SqliteConnection(harness.ConnectionString);
        await connection.OpenAsync();
        return (harness, connection);
    }

    [Fact]
    public async Task AutoJunctionOverNonIntegerKeysKeepsKeyTypesAndForeignKeys()
    {
        var (harness, connection) = await AutoJunctionHarnessAsync("AjFacets");
        await using var _ = harness;
        await using var __ = connection;

        // SQLite stores column types as declared affinities, so length, precision, and scale are not kept.
        await AutoJunctionKeyFacetAssertions.JunctionColumnsMatchKeysAndEnforceForeignKeysAsync(
            connection,
            Quote,
            static (table, column) => $"SELECT type FROM pragma_table_info('{table}') WHERE name = '{column}'",
            new AjKeyTypes(Name: "TEXT", Amount: "NUMERIC", Sku: "TEXT"));
    }

    [Fact]
    public async Task AutoJunctionWithOverriddenColumnsReadsBothNavigations()
    {
        var (harness, connection) = await AutoJunctionHarnessAsync("AjReads");
        await using var _ = harness;
        await using var __ = connection;

        await AutoJunctionKeyFacetAssertions.BothNavigationsReadTheirOwnLinksAsync(
            harness.GetRequiredService<AjCodeStore>(),
            harness.GetRequiredService<AjPriceStore>(),
            harness.GetRequiredService<AjSkuStore>(),
            connection,
            Quote);
    }
}
