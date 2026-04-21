namespace AgentFlow.Core.Abstractions;

/// <summary>
/// Runs automated tests in the cloned repository (e.g. <c>dotnet test</c>) after implementation and before broader check scripts.
/// When no test project or solution is found, implementations should return success with a clear skip message in the log.
/// </summary>
public interface ILocalTestRunner
{
    /// <summary>
    /// Executes tests at <paramref name="repositoryRoot"/> when a testable project or solution is discovered or configured.
    /// </summary>
    /// <param name="repositoryRoot">Absolute path to the repository root (working copy).</param>
    /// <param name="ct">Cancellation token to cancel the operation while tests are running.</param>
    /// <returns>
    /// A tuple with <c>Success</c> and <c>Log</c>: <c>Success</c> is <see langword="true"/> when tests pass or when no test target exists (skipped); <see langword="false"/> when validation fails, the host cannot run tests, or tests fail. <c>Log</c> contains combined output or error details.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
    Task<(bool Success, string Log)> RunAsync(string repositoryRoot, CancellationToken ct);
}
