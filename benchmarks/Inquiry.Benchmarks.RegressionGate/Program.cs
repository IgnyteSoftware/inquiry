using System.Text.Json;
using System.Text.Json.Serialization;
using Inquiry.Benchmarks.Contracts.Evidence;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Compare:  regression-gate compare <bdn-json> <baseline-json>");
    Console.Error.WriteLine("  Generate: regression-gate generate <bdn-json> --provider <p> --tfm <tfm> --commit <sha> [--env <env>] [--budget <pct>] [--abs <ns>]");
    return 1;
}

var command = args[0].ToLowerInvariant();

return command switch
{
    "compare" => RunCompare(args[1..]),
    "generate" => RunGenerate(args[1..]),
    _ => PrintError($"Unknown command: {command}")
};

static int RunCompare(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("compare requires <bdn-json-path> <baseline-json-path>");
        return 1;
    }

    var bdnPath = args[0];
    var baselinePath = args[1];

    if (!File.Exists(bdnPath))
    {
        Console.Error.WriteLine($"BDN report not found: {bdnPath}");
        return 1;
    }
    if (!File.Exists(baselinePath))
    {
        Console.Error.WriteLine($"Baseline not found: {baselinePath}");
        return 1;
    }

    var report = JsonSerializer.Deserialize<BdnReport>(File.ReadAllText(bdnPath));
    if (report?.Benchmarks is null || report.Benchmarks.Count == 0)
    {
        Console.Error.WriteLine("BDN report contains no benchmarks.");
        return 1;
    }

    var baseline = JsonSerializer.Deserialize<RegressionBaseline>(
        File.ReadAllText(baselinePath), EvidenceJson.Options);
    if (baseline?.Cases is null || baseline.Cases.Count == 0)
    {
        Console.Error.WriteLine("Baseline contains no cases.");
        return 1;
    }

    var results = RegressionComparator.Compare(baseline, report.Benchmarks);

    var failures = 0;
    var passes = 0;
    var skips = 0;

    Console.WriteLine();
    Console.WriteLine($"Regression gate: {baseline.Provider} / {baseline.RuntimeTfm}");
    Console.WriteLine($"Baseline commit: {baseline.Commit}");
    Console.WriteLine(new string('-', 120));
    Console.WriteLine($"{"Case",-60} {"Latency",-8} {"Δ%",-9} {"ΔNs",-14} {"Alloc",-8} {"Δ%",-9}");
    Console.WriteLine(new string('-', 120));

    foreach (var r in results)
    {
        var shortName = ShortenName(r.FullName);
        var latLabel = r.LatencyVerdict switch
        {
            RegressionVerdict.Pass => "PASS",
            RegressionVerdict.Fail => "FAIL",
            _ => "SKIP"
        };
        var allocLabel = r.AllocationVerdict switch
        {
            RegressionVerdict.Pass => "PASS",
            RegressionVerdict.Fail => "FAIL",
            _ => "SKIP"
        };
        var deltaPercentStr = r.LatencyVerdict == RegressionVerdict.Skip
            ? "-"
            : $"{r.LatencyDeltaPercent:+0.00;-0.00}%";
        var deltaNsStr = r.LatencyVerdict == RegressionVerdict.Skip
            ? "-"
            : FormatNs(r.LatencyDeltaNs);
        var allocDeltaStr = r.AllocationDeltaPercent is not null
            ? $"{r.AllocationDeltaPercent:+0.00;-0.00}%"
            : "-";

        Console.WriteLine($"{shortName,-60} {latLabel,-8} {deltaPercentStr,-9} {deltaNsStr,-14} {allocLabel,-8} {allocDeltaStr,-9}");

        if (r.LatencyVerdict == RegressionVerdict.Fail) failures++;
        else if (r.LatencyVerdict == RegressionVerdict.Pass) passes++;
        else skips++;

        if (r.AllocationVerdict == RegressionVerdict.Fail) failures++;
        else if (r.AllocationVerdict == RegressionVerdict.Pass) passes++;
    }

    Console.WriteLine(new string('-', 120));
    Console.WriteLine($"Total: {passes} pass, {failures} fail, {skips} skip");
    Console.WriteLine();

    if (failures > 0)
    {
        Console.Error.WriteLine($"REGRESSION DETECTED: {failures} budget violation(s).");
        return 1;
    }

    Console.WriteLine("All checks passed.");
    return 0;
}

static int RunGenerate(string[] args)
{
    if (args.Length < 1)
    {
        Console.Error.WriteLine("generate requires <bdn-json-path>");
        return 1;
    }

    var bdnPath = args[0];
    string? provider = null, tfm = null, commit = null, env = null;
    var budget = 0.10;
    var abs = 5000.0;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--provider": provider = args[++i]; break;
            case "--tfm": tfm = args[++i]; break;
            case "--commit": commit = args[++i]; break;
            case "--env": env = args[++i]; break;
            case "--budget": budget = double.Parse(args[++i]); break;
            case "--abs": abs = double.Parse(args[++i]); break;
        }
    }

    if (provider is null || tfm is null || commit is null)
    {
        Console.Error.WriteLine("generate requires --provider, --tfm, and --commit.");
        return 1;
    }

    if (!File.Exists(bdnPath))
    {
        Console.Error.WriteLine($"BDN report not found: {bdnPath}");
        return 1;
    }

    var report = JsonSerializer.Deserialize<BdnReport>(File.ReadAllText(bdnPath));
    if (report?.Benchmarks is null || report.Benchmarks.Count == 0)
    {
        Console.Error.WriteLine("BDN report contains no benchmarks.");
        return 1;
    }

    var baseline = RegressionBaselineGenerator.Generate(
        report, provider, tfm, commit, env ?? "unknown", budget, abs);

    var json = JsonSerializer.Serialize(baseline, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    });

    Console.Write(json);
    return 0;
}

static string ShortenName(string fullName)
{
    var dot = fullName.LastIndexOf('.');
    if (dot > 0)
    {
        var paren = fullName.IndexOf('(', dot);
        if (paren > 0)
            return fullName[(dot + 1)..];
    }
    return fullName.Length > 58 ? fullName[..58] + ".." : fullName;
}

static string FormatNs(double ns)
{
    return Math.Abs(ns) switch
    {
        >= 1_000_000_000 => $"{ns / 1_000_000_000:+0.000;-0.000} s",
        >= 1_000_000 => $"{ns / 1_000_000:+0.000;-0.000} ms",
        >= 1_000 => $"{ns / 1_000:+0.000;-0.000} μs",
        _ => $"{ns:+0.000;-0.000} ns"
    };
}

static int PrintError(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
