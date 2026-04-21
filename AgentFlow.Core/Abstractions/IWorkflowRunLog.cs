namespace AgentFlow.Core.Abstractions
{
    /// <summary>
    /// Per-run workflow diagnostics written to a log (file, console, or no-op). The workflow records normal steps,
    /// failures with optional exceptions, and configuration or environment issues (missing tags, unclear requirements,
    /// unresolved repo, BMAD missing, and similar). Implementations are supplied by the host (e.g. Serilog file sink in the CLI);
    /// register a different implementation via dependency injection to change behavior.
    /// </summary>
    /// <remarks>
    /// Implementations that hold file handles or other resources must release them in <see cref="IDisposable.Dispose"/>.
    /// The workflow and CLI call dispose when the run completes or after startup validation logging.
    /// </remarks>
    public interface IWorkflowRunLog : IDisposable
    {
        /// <summary>
        /// Gets the absolute path to this run's log file when file logging is enabled; otherwise <see langword="null"/>.
        /// </summary>
        /// <value>The path, or <see langword="null"/> when logging is disabled or not file-based.</value>
        string? LogFilePath { get; }

        /// <summary>
        /// Records a workflow step such as intake, gate result, PRD, test, or approval poll.
        /// </summary>
        /// <param name="phase">Short name of the step (e.g. <c>Intake</c>, <c>dotnet test</c>).</param>
        /// <param name="detail">Optional extra context (title, path, outcome). Omit when the phase alone is enough.</param>
        void Step(string phase, string? detail = null);

        /// <summary>
        /// Records an error. Pass <paramref name="exception"/> when the failure is tied to a thrown exception.
        /// </summary>
        /// <param name="message">Human-readable description of what failed.</param>
        /// <param name="exception">Optional exception to include in the log output.</param>
        void Error(string message, Exception? exception = null);

        /// <summary>
        /// Records a configuration or environment problem: missing or invalid settings, work item not ready for automation,
        /// repository or branch not available, BMAD not installed, or similar. These typically correlate with a paused or skipped run.
        /// </summary>
        /// <param name="message">What is wrong and what to fix, if known.</param>
        void Configuration(string message);
    }
}
