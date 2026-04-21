# AgentFlow — Project Source Tree

**Author:** Cursor  
**Editor:** Darshana Wijesinghe  
**Created Date:** 12/03/2025  

This document shows the project folder and file layout to help navigate the codebase. For **CLI entry → exit flow**, see **[application-flow.md](application-flow.md)**. For architecture and technical details, see **[project-overview-and-future-developments.md](project-overview-and-future-developments.md)**. For rules and policies, see **[project-rules.md](project-rules.md)**. For the documentation index, see **[index.md](index.md)**.

---

## Source tree (relevant paths only)

Build outputs (`bin/`, `obj/`), IDE metadata (`.vs/`), and similar folders are omitted.

```
AgentFlow/
├── AgentFlow.slnx
├── Directory.Packages.props
├── LICENSE
├── agentflow.png                                   # Workflow flowchart (reference image)
├── CONTRIBUTING.md
├── README.md
├── SECURITY.md
│
├── docs/
│   ├── index.md                                    # Documentation entry point
│   ├── application-flow.md                         # CLI Program → CliRunner → WorkflowEngine → exit
│   ├── project-overview-and-future-developments.md
│   ├── project-rules.md
│   ├── project-source-tree.md                      # This file
│   └── templates/
│       └── cursor-rules/                           # Source for CLI output copy (run-prd.ps1, run-dev.ps1, …)
│
├── AgentFlow.Core/
│   ├── AgentFlow.Core.csproj
│   ├── Abstractions/
│   │   ├── ICheckRunner.cs
│   │   ├── ICodeGenerator.cs
│   │   ├── IDraftGenerator.cs
│   │   ├── IGitWorkspace.cs
│   │   ├── ILocalTestRunner.cs
│   │   ├── INotifier.cs
│   │   ├── IRepoResolver.cs
│   │   ├── IBmadChecker.cs
│   │   ├── IWorkItemProvider.cs
│   │   ├── IWorkflowRunLog.cs                      # Per-run step/error/config log (file or no-op)
│   │   ├── IWorkflowRunLogFactory.cs
│   │   └── NullWorkflowRunLog.cs
│   ├── Git/
│   │   └── GitAllowlist.cs
│   ├── Models/
│   │   ├── AgentRun.cs
│   │   ├── ApprovalOutcome.cs
│   │   ├── DraftGeneratorResult.cs
│   │   ├── CodeGeneratorResult.cs
│   │   ├── RepoResolutionResult.cs
│   │   ├── RunState.cs
│   │   ├── WorkItem.cs
│   │   ├── WorkItemAttachment.cs
│   │   ├── WorkItemComment.cs
│   │   └── WorkflowStepResult.cs
│   └── Workflow/
│       ├── WorkflowEngine.cs
│       └── WorkflowOptions.cs
│
├── AgentFlow.Shared/
│   ├── AgentFlow.Shared.csproj
│   ├── Configuration/
│   │   ├── AppConfiguration.cs
│   │   └── ConfigurationValidator.cs
│   └── Helpers/                                   # Text, process, git, path helpers
│
├── AgentFlow.Adapters/
│   ├── AgentFlow.Adapters.csproj
│   ├── AzureDevOps/
│   │   └── AzureDevOpsWorkItemProvider .cs        # IWorkItemProvider (note space in filename)
│   ├── Bmad/
│   │   └── RepoBmadChecker.cs
│   ├── Checks/
│   │   └── DotnetBuildCheckRunner.cs
│   ├── CodeGen/
│   │   └── ProcessCodeGenerator.cs
│   ├── Draft/
│   │   └── ProcessDraftGenerator.cs
│   ├── Git/
│   │   └── ShellGitWorkspace.cs
│   ├── Notifications/
│   │   ├── SmtpEmailNotifier.cs
│   │   └── (ConsoleNotifier in Cli/Fakes)
│   ├── Repo/
│   │   └── DefaultRepoResolver.cs
│   └── Testing/
│       ├── DotnetTestRunner.cs
│       └── TestProjectLocator.cs
│
└── AgentFlow.Cli/
    ├── AgentFlow.Cli.csproj
    ├── Program.cs
    ├── CliRunner.cs
    ├── appsettings.json
    ├── appsettings.template.json
    ├── Properties/
    │   └── launchSettings.json
    ├── DI/
    │   └── DependencyInjection.cs
    ├── Logging/
    │   ├── SerilogWorkflowRunLog.cs
    │   └── SerilogWorkflowRunLogFactory.cs
    ├── Commands/
    │   └── RunCommandHandler.cs
    └── Fakes/
        ├── ConsoleNotifier.cs
        ├── FakeCheckRunner.cs
        ├── FakeGitWorkspace.cs
        └── FakeWorkItemProvider.cs
```

---

## Project references

| Project | References |
|---------|------------|
| **AgentFlow.Cli** | AgentFlow.Adapters, AgentFlow.Core, AgentFlow.Shared |
| **AgentFlow.Adapters** | AgentFlow.Core, AgentFlow.Shared |
| **AgentFlow.Core** | AgentFlow.Shared |
| **AgentFlow.Shared** | (none) |

---

## Where to look

| If you want to… | Look in |
|-----------------|--------|
| Change workflow steps or state transitions | `AgentFlow.Core/Workflow/WorkflowEngine.cs` |
| Change branch naming or base branch rules | `AgentFlow.Adapters/Repo/DefaultRepoResolver.cs` |
| Change ADO work item / approval / comments / PR | `AgentFlow.Adapters/AzureDevOps/AzureDevOpsWorkItemProvider .cs` |
| Change Git operations (clone, branch, commit, push) | `AgentFlow.Adapters/Git/ShellGitWorkspace.cs` |
| Change PRD or code-gen process scripts | `docs/templates/cursor-rules/` (built to `cursor-rules` under the CLI output) |
| Add or change config options | `AgentFlow.Shared/Configuration/AppConfiguration.cs`, then `ConfigurationValidator.cs` if required |
| Add a new CLI command | `AgentFlow.Cli/CliRunner.cs`, command handler, register in `DependencyInjection.cs` |
| Add a new adapter | New class in `AgentFlow.Adapters` implementing the Core interface; register in `AgentFlow.Cli/DI/DependencyInjection.cs` |
| Fake vs ADO provider selection | `AgentFlow.Cli/Commands/RunCommandHandler.cs` (`--provider fake \| ado \| azuredevops`) |
| Step / file logging (Serilog) | `AgentFlow.Cli/Logging/SerilogWorkflowRunLog*.cs`, `AgentFlow.Core/Abstractions/IWorkflowRunLog*.cs` |
| End-to-end CLI flow (diagram) | [docs/application-flow.md](application-flow.md) |
