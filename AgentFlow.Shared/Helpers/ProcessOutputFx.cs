using System.Diagnostics;
using System.Text;

namespace AgentFlow.Shared.Helpers;

/// <summary>
/// Shared helpers for streaming long-running child-process output to the console while still capturing it for logs.
/// Used by PRD and code generation steps that invoke Cursor CLI scripts.
/// </summary>
public static class ProcessOutputFx
{
    /// <summary>
    /// Updates a single console line every second with elapsed time while the process is running, using carriage return
    /// so the same line is overwritten. Provides visibility when child process output is buffered (e.g. when not a TTY).
    /// </summary>
    /// <param name="process">The child process to monitor until it exits.</param>
    /// <param name="stopwatch">Started when the process was launched; used for elapsed seconds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A task that completes when the process has exited or cancellation is requested.
    /// </returns>
    public static async Task HeartbeatWhileRunningAsync(Process process, Stopwatch stopwatch, CancellationToken ct)
    {
        const int intervalSeconds = 1;
        try
        {
            while (!process.HasExited && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct).ConfigureAwait(false);
                if (process.HasExited) break;
                var elapsed = (int)stopwatch.Elapsed.TotalSeconds;
                Console.Write("\r[AgentFlow] Cursor CLI still running... {0}s   ", elapsed);
            }
            Console.WriteLine();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Thread-safe guard so only the first line of output (from either stream) gets a leading newline,
    /// ensuring buffered output does not run onto the heartbeat line.
    /// </summary>
    public sealed class FirstOutputGuard
    {
        private int _needNewline = 1;

        /// <summary>
        /// Returns whether this is the first line of output from either stream so a leading newline can be emitted.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> the first time called; <see langword="false"/> on subsequent calls.
        /// </returns>
        public bool ConsumeNewline() => Interlocked.Exchange(ref _needNewline, 0) == 1;
    }

    /// <summary>
    /// Reads a process stream line-by-line, writing each line to the console and appending to the builder
    /// so the user sees output in real time and we still capture it for the final result.
    /// </summary>
    /// <param name="reader">Process standard output or error stream reader.</param>
    /// <param name="console">Console writer (Out or Error) to stream lines to.</param>
    /// <param name="builder">StringBuilder to append lines to for the final log.</param>
    /// <param name="firstOutputGuard">Guard so the first line from either stream gets a leading newline before the heartbeat line.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A task that completes when the stream is closed.
    /// </returns>
    public static async Task StreamOutputToConsoleAsync(
        StreamReader reader,
        TextWriter console,
        StringBuilder builder,
        FirstOutputGuard firstOutputGuard,
        CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (firstOutputGuard.ConsumeNewline())
                console.Write("\r\n");
            console.WriteLine(line);
            builder.AppendLine(line);
        }
    }
}

