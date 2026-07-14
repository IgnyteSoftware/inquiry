using System.Text.Json;
using Inquiry.Benchmarks.Contracts.Evidence;
using Inquiry.Benchmarks.Contracts.Fixtures;
using Inquiry.Benchmarks.Contracts;

if (args.Length >= 9 && args[0] == "--resolved-dependencies" &&
    Enum.TryParse<BenchmarkSourceLane>(args[2], true, out var sourceLane))
{
    var roots = args[8..].Select(argument =>
    {
        var separator = argument.IndexOf('=');
        if (separator <= 0 || separator == argument.Length - 1)
            throw new ArgumentException($"Selected-asset root must use <id>=<path>: '{argument}'.");
        return new SelectedAssetRoot(argument[..separator], argument[(separator + 1)..]);
    }).ToArray();
    var dependencyManifest = ResolvedDependencyManifestCollector.Collect(
        args[5], args[6], args[1], sourceLane, args[3], args[4], roots);
    var outputPath = Path.GetFullPath(args[7]);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    await File.WriteAllBytesAsync(outputPath, dependencyManifest.ToCanonicalJsonBytes());
    Console.WriteLine(dependencyManifest.ContentSha256);
    return 0;
}

if (args.Length == 3 && args[0] == "--table-checksum" && Enum.TryParse<FixtureTier>(args[1], true, out var tableTier))
{
    var table = NorthwindFixtureCatalog.Schema.Tables.SingleOrDefault(candidate =>
        StringComparer.Ordinal.Equals(candidate.Name, args[2]));
    if (table is null)
    {
        Console.Error.WriteLine($"Unknown fixture table '{args[2]}'.");
        return 2;
    }

    Console.WriteLine(FixtureChecksum.Compute(
        NorthwindFixtureGenerator.Generate(table.Name, tableTier, NorthwindFixtureCatalog.Seed)));
    return 0;
}

if (args.Length == 2 && args[0] == "--checksums" && Enum.TryParse<FixtureTier>(args[1], true, out var checksumTier))
{
    Console.WriteLine(JsonSerializer.Serialize(
        NorthwindFixtureGenerator.ComputeTableChecksums(checksumTier),
        new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Length != 2 || !Enum.TryParse<FixtureTier>(args[0], true, out var tier))
{
    Console.Error.WriteLine("Usage: Inquiry.Benchmarks.FixtureGenerator <tiny|standard|large> <output-directory>");
    Console.Error.WriteLine("       Inquiry.Benchmarks.FixtureGenerator --checksums <tiny|standard|large>");
    Console.Error.WriteLine("       Inquiry.Benchmarks.FixtureGenerator --table-checksum <tiny|standard|large> <table>");
    Console.Error.WriteLine("       Inquiry.Benchmarks.FixtureGenerator --resolved-dependencies <provider> <developerProject|releaseCandidatePackage> <net8.0|net10.0> <rid> <selected-assets.tsv> <project.assets.json> <output-manifest> <root-id=path> [<root-id=path> ...]");
    return 2;
}

var outputDirectory = Path.GetFullPath(args[1]);
if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
{
    Console.Error.WriteLine("Output directory must be absent or empty.");
    return 2;
}

Directory.CreateDirectory(outputDirectory);
var manifest = NorthwindFixtureCatalog.For(tier);
var checksums = new SortedDictionary<string, string>(StringComparer.Ordinal);

foreach (var table in NorthwindFixtureCatalog.Schema.Tables)
{
    var safeName = string.Concat(table.Name.Select(static c => char.IsLetterOrDigit(c) ? c : '_'));
    var path = Path.Combine(outputDirectory, safeName + ".jsonl");
    await using var stream = File.Create(path);
    await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
    var rows = NorthwindFixtureGenerator.Generate(table.Name, tier, manifest.Seed);
    foreach (var row in rows)
        await writer.WriteLineAsync(JsonSerializer.Serialize(row.Values, EvidenceJson.Options));
    await writer.FlushAsync();
    checksums[table.Name] = FixtureChecksum.Compute(NorthwindFixtureGenerator.Generate(table.Name, tier, manifest.Seed));
    if (!StringComparer.Ordinal.Equals(checksums[table.Name], manifest.TableChecksums[table.Name]))
        throw new InvalidOperationException($"Generated checksum drift for '{table.Name}'.");
}

var emittedManifest = manifest with { TableChecksums = checksums };
await File.WriteAllTextAsync(
    Path.Combine(outputDirectory, "manifest.json"),
    JsonSerializer.Serialize(emittedManifest, new JsonSerializerOptions(EvidenceJson.Options) { WriteIndented = true }));
await File.WriteAllTextAsync(
    Path.Combine(outputDirectory, "schema.json"),
    JsonSerializer.Serialize(NorthwindFixtureCatalog.Schema, new JsonSerializerOptions(EvidenceJson.Options) { WriteIndented = true }));
return 0;
