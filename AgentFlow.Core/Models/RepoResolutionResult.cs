namespace AgentFlow.Core.Models;

/// <summary>
/// Result of resolving which repository and local path to use for a work item.
/// Used by the workflow after the clarity gate to determine where to clone and which branch to create.
/// </summary>
/// <param name="Resolved"><see langword="true"/> if a repo URL and local path were determined; <see langword="false"/> if the workflow should pause and ask for confirmation.</param>
/// <param name="RepoUrl">Git clone URL (or repo identifier). <see langword="null"/> if not resolved.</param>
/// <param name="LocalPath">Absolute local path for the repo. <see langword="null"/> if not resolved.</param>
/// <param name="BranchName">Branch name to create (e.g. feature/123-add-login). <see langword="null"/> if not resolved.</param>
/// <param name="FailureReason">Short reason when not resolved (e.g. "Default repo not configured"). <see langword="null"/> when resolved.</param>
/// <param name="BaseBranch">Branch to create from (e.g. release for features, main for bugs). <see langword="null"/> means use current HEAD.</param>
public sealed record RepoResolutionResult(
    bool Resolved,
    string? RepoUrl,
    string? LocalPath,
    string? BranchName,
    string? FailureReason = null,
    string? BaseBranch = null
);
