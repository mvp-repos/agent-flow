using AgentFlow.Core.Abstractions;

namespace AgentFlow.Cli.Fakes
{
    /// <summary>
    /// A fake implementation of <see cref="IBmadChecker"/> for testing or dry-run scenarios.
    /// Always reports that BMAD is installed so the workflow proceeds without requiring BMAD on the system.
    /// </summary>
    public sealed class FakeBmadChecker : IBmadChecker
    {
        /// <inheritdoc />
        public Task<bool> IsBmadInstalledAsync(string workspacePath, CancellationToken ct)
        {
            return Task.FromResult(true);
        }
    }
}
