using System.Text;

namespace AgentFlow.Shared.Configuration
{
    public static class ConfigurationValidator
    {
        /// <summary>
        /// Validates the provided <see cref="AgentFlowOptions"/> configurations.
        /// </summary>
        /// <param name="cfg">The <see cref="AgentFlowOptions"/> instance to validate.</param>
        /// <exception cref="InvalidOperationException">Thrown when any required configuration is missing or invalid.</exception>
        public static void ValidateOrThrow(AgentFlowOptions cfg)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(cfg.AzureDevOps.OrganizationUrl))
                errors.Add("Missing AgentFlow:AzureDevOps:OrganizationUrl in appsettings.json");

            if (string.IsNullOrWhiteSpace(cfg.AzureDevOps.Project))
                errors.Add("Missing AgentFlow:AzureDevOps:Project in appsettings.json");

            if (string.IsNullOrWhiteSpace(cfg.AzureDevOps.PersonalAccessToken))
                errors.Add("Missing AgentFlow:AzureDevOps:PersonalAccessToken in appsettings.json");

            if (string.IsNullOrWhiteSpace(cfg.Workspace.RootPath))
                errors.Add("Missing AgentFlow:Workspace:RootPath in appsettings.json");

            if (string.IsNullOrWhiteSpace(cfg.AzureDevOps.DefaultRepoUrl))
                errors.Add("Missing AgentFlow:AzureDevOps:DefaultRepoUrl in appsettings.json (clone URL for the repo to use)");

            if (string.IsNullOrWhiteSpace(cfg.AzureDevOps.WorkItemTag))
                errors.Add("Missing AgentFlow:AzureDevOps:WorkItemTag in appsettings.json (e.g. agent:task); required to filter which work items the engine runs for.");

            if (cfg.AzureDevOps.WorkItemTag != null && !string.IsNullOrWhiteSpace(cfg.AzureDevOps.WorkItemTag) && cfg.AzureDevOps.WorkItemTag.Contains(';'))
                errors.Add("AgentFlow:AzureDevOps:WorkItemTag must be a single tag (no semicolons); use one value such as agent:task.");

            if (string.IsNullOrWhiteSpace(cfg.AzureDevOps.AssignedToUser))
                errors.Add("Missing AgentFlow:AzureDevOps:AssignedToUser in appsettings.json (e.g. your ADO display name); required so the engine runs only for that user's tickets.");

            // Notifications (optional): validate SMTP settings only when enabled.
            if (cfg.Notifications != null && string.Equals(cfg.Notifications.Mode?.Trim(), "smtp", StringComparison.OrdinalIgnoreCase))
            {
                var smtp = cfg.Notifications.Smtp;
                if (smtp == null)
                {
                    errors.Add("Missing AgentFlow:Notifications:Smtp settings in appsettings.json (required when Notifications.Mode is 'smtp').");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(smtp.Host))
                        errors.Add("Missing AgentFlow:Notifications:Smtp:Host in appsettings.json (required when Notifications.Mode is 'smtp').");
                    if (smtp.Port <= 0)
                        errors.Add("AgentFlow:Notifications:Smtp:Port must be > 0 (required when Notifications.Mode is 'smtp').");
                    if (string.IsNullOrWhiteSpace(smtp.Username))
                        errors.Add("Missing AgentFlow:Notifications:Smtp:Username in appsettings.json (required when Notifications.Mode is 'smtp').");
                    if (string.IsNullOrWhiteSpace(smtp.Password))
                        errors.Add("Missing AgentFlow:Notifications:Smtp:Password in appsettings.json (required when Notifications.Mode is 'smtp').");
                    if (string.IsNullOrWhiteSpace(smtp.From))
                        errors.Add("Missing AgentFlow:Notifications:Smtp:From in appsettings.json (required when Notifications.Mode is 'smtp').");
                    if (smtp.To == null || smtp.To.Length == 0 || smtp.To.All(string.IsNullOrWhiteSpace))
                        errors.Add("Missing AgentFlow:Notifications:Smtp:To in appsettings.json (required when Notifications.Mode is 'smtp').");
                }
            }

            if (errors.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("AgentFlow configuration error(s):");
                foreach (var error in errors)
                {
                    sb.AppendLine($"- {error}");
                }

                sb.AppendLine();
                sb.AppendLine($"Expected config location: {Path.Combine(AppContext.BaseDirectory, "appsettings.json")}");
                sb.AppendLine("Copy appsettings.template.json to appsettings.json and set the correct values.");

                throw new InvalidOperationException(sb.ToString());
            }
        }

        /// <summary>
        /// Validates the minimum configuration needed for <c>--provider fake</c> runs (repo resolution and workspace path).
        /// Does not require a PAT or full Azure DevOps identity settings.
        /// </summary>
        /// <param name="cfg">The agent flow options.</param>
        /// <exception cref="InvalidOperationException">Thrown when workspace root or default repo URL is missing.</exception>
        public static void ValidateFakeRunOrThrow(AgentFlowOptions cfg)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(cfg.Workspace.RootPath))
                errors.Add("Missing AgentFlow:Workspace:RootPath in appsettings.json (required for fake runs to resolve a local repo path).");
            if (string.IsNullOrWhiteSpace(cfg.AzureDevOps.DefaultRepoUrl))
                errors.Add("Missing AgentFlow:AzureDevOps:DefaultRepoUrl in appsettings.json (required for fake runs; use any valid URL if you only use --dry-run).");

            if (errors.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("AgentFlow fake provider configuration error(s):");
                foreach (var error in errors)
                    sb.AppendLine($"- {error}");
                sb.AppendLine();
                sb.AppendLine($"Config: {Path.Combine(AppContext.BaseDirectory, "appsettings.json")}");
                throw new InvalidOperationException(sb.ToString());
            }
        }

        /// <summary>
        /// Ensures that the workspace directory specified in the configuration exists. If it does not exist, it will be created.
        /// </summary>
        /// <param name="cfg">The <see cref="AgentFlowOptions"/> instance.</param>
        public static void EnsureWorkspaceExists(AgentFlowOptions cfg)
        {
            Directory.CreateDirectory(cfg.Workspace.RootPath);
        }
    }
}
