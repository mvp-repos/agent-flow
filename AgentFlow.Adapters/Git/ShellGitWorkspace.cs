using System.Diagnostics;
using AgentFlow.Core.Abstractions;
using AgentFlow.Shared.Configuration;
using AgentFlow.Shared.Helpers;
using Microsoft.Extensions.Options;

namespace AgentFlow.Adapters.Git;

/// <summary>
/// Git workspace implementation that shells out to the system <c>git</c> CLI.
/// Clones repos, creates branches, and runs diff/commit/push via process execution.
/// All operations validate inputs, handle cancellation, and wrap git failures in clear exceptions.
/// The Git executable path can be set via <see cref="WorkspaceConfig.GitExecutablePath"/> when the process cannot find git on PATH.
/// </summary>
public sealed class ShellGitWorkspace : IGitWorkspace
{
    private const string DefaultGitExe = "git";
    private readonly string _gitExe;

    public ShellGitWorkspace(IOptions<WorkspaceConfig> options)
    {
        var path = options?.Value?.GitExecutablePath?.Trim();
        _gitExe  = string.IsNullOrEmpty(path) ? DefaultGitExe : path;
    }

    /// <inheritdoc />
    public async Task EnsureRepoAsync(string repoIdOrUrl, string localPath, CancellationToken ct)
    {
        ValidateLocalPath(localPath, nameof(localPath));

        if (string.IsNullOrWhiteSpace(repoIdOrUrl))
            throw new ArgumentException("Repository URL or ID is required.", nameof(repoIdOrUrl));

        try
        {
            // If the local path exists and contains a .git folder, we assume the repo is already cloned and do nothing
            var gitDir = Path.Combine(localPath, ".git");
            if (Directory.Exists(localPath) && Directory.Exists(gitDir))
                return;

            // Ensure the parent directory exists before cloning. Git requires the target folder to not exist, but
            // the parent must exist. We create the parent if needed, and throw if we fail (e.g. permission issue)
            var parentDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                try
                {
                    Directory.CreateDirectory(parentDir);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Could not create directory for clone: {parentDir}. {ex.Message}", ex);
                }
            }

            // Clone the repo using 'git clone <repoIdOrUrl> <folderName>' with the parent directory as the
            // working directory. Capture output and handle errors
            var (exitCode, stdout, stderr) = await RunGitAsync(workingDirectory: parentDir ?? ".", ct,
                "clone", repoIdOrUrl, Path.GetFileName(localPath)).ConfigureAwait(false);

            if (exitCode != 0)
                throw new InvalidOperationException($"git clone failed (exit {exitCode}) at {localPath}. stderr: {stderr}. stdout: {stdout}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"EnsureRepoAsync failed for {localPath}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task CreateBranchAsync(string localPath, string branchName, string? baseBranch, CancellationToken ct)
    {
        ValidateLocalPath(localPath, nameof(localPath));

        if (string.IsNullOrWhiteSpace(branchName))
            throw new ArgumentException("Branch name is required.", nameof(branchName));

        EnsureRepoExists(localPath);

        try
        {
            if (!string.IsNullOrWhiteSpace(baseBranch))
            {
                // Fetch the latest refs from origin to ensure we have up-to-date information on remote branches
                var fetch = await RunGitAsync(localPath, ct, "fetch", "origin").ConfigureAwait(false);
                if (fetch.ExitCode != 0)
                    throw new InvalidOperationException($"git fetch origin failed (exit {fetch.ExitCode}) in {localPath}. stderr: {fetch.Stderr}");

                // If the base branch contains a wildcard (e.g. release/*), resolve it to the latest
                // matching remote branch by version. If no match is found, throw so the workflow can comment
                // on the work item and avoid creating a branch from an unintended base
                var resolvedBase = baseBranch!.Trim();
                if (resolvedBase.Contains('*'))
                {
                    // Resolve the pattern to the latest matching branch (e.g. release/1.3.0). If no match, throw with details
                    var matched = await ResolveLatestMatchingBranchAsync(localPath, resolvedBase, ct).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(matched))
                        throw new InvalidOperationException($"Base branch pattern '{resolvedBase}' matched no remote branches. Please ensure at least one branch exists (e.g. release/1.3.0) or update AgentFlow:AzureDevOps:BranchBaseForFeature in appsettings.json.");
                    resolvedBase = matched;
                }

                // Check out the base branch so that the new branch will be created from it. If checkout
                // fails (e.g. branch not found), throw with details
                var checkoutBase = await RunGitAsync(localPath, ct, "checkout", resolvedBase).ConfigureAwait(false);
                if (checkoutBase.ExitCode != 0)
                {
                    var msg = checkoutBase.Stderr + checkoutBase.Stdout;
                    if (msg.IndexOf("did not match any file(s) known to git", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("unknown revision", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("could not find", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new InvalidOperationException($"Base branch '{resolvedBase}' not found. Please create the branch in the repository or update AgentFlow:AzureDevOps:BranchBaseForFeature / BranchBaseForBug in appsettings.json. stderr: {checkoutBase.Stderr}");
                    throw new InvalidOperationException($"git checkout {resolvedBase} failed (exit {checkoutBase.ExitCode}) in {localPath}. stderr: {checkoutBase.Stderr}. stdout: {checkoutBase.Stdout}");
                }
            }

            // If the branch already exists (e.g. from a previous run or retry), check it out instead of
            // creating it to avoid "branch already exists" errors
            var (refExit, _, _) = await RunGitAsync(localPath, ct, "rev-parse", "--verify", $"refs/heads/{branchName}").ConfigureAwait(false);
            var branchExists = refExit == 0;

            if (branchExists)
            {
                var (coExit, coOut, coErr) = await RunGitAsync(localPath, ct, "checkout", branchName).ConfigureAwait(false);
                if (coExit != 0)
                    throw new InvalidOperationException($"git checkout failed for existing branch '{branchName}' (exit {coExit}) in {localPath}. stderr: {coErr}. stdout: {coOut}");
            }
            else
            {
                var (exitCode, stdout, stderr) = await RunGitAsync(localPath, ct, "checkout", "-b", branchName).ConfigureAwait(false);
                if (exitCode != 0)
                    throw new InvalidOperationException($"git checkout -b failed (exit {exitCode}) in {localPath}. stderr: {stderr}. stdout: {stdout}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"CreateBranchAsync failed for branch '{branchName}' at {localPath}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetDiffSummaryAsync(string localPath, CancellationToken ct)
    {
        ValidateLocalPath(localPath, nameof(localPath));
        EnsureRepoExists(localPath);

        try
        {
            // Get the diff summary using 'git diff --stat' to show changed files and line
            // counts. If it fails, return the error message as the summary so the workflow can comment on the work item
            var (exitCode, stdout, stderr) = await RunGitAsync(localPath, ct, "diff", "--stat").ConfigureAwait(false);
            if (exitCode != 0)
                return $"(git diff --stat failed: {stderr})";

            // If there are no changes, git diff returns empty stdout. In that case, we also
            // check 'git status -s' to see if there are untracked files or other changes not included
            // in the diff (e.g. new files). We include those in the summary if present; otherwise we return "(no changes)"
            var statusExit = await RunGitAsync(localPath, ct, "status", "-s").ConfigureAwait(false);
            var status     = statusExit.ExitCode == 0 ? statusExit.Stdout : string.Empty;

            if (string.IsNullOrWhiteSpace(stdout) && !string.IsNullOrWhiteSpace(status))
                return $"Modified/untracked:\n{status.Trim()}";

            return string.IsNullOrWhiteSpace(stdout) ? "(no changes)" : stdout.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"(GetDiffSummaryAsync failed: {ex.Message})";
        }
    }

    /// <inheritdoc />
    public async Task CommitAsync(string localPath, string message, CancellationToken ct)
    {
        ValidateLocalPath(localPath, nameof(localPath));

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Commit message is required.", nameof(message));

        EnsureRepoExists(localPath);

        try
        {
            // Stage all changes (including new files) with 'git add -A'. If it fails, throw
            // with details so the workflow can comment on the work item
            var (addExit, _, addErr) = await RunGitAsync(localPath, ct, "add", "-A").ConfigureAwait(false);
            if (addExit != 0)
                throw new InvalidOperationException($"git add -A failed (exit {addExit}) in {localPath}. stderr: {addErr}");

            // Commit with 'git commit -m <message>'. If it fails (e.g. no changes to commit), throw
            // with details so the workflow can comment on the work item
            var (commitExit, _, commitErr) = await RunGitAsync(localPath, ct, "commit", "-m", message).ConfigureAwait(false);
            if (commitExit != 0)
                throw new InvalidOperationException($"git commit failed (exit {commitExit}) in {localPath}. stderr: {commitErr}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"CommitAsync failed at {localPath}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task PushAsync(string localPath, CancellationToken ct)
    {
        ValidateLocalPath(localPath, nameof(localPath));
        EnsureRepoExists(localPath);

        try
        {
            // Determine the current branch with 'git rev-parse --abrev-ref HEAD'. If it fails, throw with
            // details since we need the branch name to push
            var (branchExit, branchOut, _) = await RunGitAsync(localPath, ct, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false);
            var currentBranch = branchExit == 0 ? branchOut?.Trim() : null;
            if (string.IsNullOrWhiteSpace(currentBranch))
                throw new InvalidOperationException($"Could not determine current branch in {localPath}. Is the repository initialized and on a branch?");

            // Push with 'git push -u origin <currentBranch>'. If it fails, throw with details so the workflow
            // can comment on the work item
            var (exitCode, _, stderr) = await RunGitAsync(localPath, ct, "push", "-u", "origin", currentBranch).ConfigureAwait(false);
            if (exitCode != 0)
                throw new InvalidOperationException($"git push failed (exit {exitCode}) in {localPath}. stderr: {stderr}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PushAsync failed at {localPath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validates that the local path is not <see langword="null"/> or whitespace. Used by all public methods that take a repo path.
    /// </summary>
    /// <param name="localPath">The path to validate.</param>
    /// <param name="paramName">The parameter name to use in the exception (e.g. from nameof(localPath)).</param>
    /// <exception cref="ArgumentException">When <paramref name="localPath"/> is <see langword="null"/> or whitespace.</exception>
    private static void ValidateLocalPath(string localPath, string paramName)
    {
        if (string.IsNullOrWhiteSpace(localPath))
            throw new ArgumentException("Local path is required.", paramName);
    }

    /// <summary>
    /// Ensures the path exists and contains a .git directory (i.e. is a Git repository). Call before branch, diff, commit, or push operations.
    /// </summary>
    /// <param name="localPath">The path that should be an existing Git repository.</param>
    /// <exception cref="InvalidOperationException">When the path does not exist or does not contain a .git folder.</exception>
    private static void EnsureRepoExists(string localPath)
    {
        var gitDir = Path.Combine(localPath, ".git");
        if (!Directory.Exists(localPath) || !Directory.Exists(gitDir))
            throw new InvalidOperationException($"Not a Git repository or path does not exist: {localPath}. Ensure EnsureRepoAsync has been called first.");
    }

    /// <summary>
    /// Resolves the latest remote branch matching a pattern (e.g. release/*) by version. Lists origin refs, filters by prefix (pattern with * replaced by empty), and picks the branch with the highest semantic version suffix.
    /// </summary>
    /// <param name="localPath">The local Git repository path.</param>
    /// <param name="pattern">Pattern like release/*; the part after the last / is treated as a version (e.g. 1.3.0) for ordering.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The branch name without origin/ (e.g. release/1.3.0); otherwise, <see langword="null"/> if no match.
    /// </returns>
    private async Task<string?> ResolveLatestMatchingBranchAsync(string localPath, string pattern, CancellationToken ct)
    {
        // List all remote branches (e.g. "  origin/main", "  origin/release/1.2.0", "  origin/release/1.3.0")
        var (exitCode, stdout, _) = await RunGitAsync(localPath, ct, "branch", "-r").ConfigureAwait(false);
        if (exitCode != 0) 
            return null;

        // Derive match prefix from pattern: "release/*" -> "release/". We match branches that start with this prefix
        var prefix = pattern.TrimEnd('*').TrimEnd('/');

        if (string.IsNullOrEmpty(prefix)) 
            prefix = pattern.Replace("*", "", StringComparison.Ordinal);

        if (!prefix.EndsWith("/", StringComparison.Ordinal)) 
            prefix += "/";

        // Holds candidates that match the prefix, along with their parsed version suffix for sorting (e.g. "release/1.3.0" -> [1,3,0])
        var candidates = new List<(string BranchName, int[] Version)>();

        foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // Normalize: remove "origin/" so we work with branch names like "release/1.3.0"
            var branch = line.Trim().TrimStart();

            if (branch.StartsWith("origin/", StringComparison.Ordinal))
                branch = branch["origin/".Length..];

            if (string.IsNullOrEmpty(branch) || branch == "HEAD") 
                continue;

            // Only consider branches that match the pattern prefix (e.g. release/)
            if (!branch.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) 
                continue;

            // Extract the version suffix (e.g. "1.3.0" from "release/1.3.0") and parse for ordering
            var suffix  = branch.Length > prefix.Length ? branch[prefix.Length..] : "";
            var version = VersionFx.ParseVersionSuffix(suffix);
            candidates.Add((branch, version));
        }

        if (candidates.Count == 0) 
            return null;

        // Sort by version descending so the latest (e.g. 1.3.0) is first; return that branch name
        candidates.Sort((a, b) => VersionFx.CompareVersion(b.Version, a.Version));
        return candidates[0].BranchName;
    }

    /// <summary>
    /// Runs the system git CLI with the given arguments and returns the exit code plus captured stdout and stderr.
    /// Handles process start failure (e.g. Git not installed or not on PATH), cancellation (kills the process and throws),
    /// and reads both output streams to avoid deadlock.
    /// </summary>
    /// <param name="workingDirectory">The working directory for the git process. Must exist unless it is ".".</param>
    /// <param name="ct">Cancellation token; when signalled, the process is killed and <see cref="OperationCanceledException"/> is thrown.</param>
    /// <param name="args">The git command and arguments (e.g. "clone", repoUrl, folderName).</param>
    /// <returns>
    /// A tuple of exit code, stdout text, and stderr text (all trimmed).
    /// </returns>
    /// <exception cref="ArgumentException">When <paramref name="workingDirectory"/> does not exist and is not ".". </exception>
    /// <exception cref="InvalidOperationException">When the git process cannot be started (e.g. Git not found on PATH) or stream read fails.</exception>
    /// <exception cref="OperationCanceledException">When the operation is cancelled via <paramref name="ct"/>.</exception>
    private async Task<(int ExitCode, string Stdout, string Stderr)> RunGitAsync(string workingDirectory, CancellationToken ct, params string[] args)
    {
        if (!Directory.Exists(workingDirectory) && workingDirectory != ".")
            throw new ArgumentException($"Working directory does not exist: {workingDirectory}", nameof(workingDirectory));

        var psi = new ProcessStartInfo
        {
            FileName               = _gitExe,
            WorkingDirectory       = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            ArgumentList           = { }
        };

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        Process? process = null;
        try
        {
            process = new Process { StartInfo = psi };
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start '{_gitExe}'. Ensure Git is installed and on the PATH, or set AgentFlow:Workspace:GitExecutablePath to the full path (e.g. C:\\Program Files\\Git\\bin\\git.exe).");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Could not start '{_gitExe}' (is Git installed? If not on PATH, set AgentFlow:Workspace:GitExecutablePath). {ex.Message}", ex);
        }

        using (process)
        {
            using var reg = ct.Register(() =>
            {
                try
                {
                    if (process.HasExited) return;
                    process.Kill(entireProcessTree: true);
                }
                catch { /* ignore */ }
            });

            string stdout;
            string stderr;
            try
            {
                stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
                stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error while running git: {ex.Message}", ex);
            }

            ct.ThrowIfCancellationRequested();
            return (process.ExitCode, stdout?.Trim() ?? string.Empty, stderr?.Trim() ?? string.Empty);
        }
    }
}
