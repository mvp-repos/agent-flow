using System;

namespace AgentFlow.Shared.Configuration
{
    /// <summary>
    /// Represents the application configuration for the AgentFlow CLI.
    /// </summary>
    public sealed class AppConfiguration
    {
        /// <summary>
        /// The name of the configuration section in the appsettings files that contains the AgentFlow settings.
        /// </summary>
        public static string SectionName  { get; set; } = "AgentFlow";

        /// <summary>
        /// Gets or sets the configuration options for the agent workflow.
        /// </summary>
        public AgentFlowOptions AgentFlow { get; set; } = new();
    }

    /// <summary>
    /// Represents the configuration options for the agent workflow.
    /// </summary>
    public sealed class AgentFlowOptions
    {
        public AzureDevOpsConfig AzureDevOps { get; set; } = new();
        public PollingConfig Polling         { get; set; } = new();
        public WorkspaceConfig Workspace     { get; set; } = new();
        public NotificationConfig Notifications { get; set; } = new();
    }

    /// <summary>
    /// Represents notification configuration (console vs SMTP email).
    /// </summary>
    public sealed class NotificationConfig
    {
        /// <summary>
        /// Notification mode. Supported: <c>console</c> (default), <c>smtp</c>.
        /// </summary>
        public string Mode                 { get; set; } = "console";

        /// <summary>
        /// SMTP settings used when <see cref="Mode"/> is <c>smtp</c>.
        /// </summary>
        public SmtpNotificationConfig Smtp { get; set; } = new();
    }

    /// <summary>
    /// Represents SMTP settings for email notifications.
    /// </summary>
    public sealed class SmtpNotificationConfig
    {
        /// <summary>
        /// SMTP host (e.g. smtp.gmail.com or smtp.office365.com).
        /// </summary>
        public string Host     { get; set; } = string.Empty;

        /// <summary>
        /// SMTP port (e.g. 587 for STARTTLS).
        /// </summary>
        public int Port        { get; set; } = 587;

        /// <summary>
        /// Enable SSL/TLS (recommended).
        /// </summary>
        public bool EnableSsl  { get; set; } = true;

        /// <summary>
        /// SMTP username (often the email address).
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// SMTP password (use an app password where required).
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// From email address used for notifications.
        /// </summary>
        public string From     { get; set; } = string.Empty;

        /// <summary>
        /// One or more recipient addresses.
        /// </summary>
        public string[] To     { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Represents the configuration options specific to Azure DevOps integration.
    /// </summary>
    public sealed class AzureDevOpsConfig
    {
        /// <summary>
        /// Gets or sets the URL of the Azure DevOps organization.
        /// </summary>
        public string OrganizationUrl     { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the Azure DevOps project to monitor for work items.
        /// </summary>
        public string Project             { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the personal access token (PAT) used for authenticating with the Azure DevOps API.
        /// </summary>
        public string PersonalAccessToken { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the tag used to indicate that a work item has been approved by 
        /// an agent and is ready to proceed in the workflow.
        /// </summary>
        public string ApprovalTag         { get; set; } = "agent:approved";

        /// <summary>
        /// Gets or sets the state name used to indicate that a work item is waiting for more 
        /// information from a human before it can proceed in the workflow.
        /// </summary>
        public string WaitingState        { get; set; } = "Waiting for info";

        /// <summary>
        /// Work item state to set after opening a pull request (e.g. "Code Review"). Used after commit/push + PR creation.
        /// </summary>
        public string CodeReviewState     { get; set; } = "Code Review";

        /// <summary>
        /// Default Git repository clone URL used when the work item does not specify a repo.
        /// Enables running the tool for different repos by changing config.
        /// </summary>
        public string DefaultRepoUrl       { get; set; } = string.Empty;

        /// <summary>
        /// Name used for the local folder under the workspace root (e.g. repo name).
        /// If empty, derived from the last segment of <see cref="DefaultRepoUrl"/>.
        /// </summary>
        public string DefaultRepoName      { get; set; } = string.Empty;

        /// <summary>
        /// Base branch to create feature branches from. Can be a fixed name (e.g. main) or a pattern for dynamic release branches (e.g. release/*).
        /// When a pattern like release/* is used, the latest matching remote branch by version is used (e.g. release/1.3.0). If none found, a comment is added to the work item.
        /// </summary>
        public string BranchBaseForFeature { get; set; } = "release/*";

        /// <summary>
        /// Base branch to create bug branches from (e.g. main).
        /// </summary>
        public string BranchBaseForBug     { get; set; } = "main";

        /// <summary>
        /// Base branch for other work item types (e.g. Task). Empty means use current HEAD after clone.
        /// </summary>
        public string BranchBaseForOther   { get; set; } = "main";

        /// <summary>
        /// Optional. If set, only work items that have this tag are processed (e.g. "agent:task").
        /// When each developer runs AgentFlow, only tickets with this tag are eligible. Leave empty to allow any work item.
        /// </summary>
        public string WorkItemTag         { get; set; } = string.Empty;

        /// <summary>
        /// Optional. If set, only work items assigned to this user are processed. Use the value that appears in ADO
        /// (e.g. display name or email from System.AssignedTo). Each developer sets their own identity here. Leave empty to allow any assignee.
        /// </summary>
        public string AssignedToUser      { get; set; } = string.Empty;

        /// <summary>
        /// When true, after PRD generation the workflow waits for the user to add the PRD approval tag (e.g. agent:prdapproved)
        /// before continuing to code generation and checks. When false, the workflow continues without a PRD review step.
        /// </summary>
        public bool RequirePrdApproval    { get; set; }

        /// <summary>
        /// Tag that indicates the user approved the PRD; workflow continues to the rest of the steps. Used when <see cref="RequirePrdApproval"/> is true.
        /// </summary>
        public string PrdApprovalTag      { get; set; } = "agent:prdapproved";

        /// <summary>
        /// Tag that indicates the user wants to stop the whole workflow. No commit or further steps. Checked in the PRD approval loop (and optionally elsewhere).
        /// </summary>
        public string StopTag             { get; set; } = "agent:stop";

        /// <summary>
        /// Tag that requests re-running PRD generation with latest ticket data (e.g. after user added more data to the work item). Checked only in the PRD approval loop.
        /// </summary>
        public string RegeneratePrdTag    { get; set; } = "agent:regenerate-prd";

        /// <summary>
        /// Tag that requests re-running code generation (e.g. after the agent asked questions and the dev replied in comments). Checked only in the code-generation questions loop.
        /// </summary>
        public string RegenerateCodeTag   { get; set; } = "agent:regenerate-code";
    }

    /// <summary>
    /// Represents the configuration options for polling intervals and limits when waiting for human approval in the workflow.
    /// </summary>
    public sealed class PollingConfig
    {
        /// <summary>
        /// Gets or sets the number of seconds to wait between each poll when checking for human approval of a work item.
        /// </summary>
        public int ApprovalPollSeconds { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of times to poll for human approval before giving up and 
        /// marking the workflow as failed or taking alternative action.
        /// </summary>
        public int ApprovalMaxPolls    { get; set; }
    }

    /// <summary>
    /// Represents the configuration options for the local workspace where code changes will be generated and 
    /// managed during the workflow execution.
    /// </summary>
    public sealed class WorkspaceConfig
    {
        /// <summary>
        /// Gets or sets the root path of the local workspace directory where the CLI will create subdirectories 
        /// for each workflow run, generate code changes, and manage git repositories. This should be a valid file 
        /// system path on the machine where the CLI is running.
        /// </summary>
        public string RootPath          { get; set; } = string.Empty;

        /// <summary>
        /// Full path to the Git executable (e.g. C:\Program Files\Git\bin\git.exe). If empty, "git" is used and must be on the system PATH.
        /// Set this when the process cannot find git (e.g. PATH not set in the run context).
        /// </summary>
        public string GitExecutablePath { get; set; } = string.Empty;

        /// <summary>
        /// Optional. Path to the single directory containing all Cursor rule files (Option 3): PRD prompt, coding patterns,
        /// user rules, etc. When set, passed as GUIDANCE_DIR; run-prd.cmd or run-prd.ps1 in this directory is executed for PRD generation.
        /// Can be absolute, or relative to the application directory (e.g. "cursor-rules" after agentflow init).
        /// </summary>
        public string CursorRulesDir { get; set; } = "cursor-rules";

        /// <summary>
        /// When true, after PRD approval the workflow runs the code generation step (run-dev.ps1) to implement from the PRD before running checks.
        /// When false, workflow goes from PRD approval directly to checks (PRD-only or manual implementation).
        /// </summary>
        public bool EnableCodeGenStep { get; set; } = true;

        /// <summary>
        /// Optional. Relative path to a test project, folder, or .csproj under the cloned repo (e.g. "tests/MyApp.Tests" or "src/MyApp.Tests/MyApp.Tests.csproj").
        /// Used to locate tests for code generation env vars and for the post-codegen <c>dotnet test</c> step. Empty = discover a *Test*.csproj or a root .sln.
        /// </summary>
        public string TestProjectPath { get; set; } = string.Empty;

        /// <summary>
        /// Optional. Conventional Commit scope to use for automated commits (e.g. "api", "ui", "core").\n
        /// If empty, AgentFlow derives a scope from the configured repo name.
        /// </summary>
        public string CommitScope { get; set; } = string.Empty;
    }
}
