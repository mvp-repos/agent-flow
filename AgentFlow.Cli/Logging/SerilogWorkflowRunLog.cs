using AgentFlow.Core.Abstractions;
using Serilog;
using Serilog.Core;

namespace AgentFlow.Cli.Logging
{
    /// <summary>
    /// File-backed <see cref="IWorkflowRunLog"/> using Serilog. Writes to a single file per run:
    /// <c>{workspaceRoot}/agentflow-logs/{runId}/log.txt</c>. Steps use the Information level, configuration issues use Warning,
    /// errors use Error (with optional exception). Disposing flushes and releases the Serilog logger.
    /// </summary>
    public sealed class SerilogWorkflowRunLog : IWorkflowRunLog
    {
        private readonly Logger _log;
        private readonly string _path;
        private bool            _disposed;

        /// <summary>
        /// Initializes the logger, creates the directory <c>agentflow-logs/{runId}</c> under <paramref name="workspaceRoot"/>,
        /// configures a rolling-free file sink with a fixed output template, and writes an opening line with work item and run ids.
        /// </summary>
        /// <param name="workspaceRoot">Absolute path from <c>Workspace:RootPath</c>; must not be null or empty when called from the factory.</param>
        /// <param name="runId">Unique run identifier (folder name segment).</param>
        /// <param name="workItemId">Work item id for the first log line.</param>
        /// <exception cref="IOException">Thrown when the directory or file cannot be created.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the process lacks permission to write under <paramref name="workspaceRoot"/>.</exception>
        public SerilogWorkflowRunLog(string workspaceRoot, string runId, string workItemId)
        {
            var dir = Path.Combine(workspaceRoot, "agentflow-logs", runId);
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "log.txt");
            _log = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    _path,
                    shared: true,
                    buffered: false,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
            _log.Information("Run started | work item {WorkItemId} | run {RunId}", workItemId, runId);
        }

        /// <inheritdoc />
        public string LogFilePath => _path;

        /// <inheritdoc />
        public void Step(string phase, string? detail = null)
        {
            if (string.IsNullOrEmpty(detail))
                _log.Information("STEP {Phase}", phase);
            else
                _log.Information("STEP {Phase}: {Detail}", phase, detail);
        }

        /// <inheritdoc />
        public void Error(string message, Exception? exception = null)
        {
            if (exception != null)
                _log.Error(exception, message);
            else
                _log.Error(message);
        }

        /// <inheritdoc />
        public void Configuration(string message) => _log.Warning("CONFIG: {Text}", message);

        /// <summary>
        /// Flushes pending writes and disposes the underlying Serilog <see cref="Logger"/>.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            (_log as IDisposable)?.Dispose();
        }
    }
}
