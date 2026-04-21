using AgentFlow.Core.Models;

namespace AgentFlow.Core.Abstractions;

/// <summary>
/// Generates implementation code (and tests) from an approved PRD by analyzing the PRD document and the repository.
/// Implementations typically invoke Cursor CLI via run-dev.ps1 with PRD path, repo path, and git allowlist. No commit or push is performed by the agent.
/// </summary>
public interface ICodeGenerator
{
    /// <summary>
    /// Runs implementation code generation for <paramref name="workItem"/> in the repository at <paramref name="repoPath"/>,
    /// using the approved PRD file at <paramref name="prdPath"/>. Implementations typically launch an external process (e.g. Cursor CLI via run-dev)
    /// with work item context, PRD path, guidance directory, and a git command allowlist; they do not commit or push.
    /// </summary>
    /// <param name="workItem">
    /// Work item whose id, title, description, fields, attachments, and comments are supplied to the generator as context (format is implementation-defined).
    /// </param>
    /// <param name="repoPath">Absolute path to the cloned repository root; used as the working directory for the generation process.</param>
    /// <param name="prdPath">Absolute path to the PRD document to implement (e.g. under <c>_bmad-output</c> or repo docs).</param>
    /// <param name="ct">Cancellation token; cancellation is observed while the generation process runs.</param>
    /// <returns>
    /// A <see cref="CodeGeneratorResult"/>: <see cref="CodeGeneratorResult.Success"/> is <see langword="true"/> when generation completes successfully
    /// (typically process exit code 0). Exit code <c>2</c> usually means the agent has questions—<see cref="CodeGeneratorResult.QuestionsForDev"/> contains the text to post on the work item, and <see cref="CodeGeneratorResult.Success"/> is <see langword="false"/>.
    /// Any other failure (non-zero exit, misconfiguration, or exception handled by the implementation) yields <see cref="CodeGeneratorResult.Success"/> <see langword="false"/> with details in <see cref="CodeGeneratorResult.Message"/>.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
    Task<CodeGeneratorResult> GenerateAsync(WorkItem workItem, string repoPath, string prdPath, CancellationToken ct);
}
