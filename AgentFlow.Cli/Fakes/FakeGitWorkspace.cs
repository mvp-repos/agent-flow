using AgentFlow.Core.Abstractions;
using System;

namespace AgentFlow.Cli.Fakes
{
    /// <summary>
    /// A fake implementation of the <see cref="IGitWorkspace"/> interface for testing purposes. This class simulates 
    /// Git operations such as ensuring a repository, creating branches, getting diffs, committing changes, and pushing to a remote. 
    /// Instead of performing real Git operations, it logs the actions to the console, allowing us to verify that the workflow 
    /// is invoking Git commands correctly without needing an actual Git environment set up.
    /// </summary>
    public sealed class FakeGitWorkspace : IGitWorkspace
    {
        /// <inheritdoc />
        public Task EnsureRepoAsync(string repoIdOrUrl, string localPath, CancellationToken ct)
        {
            Console.WriteLine($"[FAKE GIT] EnsureRepo {repoIdOrUrl} at {localPath}");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task CreateBranchAsync(string localPath, string branchName, string? baseBranch, CancellationToken ct)
        {
            Console.WriteLine($"[FAKE GIT] CreateBranch {branchName} from {(string.IsNullOrEmpty(baseBranch) ? "HEAD" : baseBranch)} in {localPath}");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string> GetDiffSummaryAsync(string localPath, CancellationToken ct)
        {
            return Task.FromResult("[FAKE DIFF] 2 files changed, 10 insertions(+), 3 deletions(-)");
        }

        /// <inheritdoc />
        public Task CommitAsync(string localPath, string message, CancellationToken ct)
        {
            Console.WriteLine($"[FAKE GIT] Commit in {localPath}: {message}");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PushAsync(string localPath, CancellationToken ct)
        {
            Console.WriteLine($"[FAKE GIT] Push {localPath}");
            return Task.CompletedTask;
        }
    }
}
