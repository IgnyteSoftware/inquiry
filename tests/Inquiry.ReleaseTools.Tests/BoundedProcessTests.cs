using System.ComponentModel;
using System.Diagnostics;

namespace Inquiry.ReleaseTools.Tests;

public sealed class BoundedProcessTests
{
    [Fact]
    public void Concurrent_stream_drains_bound_retention_well_below_high_volume_output()
    {
        var start = Shell(
            windows: "$line = 'x' * 128; 1..8192 | ForEach-Object { [Console]::Out.WriteLine($line); [Console]::Error.WriteLine($line) }; [Console]::Out.Write('stdout-completed'); [Console]::Error.Write('stderr-completed')",
            unix: "line='xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx'; i=0; while [ $i -lt 8192 ]; do printf '%s\\n' \"$line\"; printf '%s\\n' \"$line\" >&2; i=$((i+1)); done; printf stdout-completed; printf stderr-completed >&2");

        var result = BoundedProcess.Run(start, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StreamsDrained);
        Assert.True(result.StandardOutputTruncated);
        Assert.True(result.StandardErrorTruncated);
        Assert.Equal(BoundedProcess.MaximumCapturedCharacters, result.StandardOutput.Length);
        Assert.Equal(BoundedProcess.MaximumCapturedCharacters, result.StandardError.Length);
        Assert.EndsWith("stdout-completed", result.StandardOutput, StringComparison.Ordinal);
        Assert.EndsWith("stderr-completed", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeout_requests_tree_kill_after_child_readiness_and_stops_observed_processes()
    {
        var temporary = Directory.CreateTempSubdirectory("inquiry-process-ready-");
        var readyPath = Path.Combine(temporary.FullName, "child.pid");
        BoundedProcessResult? result = null;
        int? childId = null;
        try
        {
            var start = Shell(
                windows: $"$child = Start-Process powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 30' -PassThru; [IO.File]::WriteAllText({PowerShellLiteral(readyPath)}, $child.Id.ToString()); Wait-Process -Id $child.Id",
                unix: $"sleep 30 & child=$!; printf '%s' \"$child\" > {ShellLiteral(readyPath)}; wait \"$child\"");
            var stopwatch = Stopwatch.StartNew();

            result = BoundedProcess.Run(start, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            stopwatch.Stop();

            Assert.True(File.Exists(readyPath), "The child did not signal readiness before the execution deadline.");
            childId = int.Parse(File.ReadAllText(readyPath), System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(result.TimedOut);
            Assert.True(result.ProcessTreeKillRequested, result.KillError);
            Assert.True(result.RootExited, result.KillError);
            Assert.True(result.StreamsDrained);
            Assert.Null(result.KillError);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(12), $"Timeout cleanup took {stopwatch.Elapsed}.");
            AssertProcessStops(result.ProcessId);
            AssertProcessStops(childId.Value);
        }
        finally
        {
            childId ??= ReadProcessId(readyPath);
            if (result is not null) TryStop(result.ProcessId);
            if (childId is not null) TryStop(childId.Value);
            temporary.Delete(recursive: true);
        }
    }

    [Fact]
    public void Exited_parent_cannot_leave_stream_draining_unbounded()
    {
        var temporary = Directory.CreateTempSubdirectory("inquiry-process-descendant-");
        var childPath = Path.Combine(temporary.FullName, "child.pid");
        BoundedProcessResult? result = null;
        int? childId = null;
        try
        {
            var start = Shell(
                windows: $"$child = Start-Process powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 5' -NoNewWindow -PassThru; [IO.File]::WriteAllText({PowerShellLiteral(childPath)}, $child.Id.ToString()); [Console]::Out.Write('completed')",
                unix: $"sleep 5 & child=$!; printf '%s' \"$child\" > {ShellLiteral(childPath)}; printf completed");
            var stopwatch = Stopwatch.StartNew();

            result = BoundedProcess.Run(start, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(150));
            stopwatch.Stop();

            Assert.True(File.Exists(childPath));
            childId = int.Parse(File.ReadAllText(childPath), System.Globalization.CultureInfo.InvariantCulture);
            Assert.False(result.TimedOut);
            Assert.True(result.RootExited);
            Assert.False(result.ProcessTreeKillRequested);
            Assert.False(result.StreamsDrained);
            Assert.Contains("completed", result.StandardOutput, StringComparison.Ordinal);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Stream draining took {stopwatch.Elapsed}.");
        }
        finally
        {
            childId ??= ReadProcessId(childPath);
            if (result is not null) TryStop(result.ProcessId);
            if (childId is not null) TryStop(childId.Value);
            temporary.Delete(recursive: true);
        }
    }

    private static ProcessStartInfo Shell(string windows, string unix)
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("powershell.exe")
            : new ProcessStartInfo("/bin/sh");
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add(windows);
        }
        else
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(unix);
        }
        return start;
    }

    private static void AssertProcessStops(int processId)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(3) && IsRunning(processId))
            Thread.Sleep(25);
        Assert.False(IsRunning(processId), $"Process {processId} remained alive after timeout cleanup.");
    }

    private static void TryStop(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(2_000);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
        }
    }

    private static string PowerShellLiteral(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string ShellLiteral(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static int? ReadProcessId(string path)
    {
        try
        {
            return File.Exists(path)
                && int.TryParse(File.ReadAllText(path), System.Globalization.CultureInfo.InvariantCulture, out var processId)
                    ? processId
                    : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
