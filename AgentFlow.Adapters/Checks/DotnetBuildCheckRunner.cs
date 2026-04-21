using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AgentFlow.Core.Abstractions;
using AgentFlow.Shared.Helpers;

namespace AgentFlow.Adapters.Checks;

/// <summary>
/// Runs <c>dotnet build</c> as the additional check step after tests (build verification, compiler errors).
/// </summary>
public sealed class DotnetBuildCheckRunner : ICheckRunner
{
    /// <inheritdoc />
    public async Task<(bool Success, string Log)> RunAsync(string localPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
            return (false, "Repository path is missing or not a directory.");

        var repoRoot = Path.GetFullPath(localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var sln      = Directory.GetFiles(repoRoot, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();

        var args = sln != null
            ? $"build \"{sln}\" --verbosity normal"
            : "build --verbosity normal";

        var header = new StringBuilder();
        header.AppendLine(sln != null
            ? $"[AgentFlow] dotnet build (solution): {sln}"
            : "[AgentFlow] dotnet build (folder default; no .sln in repo root)");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName               = "dotnet",
                Arguments              = args,
                WorkingDirectory       = repoRoot,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return (false, header + "Failed to start process: Process.Start returned null.");

            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                var outTxt = await stdoutTask.ConfigureAwait(false);
                var errTxt = await stderrTask.ConfigureAwait(false);

                var log = new StringBuilder(header.ToString());
                log.AppendLine(outTxt);
                if (!string.IsNullOrWhiteSpace(errTxt))
                    log.AppendLine(errTxt);

                var ok = process.ExitCode == 0;
                if (!ok)
                    log.Insert(0, $"dotnet build exited with code {process.ExitCode}.\n");

                return (ok, log.ToString().TrimEnd());
            }
            catch (OperationCanceledException)
            {
                ProcessFx.TryKillProcess(process);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, header + ProcessFx.FormatExecutionError("dotnet build", ex));
        }
    }
}
