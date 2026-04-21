namespace AgentFlow.Shared.Helpers;

/// <summary>
/// Shared helpers for Git ref normalization used by Azure DevOps Git APIs.
/// </summary>
public static class GitFx
{
    /// <summary>
    /// Normalizes a branch identifier into a full <c>refs/heads/...</c> ref when possible.
    /// If the input already begins with <c>refs/</c> it is returned as-is.
    /// </summary>
    /// <param name="branch">Branch name or ref (e.g. <c>main</c>, <c>release/1.2.3</c>, or <c>refs/heads/main</c>).</param>
    /// <returns>
    /// A normalized ref string suitable for Azure DevOps Git APIs.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="branch"/> is <see langword="null"/> or empty.</exception>
    public static string NormalizeToHeadsRef(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
            throw new ArgumentException("Branch cannot be null or empty.", nameof(branch));

        if (branch.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase))
            return branch;
        if (branch.StartsWith("refs/", StringComparison.OrdinalIgnoreCase))
            return branch;
        return "refs/heads/" + branch.TrimStart('/');
    }
}

