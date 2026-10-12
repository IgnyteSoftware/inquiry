using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Inquiry.Benchmarks.Contracts.Evidence;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Compare:  regression-gate compare <bdn-json> <baseline-json> --tfm <tfm> --env <env> [--filter <glob>]");
    Console.Error.WriteLine("  Generate: regression-gate generate <bdn-json> --provider <p> --tfm <tfm> --commit <sha> --env <env> [--budget <pct>] [--abs <ns>]");
    Console.Error.WriteLine("Compare exits 0 when every case passes, 1 on a budget violation, 2 when evidence is missing or invalid, and 3 when --filter selects no baseline case.");
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
    // Exit code 1 means a proven budget violation. Every evidence problem uses 2 so the
    // workflow can report it as a harness failure instead of a library regression.
    const int evidenceFailure = 2;
    const int notSelected = 3;

    if (args.Length < 2)
    {
        Console.Error.WriteLine("compare requires <bdn-json-path> <baseline-json-path>");
        return evidenceFailure;
    }

    var bdnPath = args[0];
    var baselinePath = args[1];
    string? tfm = null, env = null;
    var filter = "*";

    for (var i = 2; i < args.Length - 1; i++)
    {
        switch (args[i])
        {
            case "--tfm": tfm = args[++i]; break;
            case "--env": env = args[++i]; break;
            case "--filter": filter = args[++i]; break;
        }
    }

    if (tfm is null || env is null)
    {
        Console.Error.WriteLine("compare requires --tfm and --env.");
        return evidenceFailure;
    }

    if (!File.Exists(baselinePath))
    {
        Console.Error.WriteLine($"Baseline not found: {baselinePath}");
        return evidenceFailure;
    }

    var baseline = JsonSerializer.Deserialize<RegressionBaseline>(
        File.ReadAllText(baselinePath), EvidenceJson.Options);
    if (baseline?.Cases is null || baseline.Cases.Count == 0)
    {
        Console.Error.WriteLine("Baseline contains no cases.");
        return evidenceFailure;
    }

    // Timing from another runner or runtime is not comparable, so it cannot prove or clear a regression.
    if (!string.Equals(baseline.RuntimeTfm, tfm, StringComparison.Ordinal) ||
        !string.Equals(baseline.Environment, env, StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            $"No accepted baseline for this runner: baseline is '{baseline.RuntimeTfm}' on '{baseline.Environment}', " +
            $"current run is '{tfm}' on '{env}'.");
        return evidenceFailure;
    }

    // A filtered run measures only the selected cases. This matches BenchmarkDotNet's --filter glob,
    // which tests both the full case name and the name without parameters.
    var filterRegex = new Regex(
        "^" + Regex.Escape(filter).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    var selectedCases = baseline.Cases
        .Where(c => filterRegex.IsMatch(c.FullName) || filterRegex.IsMatch(c.FullName.Split('(')[0]))
        .ToList();
    if (selectedCases.Count == 0)
    {
        Console.Error.WriteLine($"Filter '{filter}' selects no baseline cases.");
        return notSelected;
    }

    // Selection is resolved first so a class the filter selects still fails when its report is absent.
    if (!File.Exists(bdnPath))
    {
        Console.Error.WriteLine($"BDN report not found: {bdnPath}");
        return evidenceFailure;
    }

    var report = JsonSerializer.Deserialize<BdnReport>(File.ReadAllText(bdnPath));
    if (report?.Benchmarks is null || report.Benchmarks.Count == 0)
    {
        Console.Error.WriteLine("BDN report contains no benchmarks.");
        return evidenceFailure;
    }

    var results = RegressionComparator.Compare(baseline with { Cases = selectedCases }, report.Benchmarks);

    var failures = 0;
    var passes = 0;
    var skips = 0;
    var missing = 0;

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
            RegressionVerdict.Missing => "MISSING",
            _ => "SKIP"
        };
        var allocLabel = r.AllocationVerdict switch
        {
            RegressionVerdict.Pass => "PASS",
            RegressionVerdict.Fail => "FAIL",
            RegressionVerdict.Missing => "MISSING",
            _ => "SKIP"
        };
        var deltaPercentStr = r.LatencyVerdict == RegressionVerdict.Missing
            ? "-"
            : $"{r.LatencyDeltaPercent:+0.00;-0.00}%";
        var deltaNsStr = r.LatencyVerdict == RegressionVerdict.Missing
            ? "-"
            : FormatNs(r.LatencyDeltaNs);
        var allocDeltaStr = r.AllocationDeltaPercent is not null
            ? $"{r.AllocationDeltaPercent:+0.00;-0.00}%"
            : "-";

        Console.WriteLine($"{shortName,-60} {latLabel,-8} {deltaPercentStr,-9} {deltaNsStr,-14} {allocLabel,-8} {allocDeltaStr,-9}");

        foreach (var verdict in new[] { r.LatencyVerdict, r.AllocationVerdict })
        {
            switch (verdict)
            {
                case RegressionVerdict.Fail: failures++; break;
                case RegressionVerdict.Pass: passes++; break;
                case RegressionVerdict.Missing: missing++; break;
                default: skips++; break;
            }
        }
    }

    Console.WriteLine(new string('-', 120));
    Console.WriteLine($"Total: {passes} pass, {failures} fail, {missing} missing, {skips} skip");
    Console.WriteLine();

    if (missing > 0)
        Console.Error.WriteLine($"INCOMPLETE EVIDENCE: {missing} expected measurement(s) missing from the report.");

    if (failures > 0)
    {
        Console.Error.WriteLine($"REGRESSION DETECTED: {failures} budget violation(s).");
        return 1;
    }

    if (missing > 0)
        return evidenceFailure;

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

    if (provider is null || tfm is null || commit is null || env is null)
    {
        Console.Error.WriteLine("generate requires --provider, --tfm, --commit, and --env.");
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

    // The generator drops cases without statistics, so a failed benchmark would vanish from the candidate.
    var incomplete = report.Benchmarks
        .Where(b => b.FullName is null || b.Statistics is not { N: > 0 })
        .ToList();
    if (incomplete.Count > 0)
    {
        foreach (var b in incomplete)
            Console.Error.WriteLine($"Benchmark has no statistics: {b.FullName ?? "(unnamed)"}");
        return 1;
    }

    var baseline = RegressionBaselineGenerator.Generate(
        report, provider, tfm, commit, env, budget, abs);

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
