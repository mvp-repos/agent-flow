using AgentFlow.Core.Abstractions;

namespace AgentFlow.Adapters.Bmad
{
    /// <summary>
    /// Checks whether BMAD is installed <em>in the repository</em> by looking for the standard BMAD folder structure.
    /// BMAD is not an external tool; it is installed inside the repo via <c>npx bmad-method install</c>, which creates
    /// <c>_bmad/</c> (with <c>bmm/</c> method module) and <c>_bmad-output/</c>. See
    /// <see href="https://docs.bmad-method.org/how-to/installation/install-bmad/">BMAD installation</see>.
    /// </summary>
    public sealed class RepoBmadChecker : IBmadChecker
    {
        /// <summary>
        /// Default folder name created by <c>npx bmad-method install</c> at the repo root.
        /// </summary>
        private const string BmadFolderName = "_bmad";

        /// <summary>
        /// Method module subfolder; presence indicates a valid BMAD method setup.
        /// </summary>
        private const string BmadBmmFolderName = "bmm";

        /// <inheritdoc />
        public Task<bool> IsBmadInstalledAsync(string workspacePath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
                throw new ArgumentException("Workspace path is required.", nameof(workspacePath));
            if (!Directory.Exists(workspacePath))
                throw new ArgumentException($"Workspace path does not exist: {workspacePath}", nameof(workspacePath));

            ct.ThrowIfCancellationRequested();

            // Check for the presence of the BMAD folder and the bmm method module folder inside it.
            var bmadDir = Path.Combine(workspacePath, BmadFolderName);
            if (!Directory.Exists(bmadDir))
                return Task.FromResult(false);

            // The presence of the bmm folder inside _bmad is a strong indicator that BMAD is properly installed and initialized.
            var bmmDir    = Path.Combine(bmadDir, BmadBmmFolderName);
            var installed = Directory.Exists(bmmDir);

            return Task.FromResult(installed);
        }
    }
}
