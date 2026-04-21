namespace AgentFlow.Core.Git;

/// <summary>
/// Defines and validates the allowlist of git commands that the LLM (code generation step) is permitted to run.
/// Only commands that do not affect the remote or rewrite history are allowed; commit, push, pull, merge, rebase, etc. are disallowed.
/// </summary>
public static class GitAllowlist
{
    /// <summary>
    /// Allowed git subcommands (first token after "git"). No commit, push, pull, merge, rebase, reset --hard, or destructive branch operations.
    /// </summary>
    private static readonly HashSet<string> AllowedSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "status",
        "diff",
        "add",
        "restore",
        "checkout",
        "branch",
        "log",
        "show",
        "rev-parse"
    };

    /// <summary>
    /// Subcommands that are never allowed (even if someone adds them elsewhere). Takes precedence over allowlist.
    /// </summary>
    private static readonly HashSet<string> DisallowedSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "commit", "push", "pull" , "merge", "rebase", "reset", "revert",
        "stash" , "tag" , "fetch", "remote", "clone", "submodule",
        "clean" , "rm"  , "mv"
    };

    /// <summary>
    /// For "branch", only these arguments (after -a/-r etc.) are allowed when the intent is destructive. We allow listing and creating: branch, branch &lt;name&gt;.
    /// Disallow: branch -d, branch -D, branch -m (rename/delete).
    /// </summary>
    private static readonly HashSet<string> DisallowedBranchFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "-d", "-D", "-m", "-M", "--delete", "--move"
    };

    /// <summary>
    /// For "checkout", only path restore is allowed: "checkout -- &lt;path&gt;" or "checkout &lt;path&gt;". Branch switch (checkout &lt;branch&gt;) and branch create (-b) are disallowed so the agent does not leave the workflow-created branch.
    /// </summary>
    /// <param name="args">Arguments after the <c>checkout</c> subcommand.</param>
    /// <returns>
    /// <see langword="true"/> when the invocation looks like path-only checkout (e.g. <c>--</c> then paths, or path-like tokens); <see langword="false"/> when it would switch branches or otherwise be unsafe.
    /// </returns>
    private static bool IsCheckoutAllowed(string[] args)
    {
        if (args.Length == 0)
            return true; // git checkout with no args is invalid but harmless
        if (args[0].Equals("--", StringComparison.Ordinal))
            return true; // checkout -- <paths>
        foreach (var a in args)
        {
            if (a.StartsWith("-", StringComparison.Ordinal))
                continue; // skip flags
            if (a.Contains("/") || a.Contains("\\"))
                return true; // path-like argument
        }
        return false; // e.g. checkout main or checkout -b feature/x
    }

    /// <summary>
    /// Validates a git command line. The line should start with "git " (or be just "git") and the rest is the subcommand and arguments.
    /// </summary>
    /// <param name="commandLine">Full command line (e.g. "git status", "git diff --stat").</param>
    /// <returns>
    /// <see langword="true"/> if the command is allowed; <see langword="false"/> if it is disallowed or malformed.
    /// </returns>
    public static bool IsAllowed(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return false;

        var trimmed = commandLine.Trim();
        if (!trimmed.StartsWith("git ", StringComparison.OrdinalIgnoreCase) && !trimmed.Equals("git", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return trimmed.Equals("git", StringComparison.OrdinalIgnoreCase); // "git" alone is not useful but harmless
        var subcommand = parts[1];

        if (DisallowedSubcommands.Contains(subcommand))
            return false;
        if (!AllowedSubcommands.Contains(subcommand))
            return false;

        var args = parts.Skip(2).ToArray();

        if (subcommand.Equals("branch", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var a in args)
            {
                if (DisallowedBranchFlags.Contains(a))
                    return false;
            }
        }

        if (subcommand.Equals("checkout", StringComparison.OrdinalIgnoreCase))
            return IsCheckoutAllowed(args);

        if (subcommand.Equals("reset", StringComparison.OrdinalIgnoreCase))
            return false; // already in DisallowedSubcommands

        return true;
    }

    /// <summary>
    /// Returns a human-readable list of allowed git commands for inclusion in LLM prompts.
    /// </summary>
    public static string GetAllowedCommandsDescription()
    {
        return "Allowed git commands (you may only run these): git status, git diff, git diff --stat, git add, git restore, git checkout -- <path>(s), git branch, git branch <name>, git log, git show, git rev-parse. You must NOT run: git commit, git push, git pull, git merge, git rebase, git reset, git revert, git stash, git tag, git fetch, git remote, git clone, git branch -d/-D, or any other command not in the allowed list.";
    }

    /// <summary>
    /// Returns the allowlist as a single line for environment variable or prompt (short form).
    /// </summary>
    public static string GetAllowedListForEnv()
    {
        return "status,diff,add,restore,checkout,branch,log,show,rev-parse";
    }
}
