using System.IO;
using AgentFlow.Adapters.AzureDevOps;
using AgentFlow.Cli.Fakes;
using AgentFlow.Core.Abstractions;
using AgentFlow.Core.Models;
using AgentFlow.Core.Workflow;
using AgentFlow.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentFlow.Cli.Commands
{
    /// <summary>
    /// Handles the <c>run</c> command: parses <c>--provider</c>, <c>--workitem</c>, and <c>--dry-run</c>, validates configuration,
    /// builds a <see cref="WorkflowEngine"/> with the chosen <see cref="IWorkItemProvider"/>, runs <see cref="WorkflowEngine.RunAsync"/>,
    /// prints the outcome and step log path, and returns a process exit code.
    /// </summary>
    public sealed class RunCommandHandler
    {
        // Services
        private readonly IServiceProvider       _services;
        private readonly PollingConfig          _cfgPoll;
        private readonly AgentFlowOptions       _cfgAgent;
        private readonly IWorkflowRunLogFactory _runLogFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="RunCommandHandler"/> class.
        /// </summary>
        /// <param name="serviceProvider">Root DI container used to resolve <see cref="WorkflowEngine"/> dependencies and provider implementations.</param>
        /// <param name="cfgPoll">Approval polling interval and max polls (from configuration).</param>
        /// <param name="cfgAgent">Agent and workspace settings used for validation before the workflow starts.</param>
        /// <param name="runLogFactory">Creates run logs for startup validation failures and for the workflow engine.</param>
        public RunCommandHandler(
            IServiceProvider serviceProvider,
            IOptions<PollingConfig> cfgPoll,
            IOptions<AgentFlowOptions> cfgAgent,
            IWorkflowRunLogFactory runLogFactory)
        {
            _services      = serviceProvider;
            _cfgPoll       = cfgPoll.Value;
            _cfgAgent      = cfgAgent.Value;
            _runLogFactory = runLogFactory;
        }

        /// <summary>
        /// Parses CLI arguments (<c>--provider</c>, <c>--workitem</c>, <c>--dry-run</c>), resolves <see cref="IWorkItemProvider"/> (fake vs ADO),
        /// validates configuration, runs <see cref="WorkflowEngine.RunAsync"/>, prints state and <see cref="WorkflowEngine.LastStepLogPath"/> when present,
        /// and maps the outcome to a process exit code.
        /// </summary>
        /// <param name="args">Raw command-line arguments (e.g. <c>--provider</c>, <c>--workitem</c>, <c>--dry-run</c>).</param>
        /// <returns>
        /// Exit code: <c>0</c> when the run ends in <see cref="RunState.Done"/>, <see cref="RunState.Skipped"/>, or <see cref="RunState.StoppedByUser"/>; <c>1</c> on <see cref="RunState.Failed"/>, configuration or workspace errors, or invalid arguments.
        /// </returns>
        /// <remarks>
        /// Builds <see cref="WorkflowEngine"/> with <see cref="ActivatorUtilities.CreateInstance{T}(IServiceProvider, object[])"/> so the resolved provider is injected.
        /// Calls <see cref="ConfigurationValidator.ValidateOrThrow"/>, <see cref="ConfigurationValidator.ValidateFakeRunOrThrow"/>, or <see cref="ConfigurationValidator.EnsureWorkspaceExists"/> as appropriate and logs failures via <see cref="IWorkflowRunLogFactory"/> before the engine starts.
        /// Missing <c>--workitem</c> or an unknown <c>--provider</c> writes to the console and returns <c>1</c> without throwing.
        /// </remarks>
        public async Task<int> ExecuteAsync(string[] args)
        {
            string provider;
            string workItemId;
            try
            {
                provider   = GetArg(args, "--provider") ?? "fake";
                workItemId = GetArg(args, "--workitem") ?? throw new ArgumentException("Missing --workitem <id>");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(ex.Message);
                return 1;
            }

            IWorkItemProvider workItemProvider;
            try
            {
                workItemProvider = ResolveWorkItemProvider(provider);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(ex.Message);
                return 1;
            }

            var dryRun = HasFlag(args, "--dry-run");

            var preRunId = Guid.NewGuid().ToString("N");
            try
            {
                if (string.Equals(provider, "ado", StringComparison.OrdinalIgnoreCase) || string.Equals(provider, "azuredevops", StringComparison.OrdinalIgnoreCase))
                {
                    ConfigurationValidator.ValidateOrThrow(_cfgAgent);
                    ConfigurationValidator.EnsureWorkspaceExists(_cfgAgent);
                }
                else if (string.Equals(provider, "fake", StringComparison.OrdinalIgnoreCase))
                {
                    ConfigurationValidator.ValidateFakeRunOrThrow(_cfgAgent);
                    ConfigurationValidator.EnsureWorkspaceExists(_cfgAgent);
                }
            }
            catch (InvalidOperationException ex)
            {
                WriteStartupConfigurationLog(preRunId, workItemId, ex);
                Console.WriteLine(ex.Message);
                return 1;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                WriteStartupWorkspaceError(preRunId, workItemId, ex);
                Console.WriteLine(ex.Message);
                return 1;
            }

            var options = new WorkflowOptions(
                DryRun              : dryRun,
                ApprovalPollInterval: TimeSpan.FromSeconds(_cfgPoll.ApprovalPollSeconds),
                ApprovalMaxPolls    : _cfgPoll.ApprovalMaxPolls
            );

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            // Activates workflow enging
            var engine = ActivatorUtilities.CreateInstance<WorkflowEngine>(_services, workItemProvider);

            // Runs workflow engine
            var run = await engine.RunAsync(provider, workItemId, options, cts.Token).ConfigureAwait(false);

            Console.WriteLine($"\nRun completed: {run.RunId}");
            Console.WriteLine($"State: {run.State}");
            Console.WriteLine($"Message: {run.Message}");
            if (!string.IsNullOrWhiteSpace(engine.LastStepLogPath))
                Console.WriteLine($"Step log: {engine.LastStepLogPath}");

            return MapExitCode(run.State);
        }

        /// <summary>
        /// Writes <see cref="ConfigurationValidator"/> / appsettings validation failures to a run log file when
        /// <see cref="WorkspaceRootForLogging"/> returns a path (same layout as workflow logs: <c>agentflow-logs/&lt;runId&gt;/log.txt</c>).
        /// </summary>
        /// <param name="runId">Correlation id for this CLI attempt (separate from the workflow engine run id on success).</param>
        /// <param name="workItemId">Work item id from the command line.</param>
        /// <param name="ex">Exception from <see cref="ConfigurationValidator.ValidateOrThrow"/> or <see cref="ConfigurationValidator.ValidateFakeRunOrThrow"/>.</param>
        private void WriteStartupConfigurationLog(string runId, string workItemId, InvalidOperationException ex)
        {
            var root = WorkspaceRootForLogging();
            using var log = _runLogFactory.Create(root, runId, workItemId);
            log.Configuration(ex.Message);
            if (!string.IsNullOrWhiteSpace(log.LogFilePath))
                Console.WriteLine($"Step log: {log.LogFilePath}");
        }

        /// <summary>
        /// Logs failures from <see cref="ConfigurationValidator.EnsureWorkspaceExists"/> (e.g. <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>).
        /// </summary>
        /// <param name="runId">Correlation id for this CLI attempt.</param>
        /// <param name="workItemId">Work item id from the command line.</param>
        /// <param name="ex">Exception thrown when creating the workspace directory.</param>
        private void WriteStartupWorkspaceError(string runId, string workItemId, Exception ex)
        {
            var root = WorkspaceRootForLogging();
            using var log = _runLogFactory.Create(root, runId, workItemId);
            log.Error("Could not create or access Workspace:RootPath.", ex);
            if (!string.IsNullOrWhiteSpace(log.LogFilePath))
                Console.WriteLine($"Step log: {log.LogFilePath}");
        }

        /// <summary>
        /// Returns <see cref="WorkspaceConfig.RootPath"/> when set, for locating startup log files; otherwise <see langword="null"/> (no file log).
        /// </summary>
        private string? WorkspaceRootForLogging()
        {
            var r = _cfgAgent.Workspace?.RootPath;
            return string.IsNullOrWhiteSpace(r) ? null : r.Trim();
        }

        /// <summary>
        /// Maps a terminal workflow state to a process exit code for scripts and CI.
        /// </summary>
        /// <param name="state">Final <see cref="RunState"/> from <see cref="WorkflowEngine.RunAsync"/>.</param>
        /// <returns><c>0</c> for success-style outcomes; <c>1</c> for failure or non-terminal states.</returns>
        private static int MapExitCode(RunState state) =>
            state switch
            {
                RunState.Done           => 0,
                RunState.Skipped        => 0,
                RunState.StoppedByUser  => 0,
                RunState.Failed         => 1,
                _                       => 1
            };

        /// <summary>
        /// Resolves the work item provider implementation for the CLI <c>--provider</c> value.
        /// </summary>
        /// <param name="providerName">Raw value from <c>--provider</c> (e.g. <c>fake</c>, <c>ado</c>).</param>
        /// <returns>
        /// <see cref="FakeWorkItemProvider"/> or <see cref="AzureDevOps.AzureDevOpsWorkItemProvider"/> depending on <paramref name="providerName"/>.
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="providerName"/> is not a supported provider.</exception>
        private IWorkItemProvider ResolveWorkItemProvider(string providerName)
        {
            var p = providerName.Trim();
            if (string.Equals(p, "fake", StringComparison.OrdinalIgnoreCase))
                return _services.GetRequiredService<FakeWorkItemProvider>();
            if (string.Equals(p, "ado", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "azuredevops", StringComparison.OrdinalIgnoreCase))
                return _services.GetRequiredService<AzureDevOpsWorkItemProvider>();
            throw new ArgumentException($"Unknown --provider '{providerName}'. Use fake, ado, or azuredevops.");
        }

        /// <summary>
        /// Helper method to retrieve the value of a specific argument from the command-line arguments array.
        /// </summary>
        /// <param name="args">The command-line arguments.</param>
        /// <param name="name">The name of the argument to retrieve (e.g. "--provider").</param>
        /// <returns>
        /// The value of the specified argument if found; otherwise, <see langword="null"/>. If the argument is present but does not have a corresponding value, <see langword="null"/> is also returned.
        /// </returns>
        private static string? GetArg(string[] args, string name)
        {
            var idx = Array.IndexOf(args, name);
            if (idx < 0) return null;
            if (idx + 1 >= args.Length) return null;
            return args[idx + 1];
        }

        /// <summary>
        /// Helper method to check if a specific flag (argument without a value) is present in the command-line arguments array.
        /// </summary>
        /// <param name="args">The command-line arguments.</param>
        /// <param name="name">The name of the argument to retrieve (e.g. "--dry-run").</param>
        /// <returns>
        /// <see langword="true"/> if the specified flag is present in the arguments; otherwise, <see langword="false"/>.
        /// </returns>
        private static bool HasFlag(string[] args, string name)
        {
            return args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
