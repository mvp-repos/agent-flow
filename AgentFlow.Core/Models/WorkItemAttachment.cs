using System;

namespace AgentFlow.Core.Models
{
    /// <summary>
    /// Represents an attachment on a work item (e.g. file attached in Azure DevOps).
    /// Contains metadata only; content can be fetched separately if needed.
    /// </summary>
    /// <param name="Id">Provider-specific attachment identifier (e.g. GUID in ADO).</param>
    /// <param name="Name">Display/file name of the attachment.</param>
    /// <param name="Url">URL to the attachment resource (e.g. ADO API URL for download).</param>
    public sealed record WorkItemAttachment(string Id, string Name, string Url);
}
