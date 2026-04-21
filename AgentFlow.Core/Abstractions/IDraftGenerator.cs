using AgentFlow.Core.Models;

namespace AgentFlow.Core.Abstractions;

/// <summary>
/// Generates the PRD (and optionally draft content) by analyzing the work item and the cloned repository.
/// Implementations typically invoke Cursor CLI + BMAD quick-spec, passing full ADO work item data to the LLM.
/// Completion is determined by process exit success (and optional artifact check); the LLM does not send a separate callback.
/// </summary>
public interface IDraftGenerator
{
    /// <summary>
    /// Runs PRD generation for the given work item in the specified repository path.
    /// The implementation should pass all ADO work item data (title, description, fields, attachments) to the process/LLM.
    /// </summary>
    /// <param name="workItem">The work item containing ADO data to pass to the LLM.</param>
    /// <param name="repoPath">Absolute path to the cloned repository (working directory for the generation process).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success and message; success means the process exited successfully (and optionally expected PRD artifact exists).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
    Task<DraftGeneratorResult> GeneratePrdAsync(WorkItem workItem, string repoPath, CancellationToken ct);
}
