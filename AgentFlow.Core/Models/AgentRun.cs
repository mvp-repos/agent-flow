using System;

namespace AgentFlow.Core.Models
{
    /// <summary>
    /// Represents a single run of an agent, capturing essential information about the run's identity, state, timing, and 
    /// any relevant messages. This record is designed to be immutable, ensuring that once an instance is created, its properties 
    /// cannot be modified. The AgentRun record serves as a fundamental data structure for tracking the lifecycle of an agent's execution 
    /// across various stages and states.
    /// </summary>
    /// <param name="RunId">The unique identifier for this run.</param>
    /// <param name="Provider">The source or system that is executing the run (e.g. "azuredevops").</param>
    /// <param name="WorkItemId">The identifier of the work item associated with this run, which can be used to correlate the run with specific tasks or issues in the provider system.</param>
    /// <param name="State">The current state of the run, represented by the RunState enum, which indicates the stage of the run in its lifecycle.</param>
    /// <param name="StartedAt">The timestamp indicating when the run was initiated, providing a reference point for tracking the duration and timing of the run.</param>
    /// <param name="UpdatedAt">The timestamp indicating the last time the run's state or information was updated, allowing for monitoring of the run's progress and any changes that occur during its execution.</param>
    /// <param name="Message">The most recent message or log entry associated with the run, which can provide insights into the run's progress, any issues encountered, or important events that have occurred during its execution.</param>
    /// <param name="PrdPath">When set, the path to the generated PRD from the PRD generation step (parsed from LLM output). Used by the code generation step; absolute or relative to repo root.</param>
    public sealed record AgentRun(
        string RunId,
        string Provider,      // e.g. "azuredevops"
        string WorkItemId,
        RunState State,
        DateTimeOffset StartedAt,
        DateTimeOffset? UpdatedAt = null,
        string? Message = null,
        string? PrdPath = null
    );
}
