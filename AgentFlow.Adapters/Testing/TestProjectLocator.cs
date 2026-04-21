using AgentFlow.Shared.Helpers;

namespace AgentFlow.Adapters.Testing;

/// <summary>
/// Resolves a .NET test project (.csproj) or solution (.sln) under a repository root for <c>dotnet test</c>.
/// </summary>
public static class TestProjectLocator
{
    /// <summary>
    /// Tries to find a test target: configured relative path first, then a *Test*.csproj, then the first .sln in the repo root.
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repository root.</param>
    /// <param name="configuredTestProjectPath">Optional path from config (relative to <paramref name="repoRoot"/>, or absolute).</param>
    /// <returns>
    /// Full path to a .csproj or .sln, or <see langword="null"/> if none found or <paramref name="repoRoot"/> is invalid.
    /// </returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller does not have permission to enumerate files under <paramref name="repoRoot"/>.</exception>
    /// <exception cref="IOException">Thrown when a disk or I/O error occurs while searching for project files.</exception>
    /// <exception cref="PathTooLongException">Thrown when a path exceeds the maximum length defined by the system.</exception>
    public static string? TryFindTestTarget(string repoRoot, string? configuredTestProjectPath)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
            return null;

        // Normalize repo root path to ensure consistent combination with configuredTestProjectPath
        var root = Path.GetFullPath(repoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (!string.IsNullOrWhiteSpace(configuredTestProjectPath))
        {
            var p        = configuredTestProjectPath.Trim();
            var combined = Path.IsPathRooted(p) ? Path.GetFullPath(p) : Path.GetFullPath(Path.Combine(root, p));

            if (File.Exists(combined) && combined.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return combined;

            if (Directory.Exists(combined))
            {
                var inDir = Directory.GetFiles(combined, "*.csproj", SearchOption.AllDirectories)
                    .Where(PathFx.IsNotUnderBinOrObj)
                    .OrderBy(f => f.Length)
                    .FirstOrDefault(f => Path.GetFileName(f).Contains("Test", StringComparison.OrdinalIgnoreCase));
                if (inDir != null)
                    return inDir;

                var anyProj = Directory.GetFiles(combined, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (anyProj != null)
                    return anyProj;
            }
        }

        // Prefer .csproj files with "Test" in the name, then fallback to any .sln in the
        // repo root. We ignore .csproj files under bin/obj since some repos put build outputs there which
        // can be mistaken for real projects
        var projects = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(PathFx.IsNotUnderBinOrObj)
            .ToList();
        var testProject = projects
            .Where(f => Path.GetFileName(f).Contains("Test", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Length)
            .FirstOrDefault();
        if (testProject != null)
            return testProject;

        return Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
    }
}
