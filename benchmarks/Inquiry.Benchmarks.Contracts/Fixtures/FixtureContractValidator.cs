namespace Inquiry.Benchmarks.Contracts.Fixtures;

public sealed record FixtureState(
    IReadOnlyDictionary<string, int> RowCounts,
    IReadOnlyDictionary<string, string> TableChecksums,
    IReadOnlyDictionary<string, long> IdentityState)
{
    public static FixtureState FromManifest(FixtureManifest manifest)
        => new(manifest.RowCounts, manifest.TableChecksums, manifest.IdentityState);
}

public static class FixtureContractValidator
{
    public static IReadOnlyList<ContractError> Validate(FixtureManifest manifest)
    {
        var errors = new List<ContractError>();
        var expected = NorthwindFixtureCatalog.For(manifest.Tier);
        var expectedKeys = NorthwindFixtureCatalog.Schema.Tables.Select(static table => table.Name).Order(StringComparer.Ordinal).ToArray();
        var actualCountKeys = manifest.RowCounts.Keys.Order(StringComparer.Ordinal).ToArray();
        var actualChecksumKeys = manifest.TableChecksums.Keys.Order(StringComparer.Ordinal).ToArray();

        AddIf(manifest.ContractVersion != NorthwindFixtureCatalog.ContractVersion, "fixture-version", "Fixture contract version drifted.");
        AddIf(manifest.SchemaHash != NorthwindFixtureCatalog.SchemaHash, "fixture-schema", "Fixture schema hash drifted.");
        AddIf(manifest.Seed != NorthwindFixtureCatalog.Seed, "fixture-seed", "Fixture seed drifted.");
        AddIf(!expectedKeys.SequenceEqual(actualCountKeys, StringComparer.Ordinal) ||
              !expectedKeys.SequenceEqual(actualChecksumKeys, StringComparer.Ordinal),
            "fixture-table-keys", "Fixture row-count/checksum table keys do not match the complete schema.");
        AddIf(expected.RowCounts.Any(pair => !manifest.RowCounts.TryGetValue(pair.Key, out var value) || value != pair.Value),
            "fixture-count", "Fixture row counts drifted from the checked tier contract.");
        AddIf(manifest.TableChecksums.Any(static pair => !IsSha256(pair.Value)),
            "fixture-checksum", "Every fixture table requires a lowercase SHA-256 row checksum.");
        AddIf(!DictionaryEqual(expected.TableChecksums, manifest.TableChecksums),
            "fixture-checksum-drift", "Fixture table checksums drifted from the checked tier contract.");
        AddIf(!DictionaryEqual(expected.IdentityState, manifest.IdentityState),
            "fixture-identity", "Fixture identity/sequence state drifted from the checked tier contract.");
        AddIf(!DictionaryEqual(expected.SelectivityBuckets, manifest.SelectivityBuckets) ||
              !StringComparer.Ordinal.Equals(expected.Distribution, manifest.Distribution),
            "fixture-distribution", "Fixture selectivity buckets or distribution drifted from the checked tier contract.");
        AddIf(!StringComparer.Ordinal.Equals(expected.Collation, manifest.Collation) ||
              !StringComparer.Ordinal.Equals(expected.TimeZone, manifest.TimeZone) ||
              !StringComparer.Ordinal.Equals(expected.Compatibility, manifest.Compatibility),
            "fixture-settings", "Fixture collation, timezone, or compatibility settings drifted from the checked tier contract.");
        return errors;

        void AddIf(bool condition, string code, string message)
        {
            if (condition) errors.Add(new(code, message));
        }
    }

    public static IReadOnlyList<ContractError> ValidateGenerated(FixtureManifest manifest)
    {
        var errors = new List<ContractError>(Validate(manifest));
        var actual = NorthwindFixtureGenerator.ComputeTableChecksums(manifest.Tier, manifest.Seed);
        if (manifest.TableChecksums.Any(pair => !actual.TryGetValue(pair.Key, out var checksum) || checksum != pair.Value))
            errors.Add(new("fixture-row-checksum", "Generated row stream does not match the checked per-table checksums."));
        return errors;
    }

    public static IReadOnlyList<ContractError> ValidateReset(FixtureState before, FixtureState after)
    {
        if (!DictionaryEqual(before.RowCounts, after.RowCounts) ||
            !DictionaryEqual(before.TableChecksums, after.TableChecksums) ||
            !DictionaryEqual(before.IdentityState, after.IdentityState))
            return [new("fixture-mutation-leakage", "Mutable fixture data or identity state leaked across cases.")];
        return [];
    }

    private static bool DictionaryEqual<T>(IReadOnlyDictionary<string, T> left, IReadOnlyDictionary<string, T> right)
        where T : notnull
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && EqualityComparer<T>.Default.Equals(pair.Value, value));

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
