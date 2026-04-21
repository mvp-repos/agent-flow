namespace AgentFlow.Core.Models;

/// <summary>
/// Result of running code generation (e.g. Cursor CLI run-dev script implementing from the PRD).
/// Success means the process exited with 0. Exit 2 means the agent has questions for the dev (same pattern as PRD generation).
/// </summary>
/// <param name="Success"><see langword="true"/> if the code generation process completed successfully (exit 0).</param>
/// <param name="Message">Human-readable message or log excerpt (e.g. error details on failure, or agent output).</param>
/// <param name="QuestionsForDev">When set, the agent asked questions and exited with code 2; post as work item comment so the dev can answer and re-run.</param>
public sealed record CodeGeneratorResult(bool Success, string Message, string? QuestionsForDev = null);
