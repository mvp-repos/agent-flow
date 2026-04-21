namespace AgentFlow.Core.Models
{
    /// <summary>
    /// A single comment or reply on a work item (e.g. ADO discussion). Used so the PRD agent can see dev answers when re-running after questions.
    /// </summary>
    /// <param name="Author">Display name of the comment author.</param>
    /// <param name="CreatedAt">When the comment was created (provider-specific format or ISO).</param>
    /// <param name="Text">Comment body (markdown).</param>
    public sealed record WorkItemComment(string Author, string CreatedAt, string Text);
}
