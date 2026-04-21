namespace AgentFlow.Shared.Helpers;

/// <summary>
/// Shared helpers for parsing repository identifiers from URLs.
/// </summary>
public static class RepoFx
{
    /// <summary>
    /// Derives a repository name from the last segment of the URL path, trimming ".git" if present.
    /// </summary>
    /// <param name="url">Repository URL (e.g. "https://dev.azure.com/org/project/_git/repo.git").</param>
    /// <returns>
    /// The derived repo name (e.g. "repo"); otherwise, "repo" if the URL is <see langword="null"/>/empty or parsing fails.
    /// </returns>
    public static string DeriveRepoNameFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "repo";

        try
        {
            var u         = url.Trim().TrimEnd('/');
            var lastSlash = u.LastIndexOf('/');
            var seg       = lastSlash >= 0 ? u.Substring(lastSlash + 1) : u;
            if (seg.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                seg = seg.Substring(0, seg.Length - 4);
            return string.IsNullOrWhiteSpace(seg) ? "repo" : seg;
        }
        catch
        {
            return "repo";
        }
    }
}

