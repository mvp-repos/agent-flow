namespace AgentFlow.Shared.Helpers;

/// <summary>
/// Shared helpers for comparing version-like strings (e.g. release branch suffixes).
/// </summary>
public static class VersionFx
{
    /// <summary>
    /// Parses a version suffix (e.g. "1.3.0" or "2") into an array of integers for comparison.
    /// Non-numeric segments are treated as 0.
    /// </summary>
    /// <param name="suffix">The version string, typically the part after a branch prefix (e.g. "1.3.0").</param>
    /// <returns>
    /// An array of integers; at least one element.
    /// </returns>
    public static int[] ParseVersionSuffix(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
            return new[] { 0 };

        var parts = suffix.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return new[] { 0 };

        var nums = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            nums[i] = int.TryParse(parts[i], out var n) ? n : 0;
        return nums;
    }

    /// <summary>
    /// Compares two version arrays segment by segment.
    /// </summary>
    /// <param name="a">First version array.</param>
    /// <param name="b">Second version array.</param>
    /// <returns>
    /// Negative if a &lt; b, zero if a == b, positive if a &gt; b.
    /// </returns>
    public static int CompareVersion(int[] a, int[] b)
    {
        var len = Math.Max(a?.Length ?? 0, b?.Length ?? 0);
        for (int i = 0; i < len; i++)
        {
            var av = (a != null && i < a.Length) ? a[i] : 0;
            var bv = (b != null && i < b.Length) ? b[i] : 0;
            if (av != bv)
                return av.CompareTo(bv);
        }
        return 0;
    }
}

