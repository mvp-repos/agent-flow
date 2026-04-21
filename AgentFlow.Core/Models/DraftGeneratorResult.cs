namespace AgentFlow.Core.Models;

/// <summary>
/// Result of running PRD/draft generation (e.g. Cursor CLI + BMAD quick-spec).
/// Completion is determined by process exit success; no separate callback from the LLM is required.
/// When the agent has questions for the dev it exits with code 2 and we set QuestionsForDev so we can post them as a comment.
/// The LLM/BMAD should output AGENTFLOW_PRD_PATH=&lt;path&gt; so we get the generated PRD path for the code generation step.
/// </summary>
/// <param name="Success"><see langword="true"/> if the PRD generation process completed successfully (exit 0).</param>
/// <param name="Message">Human-readable message or log excerpt (e.g. error details on failure, or agent output).</param>
/// <param name="QuestionsForDev">When set, the agent asked questions and exited with code 2; post this as a work item comment so the dev can answer and re-run with agent:regenerate-prd.</param>
/// <param name="PrdPath">When set, the path to the generated PRD (from LLM output AGENTFLOW_PRD_PATH=). Absolute or relative to repo root; code gen uses this path.</param>
public sealed record DraftGeneratorResult(bool Success, string Message, string? QuestionsForDev = null, string? PrdPath = null);
