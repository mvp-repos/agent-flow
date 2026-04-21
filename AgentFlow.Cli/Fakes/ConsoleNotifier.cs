using AgentFlow.Core.Abstractions;
using System;

namespace AgentFlow.Cli.Fakes
{
    /// <summary>
    /// A simple implementation of the <see cref="INotifier"/> interface that writes notifications to the console. This is intended 
    /// for testing and demonstration purposes, allowing us to see notification outputs directly in the console without 
    /// integrating with external notification services.
    /// </summary>
    public sealed class ConsoleNotifier : INotifier
    {
        /// <inheritdoc />
        public Task NotifyAsync(string subject, string body, CancellationToken ct)
        {
            Console.WriteLine($"=== NOTIFY ===\n{subject}\n{body}\n==============");
            return Task.CompletedTask;
        }
    }
}
