using AgentFlow.Core.Abstractions;
using System;

namespace AgentFlow.Cli.Fakes
{
    /// <summary>
    /// A fake implementation of the <see cref="ICheckRunner"/> interface for testing purposes. This class simulates
    /// the execution of checks (e.g. CI tests, linters) on a codebase. Instead of running real checks, it returns 
    /// a successful result with a predefined log message.
    /// </summary>
    public sealed class FakeCheckRunner : ICheckRunner
    {
        /// <inheritdoc />
        public Task<(bool Success, string Log)> RunAsync(string localPath, CancellationToken ct)
        {
            return Task.FromResult((true, "[FAKE CHECKS] success"));
        }
    }
}
