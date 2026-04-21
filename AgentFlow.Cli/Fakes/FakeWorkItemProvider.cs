using AgentFlow.Core.Abstractions;
using AgentFlow.Core.Models;
using AgentFlow.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace AgentFlow.Cli.Fakes
{
    /// <summary>
    /// A fake implementation of the <see cref="IWorkItemProvider"/> interface for testing and development purposes.
    /// Returns work item fields aligned with <see cref="AzureDevOpsConfig"/> so tag, assignee, and clarity gates can pass,
    /// and returns <see cref="ApprovalOutcome.Approved"/> from polling so local runs complete without hanging.
    /// </summary>
    public sealed class FakeWorkItemProvider : IWorkItemProvider
    {
        private readonly AzureDevOpsConfig _ado;

        public FakeWorkItemProvider(IOptions<AzureDevOpsConfig> ado)
        {
            _ado = ado.Value;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<WorkItemComment>> GetCommentsAsync(string workItemId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<WorkItemComment>>(Array.Empty<WorkItemComment>());
        }

        /// <inheritdoc />
        public Task AddCommentAsync(string workItemId, string commentMarkdown, CancellationToken ct)
        {
            Console.WriteLine($"[FAKE ADO] Comment on {workItemId}:\n{commentMarkdown}");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<WorkItem> GetWorkItemAsync(string workItemId, CancellationToken ct)
        {
            var tag      = string.IsNullOrWhiteSpace(_ado.WorkItemTag) ? "agent:task" : _ado.WorkItemTag.Trim();
            var assignee = string.IsNullOrWhiteSpace(_ado.AssignedToUser) ? "Fake Local User" : _ado.AssignedToUser.Trim();

            var fields = new Dictionary<string, string?>
            {
                ["System.Tags"]         = tag,
                ["System.AssignedTo"]   = assignee,
                ["System.WorkItemType"] = "Feature"
            };

            var wi = new WorkItem(
                Id          : workItemId,
                Title       : "Fake work item (AgentFlow local test)",
                Description : "Fake description for clarity gate. Acceptance: AgentFlow fake run completes.",
                Fields      : fields,
                Attachments : Array.Empty<WorkItemAttachment>());

            return Task.FromResult(wi);
        }

        /// <inheritdoc />
        public Task UpdateStateAsync(string workItemId, string newState, CancellationToken ct)
        {
            Console.WriteLine($"[FAKE ADO] Update state {workItemId} -> {newState}");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveTagAsync(string workItemId, string tagToRemove, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string> CreatePullRequestAsync(string repoName, string sourceBranch, string targetBranch, string title, string description, CancellationToken ct)
        {
            Console.WriteLine($"[FAKE ADO] Create PR in repo '{repoName}' from '{sourceBranch}' to '{targetBranch}': {title}");
            return Task.FromResult("https://example.local/fake-pr/1");
        }

        /// <inheritdoc />
        public Task<ApprovalOutcome> GetWorkItemSignalAsync(
            string workItemId,
            CancellationToken ct,
            string? continueApprovalTag = null,
            string? regeneratePrdTag = null,
            string? regenerateCodeTag = null,
            bool regenerateCodeExclusive = false)
        {
            _ = workItemId;
            _ = continueApprovalTag;
            _ = regeneratePrdTag;
            _ = regenerateCodeTag;
            _ = regenerateCodeExclusive;
            return Task.FromResult(ApprovalOutcome.Approved);
        }
    }
}
