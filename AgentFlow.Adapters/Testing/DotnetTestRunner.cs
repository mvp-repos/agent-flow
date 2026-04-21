using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AgentFlow.Core.Abstractions;
using AgentFlow.Shared.Configuration;
using AgentFlow.Shared.Helpers;
using Microsoft.Extensions.Options;

namespace AgentFlow.Adapters.Testing;

/// <summary>
/// Implementation of <see cref="ILocalTestRunner"/> that runs <c>dotnet test</c> against a discovered or configured 
/// test project or solution under the repository root. Skips with success when no test target is found.
/// </summary>
public sealed class DotnetTestRunner : ILocalTestRunner
{
    // Services
    private readonly WorkspaceConfig _workspace;

    public DotnetTestRunner(IOptions<WorkspaceConfig> workspace)
    {
        _workspace = workspace.Value;
    }

    /// <inheritdoc/>
    public async Task<(bool Success, string Log)> RunAsync(string repositoryRoot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
            return (false, "Repository root is missing or not a directory.");

        // Finds the test target to run
        var target = TestProjectLocator.TryFindTestTarget(repositoryRoot, _workspace.TestProjectPath);
        if (string.IsNullOrEmpty(target))
            return (true, "No test project or solution found; dotnet test skipped. Configure AgentFlow:Workspace:TestProjectPath or add a *Test*.csproj / .sln.");

        if (!File.Exists(target))
            return (false, $"Test target path does not exist: {target}");

        var header = new StringBuilder();
        header.AppendLine($"[AgentFlow] dotnet test target: {target}");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName               = "dotnet",
                Arguments              = $"test \"{target}\" --verbosity normal",
                WorkingDirectory       = repositoryRoot,
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
                    log.Insert(0, $"dotnet test exited with code {process.ExitCode}.\n");

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
            return (false, header + ProcessFx.FormatExecutionError("dotnet test", ex));
        }
    }
}
