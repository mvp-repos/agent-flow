namespace AgentFlow.Core.Abstractions
{
    /// <summary>
    /// Defines the contract for checking whether BMAD (quick-spec / quick-dev tooling) is installed
    /// in the given workspace. BMAD is installed <em>in the repository</em> (e.g. via
    /// <c>npx bmad-method install</c>, which creates <c>_bmad/</c> and method module), not as an external system tool.
    /// The workflow requires BMAD after cloning the repo and before creating a branch.
    /// </summary>
    public interface IBmadChecker
    {
        /// <summary>
        /// Determines whether BMAD is installed and available for use in the given workspace.
        /// </summary>
        /// <param name="workspacePath">The local path of the cloned repository (working directory for the check).</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        /// <returns><see langword="true"/> if BMAD is installed and available; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="workspacePath"/> is <see langword="null"/> or empty, or the path does not exist.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
        Task<bool> IsBmadInstalledAsync(string workspacePath, CancellationToken ct);
    }
}
