using AgentFlow.Core.Models;

namespace AgentFlow.Core.Abstractions;

/// <summary>
/// Resolves the target repository URL, local path, and branch name for a work item.
/// Implementations may use config (e.g. default repo) or work item fields/links.
/// </summary>
public interface IRepoResolver
{
    /// <summary>
    /// Resolves the repo and local path for the given work item.
    /// </summary>
    /// <param name="workItem">The work item to resolve a repo for.</param>
    /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
    /// <returns>
    /// A <see cref="RepoResolutionResult"/> containing the resolution outcome.
    /// </returns>
    Task<RepoResolutionResult> ResolveAsync(WorkItem workItem, CancellationToken ct);
}
