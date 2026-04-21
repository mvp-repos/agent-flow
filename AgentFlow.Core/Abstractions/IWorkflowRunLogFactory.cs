namespace AgentFlow.Core.Abstractions
{
    /// <summary>
    /// Creates a new <see cref="IWorkflowRunLog"/> for a single <c>agentflow run</c> invocation. Each run has its own
    /// run id and log file path; the factory hides construction so <see cref="AgentFlow.Core.Workflow.WorkflowEngine"/>
    /// stays independent of Serilog or other logging libraries.
    /// </summary>
    public interface IWorkflowRunLogFactory
    {
        /// <summary>
        /// Creates a run-scoped logger. When <paramref name="workspaceRoot"/> is missing or blank, returns
        /// <see cref="NullWorkflowRunLog.Instance"/> so the workflow can run without file logging.
        /// </summary>
        /// <param name="workspaceRoot">Configured <c>Workspace:RootPath</c>; used as the base directory for log files when not empty.</param>
        /// <param name="runId">Unique id for this run (e.g. GUID), used in the log folder name.</param>
        /// <param name="workItemId">Work item id for the opening log line.</param>
        /// <returns>
        /// A concrete <see cref="IWorkflowRunLog"/> (e.g. file-backed in the CLI), or <see cref="NullWorkflowRunLog.Instance"/>
        /// when <paramref name="workspaceRoot"/> is unusable.
        /// </returns>
        IWorkflowRunLog Create(string? workspaceRoot, string runId, string workItemId);
    }
}
