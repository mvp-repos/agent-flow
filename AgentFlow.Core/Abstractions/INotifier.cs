using System;

namespace AgentFlow.Core.Abstractions
{
    /// <summary>
    /// Defines the contract for a notifier, which is responsible for sending notifications or updates about 
    /// the agent's processing of work items. The INotifier interface abstracts the underlying implementation details 
    /// of how notifications are sent, allowing agents to communicate with users or other systems in a consistent manner 
    /// regardless of the notification mechanism used (e.g. email, chat, provider comments). By implementing this 
    /// interface, different notification channels can be integrated into the agent framework without requiring changes 
    /// to the agent's processing logic.
    /// </summary>
    public interface INotifier
    {
        /// <summary>
        /// Sends a notification with the specified subject and body. This method can be used by the
        /// agent to provide updates on the processing of work items.
        /// </summary>
        /// <param name="subject">The subject or title of the notification.</param>
        /// <param name="body">The content of the notification.</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        Task NotifyAsync(string subject, string body, CancellationToken ct);
    }
}
