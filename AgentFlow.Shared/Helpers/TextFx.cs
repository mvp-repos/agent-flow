using System.Text;

namespace AgentFlow.Shared.Helpers;

/// <summary>
/// Shared helpers for common string operations used across AgentFlow (truncation, slugification).
/// </summary>
public static class TextFx
{
    /// <summary>
    /// Truncates long text for work item comments so content stays within practical service limits.
    /// </summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxChars">Maximum characters to keep before truncation (default: 12000).</param>
    /// <returns>
    /// Truncated text with an indication if truncation occurred.
    /// </returns>
    public static string TruncateForWorkItemComment(string text, int maxChars = 12000)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (maxChars <= 0)
            return string.Empty;
        if (text.Length <= maxChars)
            return text;

        const string suffix = "\n\n...[truncated]...";
        var keep            = Math.Max(0, maxChars - suffix.Length);

        return text.Substring(0, keep) + suffix;
    }

    /// <summary>
    /// Truncates a string to a single line and a maximum length. Newlines are replaced with spaces.
    /// </summary>
    /// <param name="text">Input text.</param>
    /// <param name="maxLen">Maximum length to keep.</param>
    /// <returns>
    /// Truncated single-line string with ellipsis if truncation occurred.
    /// </returns>
    public static string TruncateSingleLine(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (maxLen <= 0)
            return string.Empty;

        var single = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (single.Length <= maxLen)
            return single;
        if (maxLen <= 1)
            return "…";
        return single.Substring(0, maxLen - 1).TrimEnd() + "…";
    }

    /// <summary>
    /// Produces a short, Git-safe slug from a title (lowercase, hyphens, no invalid chars). Max length 40.
    /// </summary>
    /// <param name="title">The original title (e.g. "Add login button to homepage").</param>
    /// <returns>
    /// A slugified version suitable for branch names (e.g. "add-login-button-to-homepage"). If <paramref name="title"/> is <see langword="null"/>/empty
    /// or results in an empty slug, returns "work".
    /// </returns>
    public static string SlugifyForBranch(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "work";

        var s  = title.Trim();
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length && sb.Length < 40; i++)
        {
            var c = s[i];
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
            else if (c == ' ' || c == '-' || c == '_')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != '-')
                    sb.Append('-');
            }
        }

        var result = sb.ToString().TrimEnd('-');
        return string.IsNullOrEmpty(result) ? "work" : result;
    }

    /// <summary>
    /// Slugifies a commit scope to meet Conventional Commits style (<c>type(scope):</c>) and keep it readable.
    /// </summary>
    /// <param name="scope">Proposed scope string (e.g. "ADO Adapter").</param>
    /// <returns>
    /// A lowercased, dash-separated scope (e.g. "ado-adapter").
    /// </returns>
    public static string SlugifyForCommitScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return "core";

        var s  = scope.Trim();
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
            else if (c == ' ' || c == '-' || c == '_' || c == '/')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != '-')
                    sb.Append('-');
            }
        }

        var result = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "core" : result;
    }
}

