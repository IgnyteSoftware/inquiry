using System.Diagnostics;
using System.Globalization;

namespace Inquiry.Benchmarks;

/// <summary>
/// Measures real process cold start. Each measurement is a new process that runs one leg and exits,
/// timed from just before the process starts until it has exited. The time therefore includes process
/// launch, runtime start, assembly loading, JIT, the leg's own work and process exit.
/// </summary>
internal static class ProcessColdStartRunner
{
    /// <summary>Environment variable that gives the probe process its database connection string.</summary>
    public const string ConnectionVariable = "INQUIRY_BENCH_COLD_START_CONNECTION";

    /// <summary>The connection string that the driver passed to this probe process.</summary>
    public static string ConnectionString
        => Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"{ConnectionVariable} is not set. Start the probe through the driver.");

    /// <summary>
    /// Launches <paramref name="program"/> <paramref name="launches"/> times for each leg, as
    /// <c>program probeSwitch leg</c>, and prints the launch-to-exit times. Returns 1 when a probe fails.
    /// </summary>
    /// <param name="program">A managed assembly (<c>.dll</c>), which runs on the current host, or an executable.</param>
    /// <param name="probeSwitch">The argument that selects the probe mode of the program.</param>
    /// <param name="legs">The legs to measure. Ratios are relative to <paramref name="baseline"/>.</param>
    /// <param name="baseline">The leg that the <c>Ratio</c> column divides by.</param>
    /// <param name="launches">Processes per leg.</param>
    /// <param name="connectionString">Passed to each probe in <see cref="ConnectionVariable"/>.</param>
    public static int Run(string program, string probeSwitch, IReadOnlyList<string> legs, string baseline, int launches, string connectionString)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(launches, 1);
        var times = legs.ToDictionary(leg => leg, _ => new List<double>(launches));

        // One launch of every leg per round, so that drift on the machine reaches every leg equally.
        for (var round = 0; round < launches; round++)
        {
            foreach (var leg in legs)
            {
                var start = CreateStartInfo(program);
                start.ArgumentList.Add(probeSwitch);
                start.ArgumentList.Add(leg);
                start.Environment[ConnectionVariable] = connectionString;

                var timestamp = Stopwatch.GetTimestamp();
                using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {start.FileName}.");
                process.WaitForExit();
                var elapsed = Stopwatch.GetElapsedTime(timestamp);
                if (process.ExitCode != 0)
                {
                    Console.Error.WriteLine($"Leg '{leg}' exited with code {process.ExitCode} on launch {round + 1}.");
                    return 1;
                }

                times[leg].Add(elapsed.TotalMilliseconds);
            }
        }

        var baselineMedian = Median(times[baseline]);
        Console.WriteLine($"Process cold start: {launches} launches per leg, process launch to process exit, in ms.");
        Console.WriteLine();
        Console.WriteLine($"| {"Leg",-18} | {"Median",9} | {"Min",9} | {"Max",9} | {"Ratio",5} |");
        Console.WriteLine($"|{new string('-', 20)}|{new string('-', 10)}:|{new string('-', 10)}:|{new string('-', 10)}:|{new string('-', 6)}:|");
        foreach (var leg in legs)
        {
            var median = Median(times[leg]);
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {leg,-18} | {median,9:F1} | {times[leg].Min(),9:F1} | {times[leg].Max(),9:F1} | {median / baselineMedian,5:F2} |"));
        }

        return 0;
    }

    private static ProcessStartInfo CreateStartInfo(string program)
    {
        if (!program.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo(program) { UseShellExecute = false };
        }

        // A managed assembly needs a host. Use the host of this process when it is the dotnet muxer.
        var host = Environment.ProcessPath;
        var start = new ProcessStartInfo(
            host is not null && Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? host : "dotnet")
        {
            UseShellExecute = false,
        };
        start.ArgumentList.Add(program);
        return start;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
