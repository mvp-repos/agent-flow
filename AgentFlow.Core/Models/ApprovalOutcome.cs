namespace AgentFlow.Core.Models;

/// <summary>
/// Result of checking work item approval tags. Used for both PRD approval and final approval loops.
/// When <see cref="IWorkItemProvider.GetWorkItemSignalAsync"/> is called with <c>continueApprovalTag</c> (e.g. PrdApprovalTag),
/// that tag is used instead of the default ApprovalTag for the Approved signal. Regenerate PRD is a separate optional parameter.
/// </summary>
public enum ApprovalOutcome
{
    /// <summary>No approval or stop tag present; keep polling.</summary>
    Pending,

    /// <summary>User added the approval tag; continue (e.g. agent:prdapproved or agent:approved).</summary>
    Approved,

    /// <summary>User added the stop tag (e.g. agent:stop); stop the workflow without commit.</summary>
    Stopped,

    /// <summary>User requested PRD re-generation (e.g. agent:regenerate-prd); re-fetch work item and run PRD step again with latest ADO data.</summary>
    RegeneratePrd,

    /// <summary>User requested code-generation re-run (e.g. agent:regenerate-code); re-fetch work item (with comments) and run code gen again. Used when the agent had questions and the dev replied.</summary>
    RegenerateCode
}
