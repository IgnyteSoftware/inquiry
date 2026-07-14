namespace Inquiry.Benchmarks.Contracts.Fixtures;

public enum FixtureTier { Tiny, Standard, Large }

public sealed record FixtureColumn(
    string Name,
    string ClrType,
    string DatabaseType,
    bool Nullable,
    int? Length,
    int? Precision,
    int? Scale,
    bool IsGenerated,
    string? DefaultExpression,
    string? Collation);
public sealed record FixtureIndex(string Name, IReadOnlyList<string> Columns, bool Unique);
public sealed record FixtureStatistic(string Name, IReadOnlyList<string> Columns, string Sampling);
public sealed record FixtureForeignKey(
    string Name,
    string ChildTable,
    IReadOnlyList<string> ChildColumns,
    string ParentTable,
    IReadOnlyList<string> ParentColumns);

public sealed record FixtureTableSchema(
    string Name,
    IReadOnlyList<FixtureColumn> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<FixtureIndex> Indexes,
    IReadOnlyList<FixtureStatistic> Statistics);

public sealed record FixtureSchema(
    string Version,
    IReadOnlyList<FixtureTableSchema> Tables,
    IReadOnlyList<FixtureForeignKey> ForeignKeys)
{
    public string CanonicalText => CanonicalHash.Join(
        new[] { Version }
            .Concat(Tables.Select(static table => CanonicalHash.Join(
                new[] { table.Name, CanonicalHash.Join(table.PrimaryKey) }
                    .Concat(table.Columns.Select(static column =>
                        $"{column.Name}:{column.ClrType}:{column.DatabaseType}:{column.Nullable}:{column.Length}:{column.Precision}:{column.Scale}:{column.IsGenerated}:{column.DefaultExpression}:{column.Collation}"))
                    .Concat(table.Indexes.Select(static index => $"{index.Name}:{index.Unique}:{CanonicalHash.Join(index.Columns)}"))
                    .Concat(table.Statistics.Select(static statistic => $"{statistic.Name}:{statistic.Sampling}:{CanonicalHash.Join(statistic.Columns)}")))))
            .Concat(ForeignKeys.Select(static key => CanonicalHash.Join(
            [
                key.Name,
                key.ChildTable,
                CanonicalHash.Join(key.ChildColumns),
                key.ParentTable,
                CanonicalHash.Join(key.ParentColumns),
            ]))));
}

public sealed record DatabaseImageContract(string Provider, string Repository, string Digest)
{
    public string Reference => $"{Repository}@{Digest}";
}

public static class DatabaseImageCatalog
{
    public static IReadOnlyList<DatabaseImageContract> Images { get; } =
    [
        new("sqlserver", "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04", "sha256:c1aa8afe9b06eab64c9774a4802dcd032205d1be785b1fd51e1c0151e7586b74"),
        new("postgresql", "postgres:16-alpine", "sha256:fd1e8d0274f13f5a03a2673a207b28e14823c2f2efc3ca4bb4197c8a9f841bdc"),
        new("mysql", "mysql:8.4", "sha256:d36d39a64cd12a5c1cc9e6aa2bfb5f8d4c81a2f6586e0a04a9ae13939db02209"),
        new("mariadb", "mariadb:11.4", "sha256:a794d9eb009e20de605858a11f32f63b4075cbd197c650436f0e3b457e4caed7"),
        new("oracle", "gvenzl/oracle-xe:21-slim-faststart", "sha256:f82bccdf6020d27373fdf0e93046b63eb3f777a0289e329d9839feebaf4555de"),
    ];

    public static DatabaseImageContract GetRequired(string provider)
        => Images.SingleOrDefault(image => StringComparer.OrdinalIgnoreCase.Equals(image.Provider, provider))
           ?? throw new ArgumentOutOfRangeException(nameof(provider), provider, "Provider has no pinned benchmark image.");
}

public sealed record FixtureManifest(
    string ContractVersion,
    FixtureTier Tier,
    int Seed,
    string SchemaHash,
    IReadOnlyDictionary<string, int> RowCounts,
    IReadOnlyDictionary<string, string> TableChecksums,
    IReadOnlyDictionary<string, long> IdentityState,
    IReadOnlyDictionary<string, string> SelectivityBuckets,
    string Distribution,
    string Collation,
    string TimeZone,
    string Compatibility)
{
    public string IdentityHash => CanonicalHash.Sha256(CanonicalHash.Join(
        new[]
        {
            ContractVersion,
            Tier.ToString(),
            Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SchemaHash,
            Distribution,
            Collation,
            TimeZone,
            Compatibility,
        }
        .Concat(RowCounts.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"count:{pair.Key}={pair.Value}"))
        .Concat(TableChecksums.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"checksum:{pair.Key}={pair.Value}"))
        .Concat(IdentityState.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"identity:{pair.Key}={pair.Value}"))
        .Concat(SelectivityBuckets.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"bucket:{pair.Key}={pair.Value}"))));
}
