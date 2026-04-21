using AgentFlow.Core.Abstractions;
using AgentFlow.Core.Models;
using AgentFlow.Shared.Configuration;
using AgentFlow.Shared.Helpers;
using Microsoft.Extensions.Options;

namespace AgentFlow.Adapters.Repo;

/// <summary>
/// Resolves repo URL and local path using the configured default repo by implementing <see cref="IRepoResolver"/>.
/// Enables running the tool for different repos by changing <see cref="AzureDevOpsConfig.DefaultRepoUrl"/> in config.
/// </summary>
public sealed class DefaultRepoResolver : IRepoResolver
{
    // Services
    private readonly WorkspaceConfig   _workspace;
    private readonly AzureDevOpsConfig _ado;

    public DefaultRepoResolver(IOptions<AgentFlowOptions> options)
    {
        var opt    = options.Value;
        _workspace = opt.Workspace;
        _ado       = opt.AzureDevOps;
    }

    /// <inheritdoc />
    public Task<RepoResolutionResult> ResolveAsync(WorkItem workItem, CancellationToken ct)
    {
        if (workItem is null)
            return Task.FromResult(Unresolved("Work item is null."));

        try
        {
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Unresolved("Repo resolution was cancelled."));
        }

        try
        {
            if (string.IsNullOrWhiteSpace(_ado.DefaultRepoUrl))
                return Task.FromResult(Unresolved("Default repo not configured. Set AgentFlow:AzureDevOps:DefaultRepoUrl in appsettings.json."));

            if (string.IsNullOrWhiteSpace(_workspace.RootPath))
                return Task.FromResult(Unresolved("Workspace root not configured. Set AgentFlow:Workspace:RootPath in appsettings.json."));

            var repoName = !string.IsNullOrWhiteSpace(_ado.DefaultRepoName)
                ? _ado.DefaultRepoName.Trim()
                : RepoFx.DeriveRepoNameFromUrl(_ado.DefaultRepoUrl);

            if (string.IsNullOrWhiteSpace(repoName))
                return Task.FromResult(Unresolved("Could not derive repo name from DefaultRepoUrl. Set AgentFlow:AzureDevOps:DefaultRepoName explicitly."));

            var projectPart = string.IsNullOrWhiteSpace(_ado.Project) ? "" : _ado.Project.Trim();
            var localPath = string.IsNullOrEmpty(projectPart)
                ? Path.Combine(_workspace.RootPath.Trim(), repoName)
                : Path.Combine(_workspace.RootPath.Trim(), projectPart, repoName);

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(localPath);
            }
            catch (Exception ex)
            {
                return Task.FromResult(Unresolved($"Invalid repo path: {ex.Message}"));
            }

            if (string.IsNullOrWhiteSpace(workItem.Id))
                return Task.FromResult(Unresolved("Work item has no Id; cannot build branch name."));

            var branchName = BuildBranchName(workItem);
            var baseBranch = GetBaseBranchForWorkItem(workItem);

            return Task.FromResult(new RepoResolutionResult(
                Resolved     : true,
                RepoUrl      : _ado.DefaultRepoUrl.Trim(),
                LocalPath    : fullPath,
                BranchName   : branchName,
                FailureReason: null,
                BaseBranch   : baseBranch
            ));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Unresolved("Repo resolution was cancelled."));
        }
        catch (Exception ex)
        {
            var reason = ex.Message ?? ex.GetType().Name;
            if (ex.InnerException is not null)
                reason += $" ({ex.InnerException.Message})";
            return Task.FromResult(Unresolved($"Repo resolution failed: {reason}"));
        }
    }

    /// <summary>
    /// Gets the base branch to create from, based on work item type and config. 
    /// Feature → BranchBaseForFeature (e.g. release), Bug → BranchBaseForBug (e.g. main), others → BranchBaseForOther or <see langword="null"/>.
    /// </summary>
    /// <returns>
    /// The base branch name (e.g. "main", "release") to create the new branch from otherwise, <see langword="null"/> to use the repo default branch.
    /// </returns>
    private string? GetBaseBranchForWorkItem(WorkItem workItem)
    {
        var type = GetWorkItemType(workItem);
        return type switch
        {
            "Feature"              => string.IsNullOrWhiteSpace(_ado.BranchBaseForFeature) ? null : _ado.BranchBaseForFeature.Trim(),
            "Bug"                  => string.IsNullOrWhiteSpace(_ado.BranchBaseForBug)     ? null : _ado.BranchBaseForBug.Trim(),
            "Product Backlog Item" => string.IsNullOrWhiteSpace(_ado.BranchBaseForFeature) ? null : _ado.BranchBaseForFeature.Trim(),
            _                      => string.IsNullOrWhiteSpace(_ado.BranchBaseForOther)   ? null : _ado.BranchBaseForOther.Trim()
        };
    }

    /// <summary>
    /// Builds the branch name from the work item type and title. Feature → feature/&lt;id&gt;-short-description; Bug → bug/&lt;id&gt;-short-description; others → users/agentflow/&lt;id&gt;.
    /// </summary>
    /// <param name="workItem">The work item (Id, Title, Fields with System.WorkItemType).</param>
    /// <returns>
    /// A branch name safe for Git (e.g. feature/12345-add-login-button).
    /// </returns>
    private static string BuildBranchName(WorkItem workItem)
    {
        var type = GetWorkItemType(workItem);
        var slug = TextFx.SlugifyForBranch(workItem.Title);

        return type switch
        {
            "Feature"              => $"feature/{workItem.Id}-{slug}",
            "Bug"                  => $"bug/{workItem.Id}-{slug}",
            "Product Backlog Item" => $"feature/{workItem.Id}-{slug}",
            _                      => $"users/agentflow/{workItem.Id}"
        };
    }

    /// <summary>
    /// Gets the work item type from Fields (e.g. System.WorkItemType in Azure DevOps). Case-insensitive.
    /// </summary>
    /// <param name="workItem">The work item with Fields dictionary.</param>
    /// <returns>
    /// The trimmed work item type (e.g. "Feature", "Bug"); otherwise, <see langword="null"/> if not found/empty.
    /// </returns>
    private static string? GetWorkItemType(WorkItem workItem)
    {
        if (workItem.Fields is null) 
            return null;
        if (!workItem.Fields.TryGetValue("System.WorkItemType", out var type)) 
            return null;
        return string.IsNullOrWhiteSpace(type) ? null : type.Trim();
    }

    /// <summary>
    /// Produces a short, Git-safe slug from the title (lowercase, hyphens, no invalid chars). Max length 40.
    /// </summary>
    /// <param name="title">The original title (e.g. "Add login button to homepage").</param>
    /// <returns>
    /// A slugified version of the title suitable for branch names (e.g. "add-login-button-to-homepage"). If the title 
    /// is <see langword="null"/>/empty or results in an empty slug, returns "work".
    /// </returns>

    /// <summary>
    /// Helper to create an unresolved RepoResolutionResult with a specific failure reason.
    /// </summary>
    /// <param name="failureReason">The reason why resolution failed (e.g. config issue, invalid path, cancellation).</param>
    /// <returns>
    /// A <see cref="RepoResolutionResult"/> with <see cref="RepoResolutionResult.Resolved"/> <see langword="false"/>, <see langword="null"/> RepoUrl/LocalPath/BranchName, and the provided FailureReason.
    /// </returns>
    private static RepoResolutionResult Unresolved(string failureReason)
    {
        return new RepoResolutionResult(
            Resolved     : false,
            RepoUrl      : null,
            LocalPath    : null,
            BranchName   : null,
            FailureReason: failureReason
        );
    }
}
