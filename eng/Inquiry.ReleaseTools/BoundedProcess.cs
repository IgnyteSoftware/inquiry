using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;

namespace Inquiry.ReleaseTools;

internal sealed record BoundedProcessResult(
    int ProcessId,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    bool TimedOut,
    bool RootExited,
    bool ProcessTreeKillRequested,
    bool StreamsDrained,
    string? KillError);

internal static class BoundedProcess
{
    internal const int MaximumCapturedCharacters = 64 * 1024;

    public static BoundedProcessResult Run(
        ProcessStartInfo startInfo,
        TimeSpan executionTimeout,
        TimeSpan terminationTimeout)
        => RunCoreAsync(startInfo, executionTimeout, terminationTimeout).GetAwaiter().GetResult();

    private static async Task<BoundedProcessResult> RunCoreAsync(
        ProcessStartInfo startInfo,
        TimeSpan executionTimeout,
        TimeSpan terminationTimeout)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (!startInfo.RedirectStandardOutput || !startInfo.RedirectStandardError)
            throw new ArgumentException("Bounded processes must redirect both standard output and standard error.", nameof(startInfo));
        if (executionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(executionTimeout));
        if (terminationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(terminationTimeout));

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start process '{startInfo.FileName}'.");
        var processId = process.Id;
        var outputCapture = new BoundedTextCapture(MaximumCapturedCharacters);
        var errorCapture = new BoundedTextCapture(MaximumCapturedCharacters);
        var outputTask = DrainAsync(process.StandardOutput, outputCapture);
        var errorTask = DrainAsync(process.StandardError, errorCapture);

        using var executionCts = new CancellationTokenSource(executionTimeout);
        try
        {
            await process.WaitForExitAsync(executionCts.Token).ConfigureAwait(false);
            var streamsDrained = await DrainStreamsAsync(
                outputTask, errorTask, terminationTimeout).ConfigureAwait(false);
            return Result(processId, process.ExitCode, outputCapture, errorCapture,
                timedOut: false, rootExited: true, processTreeKillRequested: false,
                streamsDrained: streamsDrained, killError: null);
        }
        catch (OperationCanceledException) when (executionCts.IsCancellationRequested)
        {
            if (HasExited(process))
            {
                var raceStreamsDrained = await DrainStreamsAsync(
                    outputTask, errorTask, terminationTimeout).ConfigureAwait(false);
                return Result(processId, ExitCode(process), outputCapture, errorCapture,
                    timedOut: false, rootExited: true, processTreeKillRequested: false,
                    streamsDrained: raceStreamsDrained, killError: null);
            }

            var processTreeKillRequested = false;
            string? killError = null;
            try
            {
                process.Kill(entireProcessTree: true);
                processTreeKillRequested = true;
            }
            catch (InvalidOperationException) when (HasExited(process))
            {
                // The process exited between the timeout and the kill request.
            }
            catch (Exception exception) when (exception is Win32Exception or NotSupportedException or InvalidOperationException)
            {
                killError = exception.Message;
            }

            var rootExited = await WaitForExitAsync(process, terminationTimeout).ConfigureAwait(false);
            var streamsDrained = await DrainStreamsAsync(
                outputTask, errorTask, terminationTimeout).ConfigureAwait(false);
            return Result(processId, rootExited ? ExitCode(process) : null, outputCapture, errorCapture,
                timedOut: true, rootExited: rootExited, processTreeKillRequested: processTreeKillRequested,
                streamsDrained: streamsDrained, killError: killError);
        }
    }

    private static BoundedProcessResult Result(
        int processId,
        int? exitCode,
        BoundedTextCapture outputCapture,
        BoundedTextCapture errorCapture,
        bool timedOut,
        bool rootExited,
        bool processTreeKillRequested,
        bool streamsDrained,
        string? killError)
    {
        var output = outputCapture.Snapshot();
        var error = errorCapture.Snapshot();
        return new(processId, exitCode, output.Text, error.Text, output.Truncated, error.Truncated,
            timedOut, rootExited, processTreeKillRequested, streamsDrained, killError);
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        if (HasExited(process)) return true;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return HasExited(process);
        }
    }

    private static async Task<bool> DrainStreamsAsync(
        Task outputTask,
        Task errorTask,
        TimeSpan timeout)
    {
        var streams = Task.WhenAll(outputTask, errorTask);
        try
        {
            await streams.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            ObserveFault(streams);
            return false;
        }
    }

    private static async Task DrainAsync(StreamReader reader, BoundedTextCapture capture)
    {
        var buffer = ArrayPool<char>.Shared.Rent(4_096);
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
                capture.Append(buffer.AsSpan(0, read));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    private static int? ExitCode(Process process)
    {
        try { return process.HasExited ? process.ExitCode : null; }
        catch (InvalidOperationException) { return null; }
    }

    private static void ObserveFault(Task task)
        => _ = task.ContinueWith(static completed => _ = completed.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private sealed class BoundedTextCapture
    {
        private readonly char[] _buffer;
        private readonly object _gate = new();
        private int _start;
        private int _count;
        private bool _truncated;

        public BoundedTextCapture(int capacity) => _buffer = new char[capacity];

        public void Append(ReadOnlySpan<char> value)
        {
            lock (_gate)
            {
                if (value.Length >= _buffer.Length)
                {
                    _truncated |= _count != 0 || value.Length > _buffer.Length;
                    value[^_buffer.Length..].CopyTo(_buffer);
                    _start = 0;
                    _count = _buffer.Length;
                    return;
                }

                var overflow = Math.Max(0, _count + value.Length - _buffer.Length);
                if (overflow != 0)
                {
                    _start = (_start + overflow) % _buffer.Length;
                    _count -= overflow;
                    _truncated = true;
                }

                var end = (_start + _count) % _buffer.Length;
                var first = Math.Min(value.Length, _buffer.Length - end);
                value[..first].CopyTo(_buffer.AsSpan(end));
                value[first..].CopyTo(_buffer);
                _count += value.Length;
            }
        }

        public (string Text, bool Truncated) Snapshot()
        {
            lock (_gate)
            {
                if (_count == 0) return (string.Empty, _truncated);
                var value = new char[_count];
                var first = Math.Min(_count, _buffer.Length - _start);
                _buffer.AsSpan(_start, first).CopyTo(value);
                _buffer.AsSpan(0, _count - first).CopyTo(value.AsSpan(first));
                return (new string(value), _truncated);
            }
        }
    }
}
