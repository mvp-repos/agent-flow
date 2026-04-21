using System.Diagnostics;
using System.Text;
using AgentFlow.Core.Abstractions;
using AgentFlow.Core.Models;
using AgentFlow.Shared.Helpers;
using AgentFlow.Shared.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace AgentFlow.Adapters.Draft
{
    /// <summary>
    /// Runs PRD generation by invoking the script in the Cursor rules directory (run-prd.cmd or run-prd.ps1).
    /// Passes WORK_ITEM_JSON_PATH, REPO_PATH, and GUIDANCE_DIR (the Cursor rules directory). That directory
    /// holds all Cursor rule files (PRD prompt, coding patterns, user rules); Cursor reads them before making changes.
    /// </summary>
    public sealed class ProcessDraftGenerator : IDraftGenerator
    {
        // Services
        private readonly WorkspaceConfig _workspace;

        public ProcessDraftGenerator(IOptions<WorkspaceConfig> workspace)
        {
            _workspace = workspace.Value;
        }

        /// <inheritdoc />
        public async Task<DraftGeneratorResult> GeneratePrdAsync(WorkItem workItem, string repoPath, CancellationToken ct)
        {
            // Require Cursor rules directory; without it we cannot run the PRD script.
            if (string.IsNullOrWhiteSpace(_workspace.CursorRulesDir))
                return new DraftGeneratorResult(true, "No Cursor rules directory configured. Set AgentFlow:Workspace:CursorRulesDir.");

            var rulesDir = _workspace.CursorRulesDir.Trim();

            // Resolve path: relative paths are from the application directory (where exe and cursor-rules live after init).
            var rulesPath = Path.IsPathRooted(rulesDir)
                ? rulesDir
                : Path.Combine(AppContext.BaseDirectory, rulesDir);
            if (!Directory.Exists(rulesPath))
                return new DraftGeneratorResult(false, $"Cursor rules directory not found: {rulesPath}");

            var fullRulesPath = Path.GetFullPath(rulesPath);
            // Prefer run-prd.cmd, fall back to run-prd.ps1; script receives work item path, repo path, and guidance dir via env.
            var runScript = Path.Combine(fullRulesPath, "run-prd.cmd");
            if (!File.Exists(runScript))
                runScript = Path.Combine(fullRulesPath, "run-prd.ps1");
            if (!File.Exists(runScript))
                return new DraftGeneratorResult(true, "Add run-prd.cmd or run-prd.ps1 to CursorRulesDir to run PRD generation.");

            string? tempPath = null;
            try
            {
                // Write work item data to a temp JSON file so the script can pass its path to the Cursor agent.
                // Include Comments when present (e.g. dev answers after agent asked questions) so the agent sees them on re-run.
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

                // Start script under cmd or PowerShell; redirect stdout/stderr so we can stream and capture output.
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

                startInfo.Environment["WORK_ITEM_JSON_PATH"] = tempPath;
                startInfo.Environment["REPO_PATH"]           = repoPath;
                startInfo.Environment["GUIDANCE_DIR"]        = fullRulesPath;

                // Visibility: log start and that Cursor CLI output will stream (or heartbeat will show elapsed time if CLI buffers).
                Console.WriteLine($"[AgentFlow] Starting PRD generation (Cursor CLI). Work item: {workItem.Id}, Repo: {repoPath}");
                Console.WriteLine("[AgentFlow] Cursor CLI output below (streaming)...");

                using var process = Process.Start(startInfo);
                if (process == null)
                    return new DraftGeneratorResult(false, "Failed to start PRD generation process.");

                // Stream stdout/stderr to console and capture for the result; heartbeat updates one line with elapsed time while process runs.
                // First line of agent output gets a leading newline so it does not run onto the heartbeat line.
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

                // Build full log from captured output. Exit code 2 = agent has questions for the dev (post to ticket and re-run with agent:regenerate-prd).
                var log = (stdoutBuilder.ToString().TrimEnd() + (stderrBuilder.Length > 0 ? "\n" + stderrBuilder.ToString().TrimEnd() : "")).Trim();
                if (process.ExitCode == 2)
                {
                    Console.WriteLine("[AgentFlow] Agent has questions for the dev; they will be posted to the work item.");
                    return new DraftGeneratorResult(false, log, QuestionsForDev: log);
                }
                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"[AgentFlow] PRD generation exited with code {process.ExitCode}.");
                    return new DraftGeneratorResult(false, $"PRD generation exited with code {process.ExitCode}. {log}");
                }

                Console.WriteLine("[AgentFlow] PRD generation completed successfully.");
                var prdPath = ParsePrdPathFromOutput(log);
                return new DraftGeneratorResult(true, log, PrdPath: prdPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new DraftGeneratorResult(false, ex.Message);
            }
            finally
            {
                // Remove temp work item JSON so we do not leave files in %TEMP%.
                if (tempPath != null && File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* ignore */ }
                }
            }
        }

        /// <summary>
        /// Parses the PRD path from the LLM output. The agent should output a line: AGENTFLOW_PRD_PATH=&lt;path&gt; (path relative to repo or absolute).
        /// </summary>
        /// <param name="output">Full stdout/stderr from the PRD generation process.</param>
        /// <returns>
        /// The path if found (trimmed, quotes removed); <see langword="null"/> otherwise.
        /// </returns>
        private static string? ParsePrdPathFromOutput(string output)
        {
            const string prefix = "AGENTFLOW_PRD_PATH=";
            if (string.IsNullOrWhiteSpace(output)) return null;
            var line = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (line == null) return null;
            var path = line.Substring(prefix.Length).Trim().Trim('"', '\'');
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        /// <summary>
        /// Updates a single console line every second with elapsed time while the process is running, using
        /// carriage return so the same line is overwritten. Provides visibility when Cursor CLI buffers output (e.g. when not a TTY).
        /// </summary>
        /// <param name="process">The child process to monitor until it exits.</param>
        /// <param name="stopwatch">Started when the process was launched; used for elapsed seconds.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A task that completes when the process has exited or cancellation is requested.
        /// </returns>
        // Output streaming and heartbeat are implemented in AgentFlow.Shared.Helpers.ProcessOutputUtilities.
    }
}
