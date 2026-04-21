namespace AgentFlow.Shared.Helpers;

/// <summary>
/// Shared helpers for file system path filtering.
/// </summary>
public static class PathFx
{
    /// <summary>
    /// Returns whether <paramref name="path"/> is not under a <c>bin</c> or <c>obj</c> build output folder.
    /// </summary>
    /// <param name="path">A file path.</param>
    /// <returns>
    /// <see langword="true"/> if the path does not contain a <c>\bin\</c> or <c>\obj\</c> segment (normalized to the platform separator); otherwise <see langword="false"/>.
    /// </returns>
    public static bool IsNotUnderBinOrObj(string path)
    {
        var s = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return s.IndexOf($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) < 0
               && s.IndexOf($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) < 0;
    }
}

