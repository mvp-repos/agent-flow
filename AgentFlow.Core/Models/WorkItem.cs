using System.Collections.Generic;

namespace AgentFlow.Core.Models
{
    /// <summary>
    /// Represents a work item from a provider system (e.g. Azure DevOps, Jira) that an agent will process. This record captures 
    /// the essential details of the work item, including its unique identifier, title, description, any additional fields, and
    /// optional attachments. The Fields property allows for extensibility, enabling the inclusion of provider-specific 
    /// information that may be necessary for the agent to effectively handle the work item. This design ensures that 
    /// the WorkItem record can accommodate a wide range of work item types and structures across different provider
    /// systems while maintaining a consistent interface for the agent's processing logic.
    /// </summary>
    /// <param name="Id">The unique identifier of the work item, which can be used to correlate the work item with specific tasks or issues in the provider system. This ID is essential for tracking and referencing the work item throughout its lifecycle.</param>
    /// <param name="Title">The title of the work item, providing a brief summary or description of the task or issue that the agent will process. The title is often used for display purposes and can help users quickly understand the nature of the work item.</param>
    /// <param name="Description">The detailed description of the work item, which may include additional information, context, or instructions related to the task or issue. The description can be used by the agent to gain a deeper understanding of the work item and to guide its processing logic.</param>
    /// <param name="Fields">The dictionary of additional fields associated with the work item, where the key is the field name and the value is the field value. This allows for extensibility and flexibility in representing various types of work items from different provider systems, as it can accommodate provider-specific information that may be necessary for processing the work item effectively.</param>
    /// <param name="Attachments">Optional list of attachments (e.g. files) linked to the work item. Empty if none or not supported by provider.</param>
    /// <param name="Comments">Optional discussion on the work item (e.g. agent questions and dev answers). Included when re-running PRD so the agent sees the answers.</param>
    public sealed record WorkItem(
        string Id,
        string Title,
        string? Description,
        IReadOnlyDictionary<string, string?> Fields,
        IReadOnlyList<WorkItemAttachment>? Attachments = null,
        IReadOnlyList<WorkItemComment>? Comments = null
    );
}
