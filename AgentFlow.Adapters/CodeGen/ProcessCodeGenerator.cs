using System.Diagnostics;
using System.Text;
using AgentFlow.Adapters.Testing;
using AgentFlow.Core.Abstractions;
using AgentFlow.Core.Git;
using AgentFlow.Core.Models;
using AgentFlow.Shared.Configuration;
using AgentFlow.Shared.Helpers;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace AgentFlow.Adapters.CodeGen
{
    /// <summary>
    /// Runs code generation by invoking run-dev.cmd or run-dev.ps1 in the Cursor rules directory.
    /// Passes WORK_ITEM_JSON_PATH, REPO_PATH, GUIDANCE_DIR, PRD_PATH, and GIT_ALLOWLIST so the LLM implements from the PRD using only allowed git commands.
    /// </summary>
    public sealed class ProcessCodeGenerator : ICodeGenerator
    {
        // Services
        private readonly WorkspaceConfig _workspace;

        public ProcessCodeGenerator(IOptions<WorkspaceConfig> workspace)
        {
            _workspace = workspace.Value;
        }

        /// <summary>
        /// Invokes <c>run-dev.cmd</c> or <c>run-dev.ps1</c> in <see cref="WorkspaceConfig.CursorRulesDir"/> (resolved under the app base directory when relative),
        /// writes <paramref name="workItem"/> to a temporary JSON file, and runs the script with working directory <paramref name="repoPath"/>.
        /// Environment variables set for the child process include <c>WORK_ITEM_JSON_PATH</c>, <c>REPO_PATH</c>, <c>GUIDANCE_DIR</c>, <c>PRD_PATH</c>,
        /// <c>GIT_ALLOWLIST</c>, <c>GIT_ALLOWLIST_DESCRIPTION</c>, and optional <c>AGENTFLOW_HAS_TEST_PROJECT</c> / <c>AGENTFLOW_TEST_PROJECT_PATH</c> when a test project is found.
        /// </summary>
        /// <param name="workItem">Work item serialized to JSON for <c>WORK_ITEM_JSON_PATH</c>.</param>
        /// <param name="repoPath">Absolute path to the repository root (process working directory).</param>
        /// <param name="prdPath">Absolute path to the PRD file passed as <c>PRD_PATH</c>.</param>
        /// <param name="ct">Cancellation token observed while writing the temp work item file and waiting for the child process to exit.</param>
        /// <returns>
        /// <see cref="CodeGeneratorResult"/> with <see cref="CodeGeneratorResult.Success"/> <see langword="true"/> when the process exits <c>0</c>;
        /// exit code <c>2</c> returns <see cref="CodeGeneratorResult.Success"/> <see langword="false"/> with <see cref="CodeGeneratorResult.QuestionsForDev"/> set from combined stdout/stderr;
        /// any other non-zero exit or failure to start returns <see langword="false"/> with a message. Other exceptions are caught and returned as a failed result except <see cref="OperationCanceledException"/>.
        /// </returns>
        /// <exception cref="OperationCanceledException">Rethrown when cancellation is requested while waiting for the process or writing temp files.</exception>
        public async Task<CodeGeneratorResult> GenerateAsync(WorkItem workItem, string repoPath, string prdPath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_workspace.CursorRulesDir))
                return new CodeGeneratorResult(true, "No Cursor rules directory configured. Set AgentFlow:Workspace:CursorRulesDir.");

            var rulesDir = _workspace.CursorRulesDir.Trim();
            var rulesPath = Path.IsPathRooted(rulesDir)
                ? rulesDir
                : Path.Combine(AppContext.BaseDirectory, rulesDir);
            if (!Directory.Exists(rulesPath))
                return new CodeGeneratorResult(false, $"Cursor rules directory not found: {rulesPath}");

            var fullRulesPath = Path.GetFullPath(rulesPath);
            var runScript = Path.Combine(fullRulesPath, "run-dev.cmd");
            if (!File.Exists(runScript))
                runScript = Path.Combine(fullRulesPath, "run-dev.ps1");
            if (!File.Exists(runScript))
                return new CodeGeneratorResult(true, "Add run-dev.cmd or run-dev.ps1 to CursorRulesDir to run code generation.");

            string? tempPath = null;
            try
            {
                var json = JsonConvert.SerializeObject(new
                {
                    workItem.Id,
                    workItem.Title,
                    workItem.Description,
                    workItem.Fields,
                    Attachments = workItem.Attachments?.Select(a => new { a.Id, a.Name, a.Url }).ToList(),
                    Comments    = workItem.Comments?.Select(c => new { c.Author, c.CreatedAt, c.Text }).ToList()
                }, Formatting.Indented);

                tempPath = Path.Combine(Path.GetTempPath(), $"agentflow-wi-{workItem.Id}-{Guid.NewGuid():N}.json");
                await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, ct).ConfigureAwait(false);

                var isPs1 = runScript.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
                var startInfo = new ProcessStartInfo
                {
                    FileName               = isPs1 ? "powershell.exe" : "cmd.exe",
                    Arguments              = isPs1 ? $"-NoProfile -ExecutionPolicy Bypass -File \"{runScript}\"" : $"/c \"{runScript}\"",
                    WorkingDirectory       = repoPath,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };

                startInfo.Environment["WORK_ITEM_JSON_PATH"]       = tempPath;
                startInfo.Environment["REPO_PATH"]                 = repoPath;
                startInfo.Environment["GUIDANCE_DIR"]              = fullRulesPath;
                startInfo.Environment["PRD_PATH"]                  = prdPath;
                startInfo.Environment["GIT_ALLOWLIST"]             = GitAllowlist.GetAllowedListForEnv();
                startInfo.Environment["GIT_ALLOWLIST_DESCRIPTION"] = GitAllowlist.GetAllowedCommandsDescription();

                var testTarget = TestProjectLocator.TryFindTestTarget(repoPath, _workspace.TestProjectPath);
                startInfo.Environment["AGENTFLOW_HAS_TEST_PROJECT"] = testTarget != null ? "true" : "false";
                if (testTarget != null)
                    startInfo.Environment["AGENTFLOW_TEST_PROJECT_PATH"] = testTarget;

                Console.WriteLine($"[AgentFlow] Starting code generation (Cursor CLI). Work item: {workItem.Id}, PRD: {prdPath}");
                Console.WriteLine("[AgentFlow] Cursor CLI output below (streaming)...");

                using var process = Process.Start(startInfo);
                if (process == null)
                    return new CodeGeneratorResult(false, "Failed to start code generation process.");

                var stdoutBuilder    = new StringBuilder();
                var stderrBuilder    = new StringBuilder();
                var stopwatch        = Stopwatch.StartNew();
                var firstOutputGuard = new ProcessOutputFx.FirstOutputGuard();
                var stdoutTask       = ProcessOutputFx.StreamOutputToConsoleAsync(process.StandardOutput, Console.Out, stdoutBuilder, firstOutputGuard, ct);
                var stderrTask       = ProcessOutputFx.StreamOutputToConsoleAsync(process.StandardError, Console.Error, stderrBuilder, firstOutputGuard, ct);
                var heartbeatTask    = ProcessOutputFx.HeartbeatWhileRunningAsync(process, stopwatch, ct);

                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                stopwatch.Stop();
                await heartbeatTask.ConfigureAwait(false);
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

                var log = (stdoutBuilder.ToString().TrimEnd() + (stderrBuilder.Length > 0 ? "\n" + stderrBuilder.ToString().TrimEnd() : "")).Trim();
                if (process.ExitCode == 2)
                {
                    Console.WriteLine("[AgentFlow] Agent has questions for the dev; they will be posted to the work item.");
                    return new CodeGeneratorResult(false, log, QuestionsForDev: log);
                }
                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"[AgentFlow] Code generation exited with code {process.ExitCode}.");
                    return new CodeGeneratorResult(false, $"Code generation exited with code {process.ExitCode}. {log}");
                }

                Console.WriteLine("[AgentFlow] Code generation completed successfully.");
                return new CodeGeneratorResult(true, log);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new CodeGeneratorResult(false, ex.Message);
            }
            finally
            {
                if (tempPath != null && File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* ignore */ }
                }
            }
        }

        // Output streaming and heartbeat are implemented in AgentFlow.Shared.Helpers.ProcessOutputUtilities.
    }
}
