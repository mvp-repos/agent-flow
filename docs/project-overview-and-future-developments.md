# AgentFlow — Project Overview and Guide for Future Developments

**Author:** Cursor  
**Editor:** Darshana Wijesinghe  
**Created Date:** 12/03/2025

This document covers **architecture and technical implementation** only. For the **CLI process flow** (entry point, dispatch, `WorkflowEngine`, exit codes), see **[docs/application-flow.md](application-flow.md)**. For **project rules** (branch creation policy, commit and code conventions, workflow/approval, configuration, environment), see **[docs/project-rules.md](project-rules.md)**.

---

## 1. Purpose and Vision

AgentFlow is an open-source CLI tool that turns **assigned work items into safe, guardrailed pull requests** — **without committing anything until a human approves the diff.**

- **Target users:** Teams using Azure DevOps (ADO), Cursor, and BMAD-style workflows (quick-spec, quick-dev) for features and bug fixes.
- **Value:** Reduces busywork (repo setup, branching, PR creation, process compliance) while keeping a **human-in-the-loop** before any commit or PR.
- **Differentiator:** Guardrails and approval gates are first-class; the agent never commits until explicit approval (e.g. ADO tag `agent:approved`).

---

## 2. High-Level Architecture

- **Trigger:** Work item assigned (e.g. to you) — later: ADO Service Hooks or polling.
- **Orchestrator:** State machine running the workflow (Intake → Clarity → Repo → Draft → Checks → Approval → Commit → Push → PR → Done).
- **Execution:** Git + ADO via adapters; Cursor CLI invokes BMAD quick-spec and run-dev scripts for PRD and code generation (`ProcessDraftGenerator`, `ProcessCodeGenerator`).
- **Guardrails:** Clarity gate, repo confidence, diff/build/test gates, approval gate, and configurable policies (max files/lines, blocked paths, etc.).

**Design principle:** *Cursor (or any LLM) is “the author”; the orchestrator is “the judge”.*

---

## 3. Repository and Solution Layout


| Path                   | Purpose                                                                                                                              |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| **AgentFlow.Core**     | State machine, models, and abstractions (no ADO/Git implementation).                                                                 |
| **AgentFlow.Cli**      | Console app: commands (`run`, `init`), DI, fakes for testing.                                                                        |
| **AgentFlow.Adapters** | Azure DevOps (work items, comments, state, approval), Git (shell), Repo (default resolver).                                          |
| **AgentFlow.Shared**   | Configuration DTOs and validation.                                                                                                   |
| **docs/**              | Project documentation: [index.md](index.md), [application-flow.md](application-flow.md), rules, source tree, cursor-rules templates. |


- **Target framework:** .NET 10 (`net10.0`) across projects.
- **Package management:** Central versions in `Directory.Packages.props` (e.g. Microsoft.Extensions.*, Newtonsoft.Json).

---

## 4. State Machine (RunState)

Defined in `AgentFlow.Core/Models/RunState.cs`. Order of states:


| State                   | Value | Meaning                                                                                                                              |
| ----------------------- | ----- | ------------------------------------------------------------------------------------------------------------------------------------ |
| Intake                  | 0     | Starting; loading work item.                                                                                                         |
| ClarityCheck            | 10    | Evaluating if requirements are clear enough.                                                                                         |
| WaitingForInfo          | 20    | Paused; commented on ticket; waiting for user input.                                                                                 |
| RepoSelection           | 30    | Resolving repo URL and local path.                                                                                                   |
| RepoPrepared            | 40    | Repo cloned or present locally.                                                                                                      |
| BmadVerified            | 45    | BMAD (quick-spec tooling) verified after clone; required before branch creation.                                                     |
| BranchCreated           | 50    | Branch created/checked out.                                                                                                          |
| DraftGenerated          | 60    | PRD generated; working tree may have PRD artifacts only; **no commit**.                                                              |
| WaitingForPRDApproval   | 65    | PRD ready; waiting for user tag (e.g. `agent:prdapproved` or `agent:stop`) when RequirePrdApproval is true.                          |
| ImplementationGenerated | 67    | Code generated from PRD (run-dev); working tree has code + tests; **no commit**. After PRD approval, when EnableCodeGenStep is true. |
| TestsRun                | 68    | Local `dotnet test` executed when a test target exists; before general `ICheckRunner` checks.                                        |
| ChecksRun               | 70    | Additional checks executed (e.g. `dotnet build` via `DotnetBuildCheckRunner`).                                                       |
| WaitingForApproval      | 80    | Human must approve (e.g. tag `agent:approved`).                                                                                      |
| Committed               | 90    | Changes committed after approval.                                                                                                    |
| Pushed                  | 100   | Branch pushed to remote.                                                                                                             |
| PullRequestOpened       | 110   | Pull request created in Azure DevOps (`CreatePullRequestAsync`); work item moved to Code Review state.                               |
| Done                    | 200   | Workflow completed.                                                                                                                  |
| Skipped                 | 800   | Run refused: work item missing required tag or not assigned to configured user.                                                      |
| StoppedByUser           | 850   | User added `agent:stop`; workflow stopped without commit.                                                                            |
| Failed                  | 900   | Error or guardrail triggered.                                                                                                        |


---

## 5. Core Abstractions (Ports)

All in `AgentFlow.Core/Abstractions/`:

- **IWorkItemProvider** — Get work item, add comment, update state, remove tags, create pull request; GetWorkItemSignalAsync returns ApprovalOutcome (Pending, Approved, Stopped, RegeneratePrd, RegenerateCode). Parameters continueApprovalTag, regeneratePrdTag, regenerateCodeTag select which tag means “continue” vs regenerate in PRD vs final approval loops.
- **IGitWorkspace** — EnsureRepo, CreateBranch (with optional base branch), GetDiffSummary, Commit, Push.
- **ICheckRunner** — Run additional checks at a repo path (e.g. dotnet build); returns success + log.
- **ILocalTestRunner** — Run automated tests after code generation (e.g. dotnet test); returns success + log.
- **INotifier** — Send notification (subject + body); console or SMTP email via NotificationConfig.
- **IRepoResolver** — Resolve repo URL, local path, branch name, and base branch for a work item.
- **IBmadChecker** — Verify BMAD is installed *in the repository* (after clone, before branch). BMAD is not an external tool; it is installed in the repo via `npx bmad-method install` (creates `_bmad/`, `_bmad/bmm/`); used to pause with “Comment to install BMAD” when not found.
- **IDraftGenerator** — Generate PRD (e.g. Cursor CLI + BMAD quick-spec); receives full ADO work item data and repo path; completion = process exit success.
- **ICodeGenerator** — Generate implementation from approved PRD (e.g. Cursor CLI via run-dev.ps1); receives work item, repo path, and PRD path; uses **git allowlist** (no commit/push); completion = process exit success.
- **IWorkflowRunLog** / **IWorkflowRunLogFactory** — Per-run diagnostics (steps, errors, configuration issues); CLI uses Serilog file logs under `Workspace:RootPath`/`agentflow-logs/<runId>/` when configured, else **NullWorkflowRunLog**.

The **WorkflowEngine** (`AgentFlow.Core/Workflow/WorkflowEngine.cs`) depends on these and drives the state transitions. Adapters (ADO, Git, Repo) live in **AgentFlow.Adapters** and implement the interfaces.

---

## 6. Current Implementation Summary

### 6.1 Workflow steps (as implemented)

1. **Intake** — Fetch work item via `IWorkItemProvider.GetWorkItemAsync`. **Single work item only:** each run processes exactly one work item (CLI: `run --workitem <id>`). No batch or multi-item runs.
2. **Tag and assignee (required)** — `WorkItemTag` and `AssignedToUser` are required in config. **Before** `WorkflowEngine` runs: `ConfigurationValidator.ValidateOrThrow` (ADO/`azuredevops`) or `ValidateFakeRunOrThrow` (`fake`) plus `EnsureWorkspaceExists`. In **RunAsync**, only work items that have the configured tag and are assigned to the configured user are processed; others end in Skipped.
3. **Clarity gate** — `HasRequiredContentForAutomation(wi)`: requires non-empty description for non-bug types; Bugs have no description and require only non-empty Repro Steps (Microsoft.VSTS.TCM.ReproSteps).
4. **Repo resolution** — `IRepoResolver.ResolveAsync`: uses config default repo (`DefaultRepoUrl`, `Workspace.RootPath`). Branch name and base branch are derived from work item type and config; see **project-rules.md** for the branch creation policy.
5. **Repo + BMAD + branch** — `IGitWorkspace.EnsureRepoAsync` (clone if missing); then `IBmadChecker.IsBmadInstalledAsync` (verify BMAD per workflow diagram; if not installed, comment “install BMAD”, set waiting state, notify, and return); then `CreateBranchAsync` (with optional base branch). Base-branch-not-found is caught and workflow pauses with comment + notification.
6. **Draft** — PRD generation via IDraftGenerator. **Work item is re-fetched from ADO immediately before every PRD generate** (first time and on regenerate). One directory CursorRulesDir holds all Cursor rule files (Option 3); run-prd.cmd or run-prd.ps1 inside it is executed. Process receives WORK_ITEM_JSON_PATH, REPO_PATH, and GUIDANCE_DIR (the rules directory).
7. **Code generation (optional)** — When AgentFlow:Workspace:EnableCodeGenStep is true, after PRD approval the workflow runs ICodeGenerator (run-dev.ps1). PRD path is **from the LLM**: the PRD generation step must output a line AGENTFLOW_PRD_PATH= (e.g. _bmad-output/implementation-artifacts/...); BMAD’s normal output location is used and no directories are forced. Code gen uses that path. When a test project or solution is found, AgentFlow sets AGENTFLOW_HAS_TEST_PROJECT and AGENTFLOW_TEST_PROJECT_PATH so run-dev can instruct the agent to add/update tests (it does **not** run dotnet test itself). The LLM may only run **git commands from the allowlist** (status, diff, add, restore, checkout -- , branch, log, show, rev-parse); no commit, push, pull, merge, rebase, or destructive operations. See AgentFlow.Core.Git.GitAllowlist. On agent questions (exit 2), workflow posts comment and polls for RegenerateCodeTag (e.g. agent:regenerate-code) or StopTag; dev replies and adds the tag to re-run code gen in the same process.
8. **Local tests (dotnet test)** — After code gen (or after PRD-only flow when code gen is disabled), ILocalTestRunner (DotnetTestRunner) runs dotnet test against Workspace:TestProjectPath if set, else discovers a *Test*.csproj or a root .sln. If tests fail, the workflow notifies, comments on the work item with logs, and fails. If no test target exists, this step succeeds with a skip message.
9. **Checks** — `ICheckRunner.RunAsync` via **DotnetBuildCheckRunner** (`dotnet build` on the root `.sln` if present, else `dotnet build` in the repo folder). On failure: notify, comment on the work item with log, **Failed**; dev fixes and re-runs the CLI for the same work item. Additional scripts/linters can be added later.
10. **Diff summary** — `IGitWorkspace.GetDiffSummaryAsync` for review notification.
11. **Approval loop** — Poll `IWorkItemProvider.GetWorkItemSignalAsync` (approval signal is configurable; see **project-rules.md**) with configurable interval and max polls.
12. **Commit / Push / PR** — On approval, commit and push, open an Azure DevOps pull request, notify the user, and set the work item state to the configured Code Review state.

### 6.2 Configuration (appsettings.json)

- **AgentFlow:AzureDevOps:** OrganizationUrl, Project, PersonalAccessToken, ApprovalTag, WaitingState, **CodeReviewState** (state after PR is opened), DefaultRepoUrl, DefaultRepoName, BranchBaseForFeature/Bug/Other, **WorkItemTag** (required), **AssignedToUser** (required). **RequirePrdApproval**, **PrdApprovalTag** (e.g. `agent:prdapproved`), **StopTag** (e.g. `agent:stop`), **RegeneratePrdTag** (e.g. `agent:regenerate-prd`; in PRD approval loop, re-run PRD with latest ticket data), **RegenerateCodeTag** (e.g. `agent:regenerate-code`; when the agent has questions during code gen, add this tag after replying to re-run code gen). WorkItemTag and AssignedToUser validated at run start.
- **AgentFlow:Workspace:** RootPath, GitExecutablePath, **CursorRulesDir** (optional; default `cursor-rules`; run-prd and run-dev scripts live here), **EnableCodeGenStep** (optional; when true, after PRD approval run code generation via run-dev.ps1; default true), **TestProjectPath** (optional; relative path to test .csproj, folder, or project name hint; used for code-gen env and dotnet test resolution; empty = auto-discover), **CommitScope** (optional; Conventional Commit scope for automated commits; empty = derived from repo name). PRD path is not configured: the PRD generation process outputs AGENTFLOW_PRD_PATH= and code gen uses that path. The build copies **docs/templates/cursor-rules** to **cursor-rules** in the application directory; the end user edits the files there.
- **AgentFlow:Notifications:** Mode (`console` or `smtp`); when `smtp`, **Smtp** subsection (Host, Port, EnableSsl, Username, Password, From, To).
- **AgentFlow:Polling:** ApprovalPollSeconds, ApprovalMaxPolls.

Validation: `ConfigurationValidator.ValidateOrThrow` when running with provider `ado`/`azuredevops`; `ValidateFakeRunOrThrow` when provider is `fake` (minimal settings for local runs). Both paths use `EnsureWorkspaceExists` where applicable. The `init` command copies `appsettings.template.json` to `appsettings.json` in the application directory (the template is included in the CLI project and copied to output).

### 6.3 CLI commands

- **init** — Copy appsettings.template.json to appsettings.json (if missing). Cursor rules are in cursor-rules (build copies from docs/templates/cursor-rules); edit run-prd.ps1, prompt-prefix.md there as needed.
- **run** — `run --provider <fake|ado|azuredevops> --workitem <id> [--dry-run]` — One work item per invocation. **ado** / **azuredevops** use **AzureDevOpsWorkItemProvider** (requires valid `appsettings.json`). **fake** uses **FakeWorkItemProvider** (no ADO). Default `--provider` is **fake** if omitted. Exit **0** only for `RunState` **Done**, **Skipped**, or **StoppedByUser**; **1** otherwise (see **[application-flow.md](application-flow.md)**).

### 6.4 Notable adapters

- **AzureDevOpsWorkItemProvider** — REST API (work items, comments, state, tags for approval). Attachments supported with proper download URL.
- **ShellGitWorkspace** — All Git operations via shell (`git clone`, `checkout`, `diff --stat`, `add -A`, `commit`, `push`). Supports configurable Git executable and `release/*` base-branch resolution.
- **DefaultRepoResolver** — Single default repo from config; branch naming and base branch by work item type per project rules.

---

## 7. Intended Workflow and Current Coverage

**Workflow diagram:** The full workflow (intake, clarity gate, repo selection, branch, PRD/draft, checks, human review, approval, commit, push, PR) is documented in the flowchart image in the repository root (`agentflow.png`). It aligns with the state machine (Section 4) and the implementation in `WorkflowEngine.cs`. The "next steps" in Section 8 follow the same order (draft → checks → approval → PR).

The target “task-to-PR” flow includes:

- Requirements clarity gate (with comment + “Waiting for info” + email).
- Repo selection and workspace setup (clone, branch).
- **No commit until human approval** — review diff, then approve (tag/field/comment).
- After approval: commit, push, create PR, notify.
- Guardrails: clarity threshold, repo confidence, diff size, blocked paths, build/test gates, etc.

Current codebase already implements:

- State machine and core flow (intake → clarity → repo → BMAD → branch → PRD generation → optional PRD approval → optional code generation → dotnet test → dotnet build → diff review → approval → commit → push → PR).
- Clarity gate (rules-based: description / repro steps by work item type).
- Repo resolver and Git with base-branch handling (including `release/`* resolution).
- PRD and code generation via Cursor CLI scripts in **CursorRulesDir**; git allowlist enforced for code gen.
- Local tests (`ILocalTestRunner`) and additional checks (`ICheckRunner` / `dotnet build`).
- Approval via ADO tags and configurable polling; regenerate PRD/code flows; SMTP or console notifications.
- Pull request creation and work item state update to Code Review after push.
- No commit until human approval.

Remaining gaps to address in **future developments** (see Section 8).

---

## 8. Suggested Future Developments

### 8.1 High value (next steps, in workflow order)

1. **Run persistence + resume** — Persist run state (e.g. SQLite/JSON) so that “WaitingForApproval” and “WaitingForPRDApproval” can survive process restarts. On resume: re-validate branch and diff before commit.
2. **Configurable check runner** — Extend the current `dotnet build` runner to support configurable scripts/commands and multi-step checks (linters, format, security scans). Runs after tests, before WaitingForApproval.
3. **Notification UX** — Improve email templates and optionally support multiple channels (SMTP + console + Teams/Slack). Keep console as an option.
4. **PR ↔ work item linking** — Explicitly link the created PR to the work item via ADO APIs where applicable (beyond comments and state), plus optional reviewers and policies.

### 8.2 Guardrails (config-driven)

1. **Guardrail engine** — Configurable rules in config (e.g. YAML or appsettings):
  - max files changed / max lines changed;
  - blocked paths (e.g. `infra/`, `auth/`);
  - require tests for certain folders;
  - optional PRD approval gate (see below).
2. **Diff policy checks** — After PRD/code generation, run policy before checks: reject or pause if diff exceeds limits or touches blocked paths; comment on ticket and notify.

### 8.3 PRD and approval gates

1. **Stronger clarity gate** — Optional LLM or rules (acceptance criteria, repro steps, impacted module, DB changes) and store clarity score/reasons on work item for audit. (PRD approval gate is already configurable via **RequirePrdApproval** / **PrdApprovalTag**.)

### 8.4 Operations and UX

1. **Daemon mode** — `agentflow daemon`: poll ADO for assigned work items; start new runs and resume approved ones (best paired with run persistence). Configurable poll interval.
2. **Abort / disable** — `agentflow abort --workitem <id>`: mark run aborted, comment on ticket, leave branch intact. Support ADO tag `agent:disabled` to skip items.
3. **Packaging** — Publish as global `dotnet tool` and optionally self-contained binaries; `agentflow doctor` to validate config and connectivity.
4. **Service Hooks** — Optional ADO webhook to trigger runs or resume on tag/state change instead of polling.

### 8.5 Out of scope for early phases

- GUI automation of Visual Studio.
- Deep MSSQL automation (if ever: read-only by default, allowlist, no destructive ops without approval).
- Multiple LLM providers and complex prompt frameworks.
- Hosting/UI/dashboard.

---

## 9. Document and Config References

- **Workflow diagram:** Flowchart image in the repository root (`agentflow.png`) shows intake → approval → PR → done. Keep it aligned with `RunState.cs` and `WorkflowEngine.cs`.
- **Project source tree:** Folder and file layout for navigating the codebase: **[docs/project-source-tree.md](project-source-tree.md)**.
- **Project rules:** Branch creation policy, commit/code conventions, approval, config, and environment rules are in **[docs/project-rules.md](project-rules.md)**. Update that doc when adding or changing rules.
- **Architecture / config / guardrails:** To be expanded in `docs/` (e.g. `architecture.md`, `config.md`, `guardrails.md`) as the project grows.
- **Master index:** [docs/index.md](index.md) is the documentation entry point. **CLI flow:** [docs/application-flow.md](application-flow.md).
- **Config schema:** `appsettings.template.json` and `AgentFlow.Shared/Configuration/`* define the current schema. BMAD is detected in-repo (presence of `_bmad/bmm/`). Any new guardrails or adapters should extend these or a future `agentflow.yaml`.

---

## 10. Quick Reference — Key Files


| Area                             | File(s)                                                                                                                                                 |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| State machine                    | `AgentFlow.Core/Models/RunState.cs`, `AgentRun.cs`                                                                                                      |
| Workflow orchestration           | `AgentFlow.Core/Workflow/WorkflowEngine.cs`, `WorkflowOptions.cs`                                                                                       |
| Work item contract               | `AgentFlow.Core/Abstractions/IWorkItemProvider.cs`                                                                                                      |
| Git contract                     | `AgentFlow.Core/Abstractions/IGitWorkspace.cs`                                                                                                          |
| Repo resolution                  | `AgentFlow.Core/Abstractions/IRepoResolver.cs`, `Models/RepoResolutionResult.cs`                                                                        |
| ADO adapter                      | `AgentFlow.Adapters/AzureDevOps/AzureDevOpsWorkItemProvider .cs` (note space in filename)                                                               |
| Git adapter                      | `AgentFlow.Adapters/Git/ShellGitWorkspace.cs`                                                                                                           |
| Repo resolver impl               | `AgentFlow.Adapters/Repo/DefaultRepoResolver.cs`                                                                                                        |
| BMAD checker                     | `AgentFlow.Core/Abstractions/IBmadChecker.cs`, `AgentFlow.Adapters/Bmad/RepoBmadChecker.cs`                                                             |
| PRD generation                   | `AgentFlow.Core/Abstractions/IDraftGenerator.cs`, `AgentFlow.Adapters/Draft/ProcessDraftGenerator.cs`                                                   |
| Code generation / git allowlist  | `AgentFlow.Core/Abstractions/ICodeGenerator.cs`, `AgentFlow.Core/Git/GitAllowlist.cs`, `AgentFlow.Adapters/CodeGen/ProcessCodeGenerator.cs`             |
| Local dotnet test (post-codegen) | `AgentFlow.Core/Abstractions/ILocalTestRunner.cs`, `AgentFlow.Adapters/Testing/DotnetTestRunner.cs`, `AgentFlow.Adapters/Testing/TestProjectLocator.cs` |
| Additional checks (dotnet build) | `AgentFlow.Core/Abstractions/ICheckRunner.cs`, `AgentFlow.Adapters/Checks/DotnetBuildCheckRunner.cs`                                                    |
| Config                           | `AgentFlow.Shared/Configuration/AppConfiguration.cs`, `ConfigurationValidator.cs`                                                                       |
| CLI entry                        | `AgentFlow.Cli/Program.cs`, `CliRunner.cs`, `Commands/RunCommandHandler.cs`                                                                             |
| DI                               | `AgentFlow.Cli/DI/DependencyInjection.cs`                                                                                                               |
| Workflow run log (CLI)           | `AgentFlow.Cli/Logging/SerilogWorkflowRunLog.cs`, `SerilogWorkflowRunLogFactory.cs`; abstractions in `AgentFlow.Core/Abstractions/IWorkflowRunLog*.cs`  |
| CLI → engine flow (doc)          | [docs/application-flow.md](application-flow.md)                                                                                                         |
| Workflow diagram                 | Flowchart image in repo root (`agentflow.png`)                                                                                                          |
| Project source tree              | [docs/project-source-tree.md](project-source-tree.md)                                                                                                   |
| Project rules                    | [docs/project-rules.md](project-rules.md)                                                                                                               |


Use this document for architecture and technical reference. For the project folder layout, see **[docs/project-source-tree.md](project-source-tree.md)**. For rules and policies, see **[docs/project-rules.md](project-rules.md)**.