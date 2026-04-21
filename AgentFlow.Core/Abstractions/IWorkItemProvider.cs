using System;
using AgentFlow.Core.Models;

namespace AgentFlow.Core.Abstractions
{
    /// <summary>
    /// Defines the contract for a work item provider, which is responsible for retrieving and managing work items from a specific provider system (e.g. Azure DevOps, Jira). Abstracts how work items are accessed and manipulated so agents can interact consistently regardless of provider. Implementations may throw for invalid arguments, provider/API errors, or network failures; callers should handle these exceptions appropriately.
    /// </summary>
    public interface IWorkItemProvider
    {
        /// <summary>
        /// Gets the work item by its ID. The work item contains all the necessary
        /// information for the agent to process it, such as title, description, and relevant fields.
        /// </summary>
        /// <param name="workItemId">The unique identifier of the work item to retrieve.</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        /// <returns>The work item.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="workItemId"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider fails to retrieve the work item (e.g. not found, API or network error).</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
        Task<WorkItem> GetWorkItemAsync(string workItemId, CancellationToken ct);

        /// <summary>
        /// Gets the discussion (comments) on the work item, in chronological order. Used when re-running PRD generation
        /// so the agent can see the dev's answers to its questions.
        /// </summary>
        /// <param name="workItemId">The unique identifier of the work item.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>List of comments (author, date, text); empty if none or not supported.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="workItemId"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
        Task<IReadOnlyList<WorkItemComment>> GetCommentsAsync(string workItemId, CancellationToken ct);

        /// <summary>
        /// Adds a comment to the specified work item. This can be used by the agent to communicate
        /// with users, ask for clarification, or provide updates on the processing of the work item. The comment is provided in
        /// markdown format, allowing for rich text formatting and better readability.
        /// </summary>
        /// <param name="workItemId">The unique identifier of the work item.</param>
        /// <param name="commentMarkdown">The content of the comment to add, formatted in markdown for rich text representation.</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="workItemId"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider fails to add the comment (e.g. work item not found, API or network error).</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
        Task AddCommentAsync(string workItemId, string commentMarkdown, CancellationToken ct);

        /// <summary>
        /// Updates the state of the specified work item to a new state. This can be used by the agent to
        /// move the work item through different stages of processing, such as from "Intake" to "In Progress" or "Waiting for Info".
        /// The new state is typically defined by the provider system and may require specific values depending on the workflow
        /// and lifecycle of the work item in that system.
        /// </summary>
        /// <param name="workItemId">The unique identifier of the work item.</param>
        /// <param name="newState">The new state to set for the work item, which should be a valid state defined by the provider system's workflow.</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="workItemId"/> or <paramref name="newState"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider fails to update the state (e.g. work item not found, invalid state, API or network error).</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
        Task UpdateStateAsync(string workItemId, string newState, CancellationToken ct);

        /// <summary>
        /// Removes a tag from the work item so it is not seen on the next poll. Used after handling RegeneratePrd
        /// so the same tag can be added again to trigger another run (avoids repeated triggers from the same tag).
        /// </summary>
        /// <param name="workItemId">The work item ID.</param>
        /// <param name="tagToRemove">The tag to remove (e.g. agent:regenerate-prd).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="workItemId"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider fails to remove the tag (e.g. work item not found, API or network error).</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
        Task RemoveTagAsync(string workItemId, string tagToRemove, CancellationToken ct);

        /// <summary>
        /// Creates a pull request in the provider system (e.g. Azure DevOps) for the given repository and branch refs.
        /// Implementations should return a web URL that can be opened by a human reviewer.
        /// </summary>
        /// <param name="repoName">Repository name (provider-specific identifier; for ADO this is the repo name).</param>
        /// <param name="sourceBranch">Source branch name (e.g. feature/123-add-thing or refs/heads/feature/123-add-thing).</param>
        /// <param name="targetBranch">Target/base branch name (e.g. main or release/1.2.3 or refs/heads/main). May contain a simple wildcard like release/* for ADO.</param>
        /// <param name="title">Pull request title.</param>
        /// <param name="description">Pull request description/body.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Web URL of the created pull request.</returns>
        /// <exception cref="ArgumentException">Thrown when required parameters are missing.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider fails to create the pull request.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
        Task<string> CreatePullRequestAsync(string repoName, string sourceBranch, string targetBranch, string title, string description, CancellationToken ct);

        /// <summary>
        /// Evaluates work item tags and returns the current automation signal. Returns <see cref="ApprovalOutcome.Approved"/> when the configured continue tag is present,
        /// <see cref="ApprovalOutcome.Stopped"/> when the stop tag is present (checked first), or <see cref="ApprovalOutcome.Pending"/> otherwise.
        /// Use for both PRD gate and final gate: pass <paramref name="continueApprovalTag"/> to select which tag counts as continue
        /// (e.g. PrdApprovalTag for the PRD loop); when <see langword="null"/>, the provider uses the configured ApprovalTag (final gate).
        /// Pass <paramref name="regeneratePrdTag"/> when this poll should also detect a PRD re-run tag (e.g. <c>agent:regenerate-prd</c>); when <see langword="null"/>, <see cref="ApprovalOutcome.RegeneratePrd"/> is never returned.
        /// When <paramref name="regenerateCodeTag"/> is provided, the provider can also detect a code re-run request (e.g. <c>agent:regenerate-code</c>).
        /// When <paramref name="regenerateCodeExclusive"/> is <see langword="true"/> (used by the code-gen questions loop), only stop and regenerate-code are evaluated,
        /// so the workflow does not accidentally continue/approve while waiting for answers.
        /// </summary>
        /// <param name="workItemId">The unique identifier of the work item.</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        /// <param name="continueApprovalTag">Optional. When provided (e.g. PrdApprovalTag), this tag is used instead of the default ApprovalTag for the Approved signal. Ignored when <paramref name="regenerateCodeTag"/> is set.</param>
        /// <param name="regeneratePrdTag">Optional. When provided (non-whitespace), the work item is checked for this tag to return <see cref="ApprovalOutcome.RegeneratePrd"/> (evaluated after stop and code-gen-only mode, before Approved). Pass <see langword="null"/> when PRD re-run is not applicable (e.g. final approval poll).</param>
        /// <param name="regenerateCodeTag">Optional. When provided, this tag is checked to return <see cref="ApprovalOutcome.RegenerateCode"/>.</param>
        /// <param name="regenerateCodeExclusive">Optional. When <see langword="true"/>, only stop and <paramref name="regenerateCodeTag"/> are evaluated; continue/PRD-regenerate tags are ignored.</param>
        /// <returns>
        /// <see cref="ApprovalOutcome.Approved"/> to continue, <see cref="ApprovalOutcome.Stopped"/> to stop, <see cref="ApprovalOutcome.RegeneratePrd"/> or <see cref="ApprovalOutcome.RegenerateCode"/> to re-run, or <see cref="ApprovalOutcome.Pending"/> to keep polling.
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="workItemId"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider fails to read tags (e.g. work item not found, API or network error).</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="ct"/>.</exception>
        Task<ApprovalOutcome> GetWorkItemSignalAsync(
            string workItemId,
            CancellationToken ct,
            string? continueApprovalTag = null,
            string? regeneratePrdTag = null,
            string? regenerateCodeTag = null,
            bool regenerateCodeExclusive = false);
    }
}
