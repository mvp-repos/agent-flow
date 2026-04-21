using System;

namespace AgentFlow.Core.Abstractions
{
    /// <summary>
    /// Defines the contract for a Git workspace: clone, branch, diff, commit, and push.
    /// Implementations manage a local Git repository for the agent workflow.
    /// </summary>
    public interface IGitWorkspace
    {
        /// <summary>
        /// Ensures a local Git repository exists at the specified path. If the directory does not contain
        /// a .git folder, clones from the given repository URL (creating the parent directory if needed).
        /// If the repo already exists at the path, no action is taken.
        /// </summary>
        /// <param name="repoIdOrUrl">The repository URL or ID to clone when the local repository does not exist.</param>
        /// <param name="localPath">The full local path where the Git repository should be located.</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        /// <exception cref="ArgumentException">When <paramref name="repoIdOrUrl"/> or <paramref name="localPath"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">When clone fails (e.g. invalid URL, network error, or Git not installed).</exception>
        /// <exception cref="OperationCanceledException">When the operation is cancelled via <paramref name="ct"/>.</exception>
        Task EnsureRepoAsync(string repoIdOrUrl, string localPath, CancellationToken ct);

        /// <summary>
        /// Ensures the repository is on the given branch: creates and checks out <paramref name="branchName"/> if it does not exist,
        /// otherwise checks out the existing branch (so retries do not fail with "branch already exists"). If <paramref name="baseBranch"/>
        /// is specified, fetches and checks out that branch first, then creates the new branch from it; if the base branch does not exist,
        /// implementations throw so the workflow can comment on the work item. If <paramref name="baseBranch"/> is <see langword="null"/>, creates from current HEAD.
        /// </summary>
        /// <param name="localPath">The local path of an existing Git repository.</param>
        /// <param name="branchName">The name of the branch to create (if missing) and switch to.</param>
        /// <param name="baseBranch">Optional. Branch to create from (e.g. release for features, main for bugs). <see langword="null"/> to use current HEAD.</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        /// <exception cref="ArgumentException">When <paramref name="localPath"/> or <paramref name="branchName"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">When the path is not a Git repository, checkout fails, or the base branch does not exist.</exception>
        /// <exception cref="OperationCanceledException">When the operation is cancelled via <paramref name="ct"/>.</exception>
        Task CreateBranchAsync(string localPath, string branchName, string? baseBranch, CancellationToken ct);

        /// <summary>
        /// Gets a summary of uncommitted changes (e.g. which files changed, add/delete counts). Used for
        /// human review before commit.
        /// </summary>
        /// <param name="localPath">The local path of the Git repository.</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        /// <returns>A string summarizing the current diff/status.</returns>
        /// <exception cref="ArgumentException">When <paramref name="localPath"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">When the path is not a Git repository or the diff command fails.</exception>
        /// <exception cref="OperationCanceledException">When the operation is cancelled via <paramref name="ct"/>.</exception>
        Task<string> GetDiffSummaryAsync(string localPath, CancellationToken ct);

        /// <summary>
        /// Stages all changes and creates a commit with the given message in the repository at the given path.
        /// </summary>
        /// <param name="localPath">The local path of the Git repository.</param>
        /// <param name="message">The commit message.</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        /// <exception cref="ArgumentException">When <paramref name="localPath"/> or <paramref name="message"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">When the path is not a Git repository, there is nothing to commit, or commit fails.</exception>
        /// <exception cref="OperationCanceledException">When the operation is cancelled via <paramref name="ct"/>.</exception>
        Task CommitAsync(string localPath, string message, CancellationToken ct);

        /// <summary>
        /// Pushes the current branch from the local repository to the remote (e.g. origin).
        /// </summary>
        /// <param name="localPath">The local path of the Git repository.</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        /// <exception cref="ArgumentException">When <paramref name="localPath"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">When the path is not a Git repository, the current branch cannot be determined, or push fails.</exception>
        /// <exception cref="OperationCanceledException">When the operation is cancelled via <paramref name="ct"/>.</exception>
        Task PushAsync(string localPath, CancellationToken ct);
    }
}
