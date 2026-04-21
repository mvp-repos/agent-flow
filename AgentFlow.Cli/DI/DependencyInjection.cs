using AgentFlow.Cli.Commands;
using AgentFlow.Cli.Logging;
using AgentFlow.Shared.Configuration;
using AgentFlow.Cli.Fakes;
using AgentFlow.Core.Abstractions;
using AgentFlow.Core.Workflow;
using AgentFlow.Adapters.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using AgentFlow.Adapters.AzureDevOps;
using AgentFlow.Adapters.Bmad;
using AgentFlow.Adapters.CodeGen;
using AgentFlow.Adapters.Draft;
using AgentFlow.Adapters.Git;
using AgentFlow.Adapters.Repo;
using AgentFlow.Adapters.Checks;
using AgentFlow.Adapters.Testing;

namespace AgentFlow.Cli.DI
{
    /// <summary>
    /// Registers AgentFlow CLI services, including separate Azure DevOps and fake work item provider singletons
    /// (see <see cref="Commands.RunCommandHandler"/> for per-run <see cref="WorkflowEngine"/> construction).
    /// </summary>
    public static class DependencyInjection
    {
        /// <summary>
        /// Configures JSON configuration, binds <c>AgentFlow</c> options, and registers adapters and command handlers.
        /// </summary>
        /// <param name="host">The host builder to extend.</param>
        /// <param name="args">Command-line arguments (registered as a singleton for handlers such as <see cref="Commands.RunCommandHandler"/>).</param>
        /// <returns>The same <paramref name="host"/> for chaining.</returns>
        /// <remarks>
        /// Registers <see cref="IWorkflowRunLogFactory"/> as <see cref="SerilogWorkflowRunLogFactory"/> for file-backed step logs under the configured workspace root.
        /// <see cref="WorkflowEngine"/> is not registered here; <see cref="Commands.RunCommandHandler"/> constructs it per run with a provider-specific <see cref="IWorkItemProvider"/>.
        /// </remarks>
        public static IHostBuilder ConfigureAgentFlowCli(this IHostBuilder host, string[] args)
        {
            return host
            .ConfigureAppConfiguration((context, config) =>
            {
                var basePath = AppContext.BaseDirectory;
                config.SetBasePath(basePath)
                      .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

                // env vars + command line are already included by CreateDefaultBuilder(args)
                // but you can still add custom sources here if needed.
            })
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();

                services.AddHttpClient();

                // args as injectable
                services.AddSingleton(args);

                // Options (bind from config)
                var agentFlowSection = context.Configuration.GetSection(AppConfiguration.SectionName);
                services.Configure<AppConfiguration>(agentFlowSection);
                services.Configure<AgentFlowOptions>(agentFlowSection);
                services.Configure<AzureDevOpsConfig>(agentFlowSection.GetSection("AzureDevOps"));
                services.Configure<PollingConfig>(agentFlowSection.GetSection("Polling"));
                services.Configure<WorkspaceConfig>(agentFlowSection.GetSection("Workspace"));
                services.Configure<NotificationConfig>(agentFlowSection.GetSection("Notifications"));

                // Work items: concrete types registered; RunCommandHandler picks fake vs ADO per --provider and builds WorkflowEngine
                services.AddSingleton<AzureDevOpsWorkItemProvider>();
                services.AddSingleton<FakeWorkItemProvider>();
                services.AddSingleton<INotifier>(sp =>
                {
                    var cfg  = sp.GetRequiredService<IOptions<NotificationConfig>>().Value;
                    var mode = cfg.Mode?.Trim() ?? "console";
                    return string.Equals(mode, "smtp", StringComparison.OrdinalIgnoreCase)
                        ? new SmtpEmailNotifier(sp.GetRequiredService<IOptions<NotificationConfig>>())
                        : new ConsoleNotifier();
                });
                services.AddSingleton<ICheckRunner    , DotnetBuildCheckRunner>();
                services.AddSingleton<ILocalTestRunner, DotnetTestRunner>();

                // Repo resolution (Default repo from config), real Git, BMAD checker, draft/PRD generator, and code generator
                services.AddSingleton<IRepoResolver  , DefaultRepoResolver>();
                services.AddSingleton<IGitWorkspace  , ShellGitWorkspace>();
                services.AddSingleton<IBmadChecker   , RepoBmadChecker>();
                services.AddSingleton<IDraftGenerator, ProcessDraftGenerator>();
                services.AddSingleton<ICodeGenerator , ProcessCodeGenerator>();

                // WorkflowEngine is created per run in RunCommandHandler (provider-specific IWorkItemProvider).

                // Command handlers
                services.AddSingleton<RunCommandHandler>();
                services.AddSingleton<IWorkflowRunLogFactory, SerilogWorkflowRunLogFactory>();

                // Runner / dispatcher
                services.AddSingleton<CliRunner>();
            });
        }
    }
}
