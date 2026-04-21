**Author:** Cursor  
**Editor:** Darshana Wijesinghe  
**Created Date:** 16/04/2026  

# Application flow (CLI)

This page maps the **start point** (process entry) to the **end point** (process exit and outcomes) so you can navigate the codebase quickly. For the *business* workflow (PRD → code → tests → approval), see [project-overview-and-future-developments.md](project-overview-and-future-developments.md) and the repo-root [README.md](../README.md).

---

## Start point


| Step | Location                                  | What happens                                                                                                                                                                                                                                                                                                                                        |
| ---- | ----------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1    | `AgentFlow.Cli/Program.cs` → `Main`       | `Host.CreateDefaultBuilder(args)` loads **environment variables** and **command-line** config (see .NET generic host). Then `ConfigureAgentFlowCli` (`AgentFlow.Cli/DI/DependencyInjection.cs`) adds **optional** `appsettings.json` from `AppContext.BaseDirectory` (reload on change). `Build()`, resolve `CliRunner`, `await runner.RunAsync()`. |
| 2    | `AgentFlow.Cli/CliRunner.cs` → `RunAsync` | Uses **all** argv: first token is the **command** (`init`, `run`, or help). For `run`, arguments **after** the word `run` are passed to `RunCommandHandler.ExecuteAsync` (e.g. `AgentFlow.exe run --provider fake --workitem 1` → handler receives `--provider fake --workitem 1`).                                                                 |


---

## Configuration and dependency injection

- **JSON:** `appsettings.json` next to the published executable (optional). Options bind under the `AgentFlow` section (`AppConfiguration.SectionName`) into `AgentFlowOptions`, `AzureDevOpsConfig`, `PollingConfig`, `WorkspaceConfig`, `NotificationConfig`, etc.
- **Injectable `args`:** The raw `string[] args` from `Main` is registered as a **singleton** for any component that needs the full command line.
- **Registered services (typical):** `AddLogging`, `AddHttpClient`; `AzureDevOpsWorkItemProvider`, `FakeWorkItemProvider` (both singletons); `INotifier` → `ConsoleNotifier` or `SmtpEmailNotifier` from `Notifications:Mode`; `ICheckRunner` → `DotnetBuildCheckRunner`; `ILocalTestRunner` → `DotnetTestRunner`; `IRepoResolver` → `DefaultRepoResolver`; `IGitWorkspace` → `ShellGitWorkspace`; `IBmadChecker` → `RepoBmadChecker`; `IDraftGenerator` → `ProcessDraftGenerator`; `ICodeGenerator` → `ProcessCodeGenerator`; `RunCommandHandler`; `IWorkflowRunLogFactory` → `SerilogWorkflowRunLogFactory`; `CliRunner`.
- **Not registered:** `WorkflowEngine` is constructed **per run** in `RunCommandHandler` via `ActivatorUtilities.CreateInstance<WorkflowEngine>(services, workItemProvider)` so the correct `IWorkItemProvider` is injected.

---

## Flow diagram (start → end)

Read **top to bottom**, then **left to right** on branches. Only the **`run`** command branch loads `WorkflowEngine`.

```
Program.Main
    |
    |  CreateDefaultBuilder  +  ConfigureAgentFlowCli  +  Build  +  GetRequiredService<CliRunner>()
    v
CliRunner.RunAsync
    |
    +-- argv[0] empty  OR  -h  OR  --help  --------->  PrintHelp()  --------------------------->  exit 0
    |
    +-- argv[0] == init  --------------------------->  copy appsettings template / hints  ----->  exit 0 or 1
    |
    +-- else (neither help nor init nor run)  ------>  error + PrintHelp()  ------------------->  exit 1
    |
    +-- argv[0] == run  ---------------------------->  RunCommandHandler.ExecuteAsync( argv[1..] )
            |                                              |
            |         parse CLI, validate, optional        |
            |         startup log (preRunId)               |
            |         resolve IWorkItemProvider from DI    |
            |         ActivatorUtilities -> WorkflowEngine |
            v                                              v
        WorkflowEngine.RunAsync  ----->  AgentRun  +  LastStepLogPath  +  optional file log (RunId)
            |
            v
        MapExitCode( RunState )  ----->  exit 0 or 1  ----->  return to Main  ----->  OS process exit
```

**Dispatch table** (matches `CliRunner` order: help → `init` → unknown → `run`):


| `argv[0]`                                           | Goes to                                                | Ends with                                |
| --------------------------------------------------- | ------------------------------------------------------ | ---------------------------------------- |
| *(empty)*, `-h`, `--help`                           | `PrintHelp`                                            | exit **0**                               |
| `init`                                              | Create/locate `appsettings.json`, hints                | exit **0** or **1** (template missing)   |
| any other token **except** `run` (case-insensitive) | Error line + `PrintHelp`                               | exit **1**                               |
| `run`                                               | `RunCommandHandler` → `WorkflowEngine` → `MapExitCode` | exit **0** or **1** (see **Exit codes**) |


**`run` sub-flow (who calls whom):**

| Step | Connection |
| ---- | ---------- |
| 1 | `RunCommandHandler.ExecuteAsync` resolves `IWorkItemProvider` from DI (`Fake*` or `AzureDevOps*`). |
| 2 | `ExecuteAsync` builds `WorkflowEngine` once per run via `ActivatorUtilities` (injects that provider). |
| 3 | `WorkflowEngine.RunAsync` returns `AgentRun` and sets `LastStepLogPath` when file logging is on. |
| 4 | `MapExitCode` maps `RunState` to process exit **0** or **1**. |


**Parallel outputs** (do not change the call order above): console text; optional `…/agentflow-logs/<id>/log.txt` when workspace root is configured.

---

## `init` command


| Outcome                                                                           | Exit code |
| --------------------------------------------------------------------------------- | --------- |
| `appsettings.template.json` missing under `AppContext.BaseDirectory`              | **1**     |
| Template exists; creates `appsettings.json` if missing; prints cursor-rules hints | **0**     |


---

## `run` command (main automation path)

### CLI defaults and validation


| Item                 | Behavior                                                                                                                                                                                                                    |
| -------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `--provider`         | Optional; defaults to `**fake`** if omitted. Allowed: `fake`, `ado`, `azuredevops` (case-insensitive). Unknown value → message and exit **1** before the workflow starts.                                                   |
| `--workitem`         | **Required**; missing → exit **1**.                                                                                                                                                                                         |
| `--dry-run`          | Optional flag; passed through to `WorkflowOptions` (skips real git, tests, checks where implemented).                                                                                                                       |
| Config before engine | `**ado` / `azuredevops`:** `ConfigurationValidator.ValidateOrThrow` + `EnsureWorkspaceExists`. `**fake`:** `ValidateFakeRunOrThrow` + `EnsureWorkspaceExists`. Other providers are rejected earlier (unknown `--provider`). |


### Cancellation

`RunCommandHandler` wires `**Console.CancelKeyPress`** to cancel a `CancellationTokenSource` passed into `WorkflowEngine.RunAsync`, so **Ctrl+C** requests cancellation (workflow handles it and typically returns a failed or terminal `AgentRun` rather than crashing the process).

### Startup logging vs workflow logging


| Phase                                                     | Correlation id                         | Purpose                                                                                                                                       |
| --------------------------------------------------------- | -------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| Config/workspace failure **before** `WorkflowEngine` runs | `preRunId` (new GUID per CLI attempt)  | `WriteStartupConfigurationLog` / `WriteStartupWorkspaceError` — may write under `agentflow-logs/<preRunId>/` when `Workspace:RootPath` is set |
| Successful path into engine                               | `AgentRun.RunId` from `WorkflowEngine` | Per-run step log under `agentflow-logs/<RunId>/log.txt` (when file logging is enabled)                                                        |


If `Workspace:RootPath` is empty, `IWorkflowRunLogFactory` returns `**NullWorkflowRunLog`** — no file, console only.

### High-level order

1. `**RunCommandHandler.ExecuteAsync`** (`AgentFlow.Cli/Commands/RunCommandHandler.cs`)
  - Parse `--provider`, `--workitem`, `--dry-run`.  
  - Resolve `IWorkItemProvider`: `FakeWorkItemProvider` or `AzureDevOpsWorkItemProvider` from DI.  
  - **Configuration gate:** provider-specific validators + `EnsureWorkspaceExists`; on failure, optional startup file log via `IWorkflowRunLogFactory`.  
  - Build `**WorkflowEngine`** with `ActivatorUtilities.CreateInstance<WorkflowEngine>(services, workItemProvider)`.
2. `**WorkflowEngine.RunAsync`** (`AgentFlow.Core/Workflow/WorkflowEngine.cs`)
  - Creates per-run `**IWorkflowRunLog`** via `_runLogFactory.Create(_workspace.RootPath, run.RunId, workItemId)`.  
  - Typical step order: **intake** → **tag/assignee gate** → **clarity gate** → **repo resolution** → **clone / BMAD / branch** → **PRD** (+ optional PRD approval loop) → **optional code gen** (`EnableCodeGenStep`) → `**dotnet test`** → **checks** (`ICheckRunner`) → **diff / wait for approval** → **final approval loop** (regenerate code, tests, checks as needed) → **commit / push / PR** when not dry-run.  
  - Early exits return `AgentRun` in states such as `Skipped`, `WaitingForInfo`, `WaitingForPRDApproval`, `Failed`, etc. Top-level exceptions are caught, logged, and returned as `**Failed`**.  
  - Sets `**LastStepLogPath`** in `finally` from the run log when file logging is enabled.
3. **Return to CLI**
  - Prints run id, `RunState`, message, step log path.  
  - `**MapExitCode`** (see table below).

**Process exit:** `Main` returns the `int` from `CliRunner.RunAsync` — the **observable end point** for scripts and CI.

---

## Exit codes vs `RunState`

`MapExitCode` maps **only** these states to **0**:


| `RunState`      | Exit code |
| --------------- | --------- |
| `Done`          | 0         |
| `Skipped`       | 0         |
| `StoppedByUser` | 0         |


**Every other** final state — including `Failed`, `WaitingForApproval`, `WaitingForInfo`, `WaitingForPRDApproval`, and any non-terminal value not listed above — maps to exit **1**. So a run that pauses waiting for a human still exits **1** unless you extend the CLI to treat specific states differently.

CLI **argument/config errors** (missing `--workitem`, unknown provider, validation failure before the engine) also return **1**.

---

## Where to change behavior


| Concern                | Primary types / files                                                                              |
| ---------------------- | -------------------------------------------------------------------------------------------------- |
| New CLI command        | `CliRunner`, new handler, register in DI if needed                                                 |
| ADO vs fake work items | `RunCommandHandler.ResolveWorkItemProvider`; `AzureDevOpsWorkItemProvider`, `FakeWorkItemProvider` |
| Config validation      | `AgentFlow.Shared/Configuration/ConfigurationValidator.cs`                                         |
| Orchestration / states | `WorkflowEngine`; `RunState` in `AgentFlow.Core/Models/RunState.cs`                                |
| PRD / code-gen scripts | `ProcessDraftGenerator`, `ProcessCodeGenerator`; templates under `docs/templates/cursor-rules/`    |
| File step logs         | `SerilogWorkflowRunLog`, `SerilogWorkflowRunLogFactory`, `NullWorkflowRunLog`                      |
| Notifications          | `ConsoleNotifier`, `SmtpEmailNotifier`, `NotificationConfig`                                       |


---

## Related

- [project-source-tree.md](project-source-tree.md) — folder layout  
- [project-overview-and-future-developments.md](project-overview-and-future-developments.md) — architecture and state machine  
- [index.md](index.md) — documentation index

