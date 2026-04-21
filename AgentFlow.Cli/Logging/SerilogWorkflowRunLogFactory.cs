using AgentFlow.Core.Abstractions;

namespace AgentFlow.Cli.Logging
{
    /// <summary>
    /// Default <see cref="IWorkflowRunLogFactory"/> for the CLI: returns <see cref="SerilogWorkflowRunLog"/> when the workspace
    /// root is valid, otherwise <see cref="NullWorkflowRunLog.Instance"/>. If Serilog setup throws
    /// (e.g. path denied), the factory falls back to the null logger so the run can still report failure via console and exit code.
    /// </summary>
    public sealed class SerilogWorkflowRunLogFactory : IWorkflowRunLogFactory
    {
        /// <summary>
        /// Returns a file-backed <see cref="SerilogWorkflowRunLog"/> when <paramref name="workspaceRoot"/> is usable;
        /// otherwise returns <see cref="NullWorkflowRunLog.Instance"/>.
        /// </summary>
        /// <param name="workspaceRoot">Configured workspace root; when null or whitespace, no file log is created.</param>
        /// <param name="runId">Unique run identifier used in the log folder path.</param>
        /// <param name="workItemId">Work item id written to the opening log line.</param>
        /// <returns>
        /// A <see cref="SerilogWorkflowRunLog"/> instance, or <see cref="NullWorkflowRunLog.Instance"/> when the root is empty
        /// or when constructing the Serilog logger fails (e.g. access denied).
        /// </returns>
        /// <remarks>
        /// Exceptions from <see cref="SerilogWorkflowRunLog"/> construction are caught so a logging failure does not mask the workflow outcome; that run has no file log.
        /// </remarks>
        public IWorkflowRunLog Create(string? workspaceRoot, string runId, string workItemId)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                return NullWorkflowRunLog.Instance;
            try
            {
                return new SerilogWorkflowRunLog(workspaceRoot.Trim(), runId, workItemId);
            }
            catch
            {
                return NullWorkflowRunLog.Instance;
            }
        }
    }
}
