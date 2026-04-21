using System;

namespace AgentFlow.Core.Workflow
{
    /// <summary>
    /// Options for configuring workflow execution behavior, such as dry run mode and approval polling settings.
    /// </summary>
    /// <param name="DryRun">The DryRun flag indicates whether the workflow should be executed in dry run mode, where actions are simulated but not actually performed.</param>
    /// <param name="ApprovalPollInterval">This specifies the time interval between each poll for approval status when the workflow is waiting for approval. It determines how frequently the system checks for updates on the approval status.</param>
    /// <param name="ApprovalMaxPolls">This defines the maximum number of times the system will poll for approval status before giving up. If the approval status is not received within this number of polls, the workflow may be terminated or marked as failed.</param>
    public sealed record WorkflowOptions(
        bool DryRun,
        TimeSpan ApprovalPollInterval,
        int ApprovalMaxPolls
    );
}
