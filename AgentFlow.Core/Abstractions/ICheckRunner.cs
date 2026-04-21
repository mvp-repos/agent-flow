using System;

namespace AgentFlow.Core.Abstractions
{
    /// <summary>
    /// Defines the contract for a check runner, which is responsible for executing checks on a given 
    /// local path. The ICheckRunner interface abstracts the underlying implementation details of how checks 
    /// are performed, allowing agents to run various types of checks (e.g. code quality, security scans, unit tests) in a 
    /// consistent manner regardless of the specific tools or frameworks used.
    /// </summary>
    public interface ICheckRunner
    {
        /// <summary>
        /// Runs the checks on the specified local path. The checks can include various types of validations, such as code 
        /// quality analysis, security scans, or unit tests, depending on the implementation of the ICheckRunner. The method 
        /// returns a tuple indicating whether the checks were successful and a log containing details about the execution of 
        /// the checks, which can be used for debugging or reporting purposes.
        /// </summary>
        /// <param name="localPath">The local file system path where the Git repository should be located.</param>
        /// <param name="ct">Cancellation token to cancel the check process.</param>
        /// <returns>
        /// Returns a tuple containing a boolean value indicating the success of the checks and a
        /// string log that provides details about the execution of the checks. The success value will be <see langword="true"/>
        /// if all checks passed successfully, and <see langword="false"/> if any check failed. The log will contain information
        /// about which checks were run, their results, and any relevant messages or errors that occurred during the
        /// execution of the checks.
        /// </returns>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
        Task<(bool Success, string Log)> RunAsync(string localPath, CancellationToken ct);
    }
}
