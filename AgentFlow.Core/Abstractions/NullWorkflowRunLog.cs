namespace AgentFlow.Core.Abstractions
{
    /// <summary>
    /// No-op implementation of <see cref="IWorkflowRunLog"/> used when <see cref="IWorkflowRunLogFactory"/> cannot create a file log
    /// (e.g. empty workspace root) or when tests need a silent logger. All members do nothing; <see cref="LogFilePath"/> is always
    /// <see langword="null"/>.
    /// </summary>
    public sealed class NullWorkflowRunLog : IWorkflowRunLog
    {
        /// <summary>
        /// Singleton instance returned by factories when file logging is not available.
        /// </summary>
        /// <value>
        /// The shared no-op logger instance.
        /// </value>
        public static readonly NullWorkflowRunLog Instance = new();

        private NullWorkflowRunLog() { }

        /// <inheritdoc />
        public string? LogFilePath => null;

        /// <inheritdoc />
        public void Step(string phase, string? detail = null) { }

        /// <inheritdoc />
        public void Error(string message, Exception? exception = null) { }

        /// <inheritdoc />
        public void Configuration(string message) { }

        /// <inheritdoc />
        /// <remarks>
        /// The singleton holds no resources; <see cref="IDisposable.Dispose"/> is a no-op.
        /// </remarks>
        public void Dispose() { }
    }
}
