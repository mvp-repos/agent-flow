namespace AgentFlow.Core.Models;

/// <summary>
/// Result of a single workflow step (tag/assignee, clarity, repo resolution, or workspace/branch preparation).
/// Used by the workflow engine to decide whether to continue or return early.
/// </summary>
/// <param name="Run">The current or updated run state after the step.</param>
/// <param name="ShouldContinue"><see langword="true"/> to proceed to the next step; <see langword="false"/> to return this run to the caller (skipped, paused, or failed).</param>
/// <param name="Resolution">Resolved repo details; present only when the step is repo resolution and resolution succeeded. Otherwise <see langword="null"/>.</param>
public sealed record WorkflowStepResult(
    AgentRun Run,
    bool ShouldContinue,
    RepoResolutionResult? Resolution = null
);
