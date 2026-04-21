# AgentFlow

AgentFlow is a **.NET CLI** that turns **Azure DevOps work items** into **reviewed, conventional pull requests**. It runs **Cursor + BMAD** for PRD and code, **dotnet test** / **dotnet build** as gates, and **only commits after you add an approval tag** on the work item.

**License:** [MIT](LICENSE)  
**Status:** Early development (v0). Needs **Cursor CLI**, **BMAD** in the repo, and (for real ADO runs) **Azure DevOps** credentials.

---

## Scope and team expectations (v0)

Read this before you roll AgentFlow out to a team or pipeline.

| Topic | Current behavior |
|--------|------------------|
| **Work items per run** | **Exactly one** work item per process (`run --workitem <id>`). There is **no batch queue** inside the tool: to handle N tickets, invoke the CLI **N times** (script, CI matrix, or manual). |
| **Concurrency** | A single run is **one OS process** with one workflow execution. Do not assume parallel tickets in one invocation. |
| **Daemon / background mode** | **Not supported.** There is no `agentflow daemon` or service that polls Azure DevOps for new work items. Each run is started explicitly from the command line or automation. Roadmap: **[Suggested Future Developments (section 8)](docs/project-overview-and-future-developments.md#8-suggested-future-developments)**. |
| **Resume / persisted runs** | **Not supported.** Run state is **not** saved to a local database for "pick up where we left off" after a crash or reboot. If the process stops while waiting for a human (tags, approval), start **`run` again** with the same work item; the workflow re-reads the work item and repo. Planned: **run persistence** in **[section 8.1](docs/project-overview-and-future-developments.md#81-high-value-next-steps-in-workflow-order)**. |
| **Per-project setup** | Each environment (dev machine, build agent) needs its own **`appsettings.json`**: organization, project, PAT, **Workspace:RootPath**, **DefaultRepoUrl**, tags, assignee filter, polling, notifications. This is a **configured integration**, not a global "turn on for the whole org" switch. |
| **Repository cloning** | The tool **does** run **Git** against the **resolved** repository: it clones or updates under **`Workspace:RootPath`** using your configured **default repo** and branch rules (`DefaultRepoResolver`, `ShellGitWorkspace`). It does **not** automatically discover or clone **every** repository in a project; resolution is **rules + config** for that work item. Teams should treat **`Workspace:RootPath`** as a dedicated working area for AgentFlow. |
| **Exit codes** | **`0`** only when the workflow ends in **Done**, **Skipped**, or **StoppedByUser**. **`1`** for failures, invalid CLI args, config errors, **and** paused/waiting states such as waiting for approval or info (scripts should not assume success just because the process finished). Details: **[Exit codes](docs/application-flow.md#exit-codes-vs-runstate)**. |

---

## Table of contents

1. [Scope and team expectations (v0)](#scope-and-team-expectations-v0)
2. [Overview and workflow](#overview-and-workflow)
3. [Prerequisites](#prerequisites)
4. [Quick start](#quick-start)
5. [CLI](#cli)
6. [Fake mode (`--provider fake`)](#fake-mode---provider-fake)
7. [Configuration (`appsettings.json`)](#configuration-appsettingsjson)
8. [Documentation](#documentation)
9. [Security](#security)
10. [Troubleshooting](#troubleshooting)

---

## Overview and workflow

Reference diagram image: [agentflow.png](agentflow.png).

**At a glance:** Load work item, **eligibility + clarity** gates, clone **BMAD** branch, **PRD**, optional **PRD approval**, optional **code gen**, **dotnet test**, **dotnet build**, post **diff**, **final approval**, then **commit / push / PR**. Roadmap items are in [docs/project-overview-and-future-developments.md](docs/project-overview-and-future-developments.md) section 8.

### End-to-end flow

Read **top to bottom**. **Waiting for info** = run pauses (work item can move to **WaitingState**); fix the issue and **run again**. **Poll** = AgentFlow checks the work item on a timer until someone adds the right tag or **max polls** is reached (then the run ends **still waiting**, not **Failed**). **No commit** until you add the final **ApprovalTag** after the diff.

**Main path (happy path)**

```text
agentflow run
  |
  +-- Load work item
  |
  +-- Eligibility: required tag + assignee (if configured)   -->  Skipped if no match
  |
  +-- Clarity: description / repro steps (by work item type) -->  Waiting for info if empty
  |
  +-- Resolve repo -> clone or update -> verify BMAD -> create branch
  |      |
  |      +-- cannot resolve repo --------->  Waiting for info
  |      +-- BMAD missing ---------------->  Waiting for info
  |      +-- base branch missing --------->  Waiting for info
  |
  +-- Generate PRD (Cursor/BMAD)
  |      |
  |      +-- hard failure ---------------------------------->  Failed
  |      +-- agent asks questions on ticket ---------------->  poll PRD tags (reply / regen / stop)
  |      +-- optional "PRD ready" approval (RequirePrdApproval) ->  same PRD poll
  |
  +-- Code gen from PRD (only if EnableCodeGenStep)
  |      |
  |      +-- hard failure ------------->  Failed
  |      +-- agent asks questions ----->  poll code-gen tags (reply / stop)
  |
  +-- dotnet test                -->  Failed if tests fail
  |
  +-- Checks (e.g. dotnet build) -->  Failed if checks fail
  |
  +-- Post diff + notify (you review on the work item)
  |
  +-- Poll final tags
         |
         +-- ApprovalTag  ---------->  commit -> push -> open PR -> Done
         +-- RegenerateCodeTag  ---->  code gen (if on) else tests+checks again, new diff, poll again
         +-- StopTag  -------------->  Stopped by user
         +-- max polls  ------------>  still waiting (approval)
```

**Tag names** are configurable in `AgentFlow:AzureDevOps` (defaults below).


| When | Tags (defaults; all configurable under `AgentFlow:AzureDevOps`) |
|------|----------------------------------------------------------------|
| **PRD poll** (only if `RequirePrdApproval`) | Continue: **PrdApprovalTag** (`agent:prdapproved`). Redo PRD: **RegeneratePrdTag**. Stop: **StopTag**. |
| **After diff** | Ship: **ApprovalTag** (`agent:approved`). Redo code + tests + build: **RegenerateCodeTag**. Stop: **StopTag**. |
| **Code-gen questions** (process exit 2) | Reply in ADO, then **RegenerateCodeTag** or **StopTag**. |

Polling uses **ApprovalPollSeconds** and **ApprovalMaxPolls** for every wait loop. **TestsRun** / **ChecksRun** in code map to **dotnet test** / **dotnet build** ([`RunState.cs`](AgentFlow.Core/Models/RunState.cs)).

---

## Prerequisites


| Requirement          | Notes                                                                   |
| -------------------- | ----------------------------------------------------------------------- |
| **.NET 10 SDK**      | `dotnet --version` should show 10.x.                                    |
| **Git**              | On `PATH`, or `Workspace:GitExecutablePath`.                            |
| **Cursor CLI**       | `agent` non-interactive ([docs](https://cursor.com/docs/cli/overview)). |
| **Azure DevOps PAT** | For `--provider ado` (work items, repos, PRs).                          |
| **BMAD**             | `npx bmad-method install` in the target repo (`_bmad/`).                |


---

## Quick start

```powershell
dotnet build
cd AgentFlow.Cli\bin\Debug\net10.0
.\AgentFlow.exe init
# Edit appsettings.json (at least Workspace:RootPath + AzureDevOps:DefaultRepoUrl for fake), then:
.\AgentFlow.exe run --provider fake --workitem 123 --dry-run
.\AgentFlow.exe run --provider ado --workitem 12345
```

**Cursor rules:** Built output includes `cursor-rules` from [docs/templates/cursor-rules/](docs/templates/cursor-rules/) -- edit `run-prd.ps1`, `run-dev.ps1`, `prompt-prefix.md` there.

---

## CLI

| Command | Purpose |
|---------|---------|
| `init` | Creates `appsettings.json` from the template next to the executable if missing; prints next steps. |
| `run` | `run --provider` with `fake`, `ado`, or `azuredevops`; `run --workitem <id>`; optional `--dry-run`. One work item per invocation ([Scope](#scope-and-team-expectations-v0)). |

| Provider | Notes |
|----------|--------|
| `fake` | No ADO calls; minimal config ([Fake mode](#fake-mode)). Default if `--provider` is omitted. |
| `ado` / `azuredevops` | Full Azure DevOps integration; requires valid `appsettings.json` and workspace. |

**Exit code:** `0` only for **Done**, **Skipped**, **StoppedByUser**. `1` for **Failed**, waiting/paused outcomes, bad args, and config errors ([details](docs/application-flow.md#exit-codes-vs-runstate)).

### Fake mode (`--provider fake`)

Use this to exercise the CLI and workflow **without** Azure DevOps (no PAT, org, or project).

**Minimal `appsettings.json` for a successful run**


| Section         | Keys               | Why                                                                                                      |
| --------------- | ------------------ | -------------------------------------------------------------------------------------------------------- |
| **Workspace**   | **RootPath**       | Must exist; BMAD and git operations resolve under here.                                                  |
| **AzureDevOps** | **DefaultRepoUrl** | Repo URL used to resolve the local clone path (no network fetch required for the fake work item itself). |


Optional: **WorkItemTag**, **AssignedToUser** - if set, the fake work item includes matching fields; if empty, tag/assignee gates are skipped so smoke runs still move forward.

**Polling:** The fake provider **approves** PRD and final approval polls immediately, so loops do not wait for real work item tags.

---

## Configuration (`appsettings.json`)

Root JSON section: **`AgentFlow`**. Template: [AgentFlow.Cli/appsettings.template.json](AgentFlow.Cli/appsettings.template.json). **Do not commit secrets** (PAT, SMTP passwords).

### AzureDevOps


| Key                                                                                           | Description                                         |
| --------------------------------------------------------------------------------------------- | --------------------------------------------------- |
| **OrganizationUrl**, **Project**, **PersonalAccessToken**                                     | ADO org URL, project name, PAT.                     |
| **DefaultRepoUrl**, **DefaultRepoName**                                                       | Clone URL and local folder name (name optional).    |
| **BranchBaseForFeature**, **BranchBaseForBug**, **BranchBaseForOther** | Base branches (`release/*` pattern supported for features; see [project-rules.md](docs/project-rules.md)). |
| **WorkItemTag**, **AssignedToUser**                                                           | **Required** for ADO runs; filter who/what runs.    |
| **ApprovalTag**, **PrdApprovalTag**, **StopTag**, **RegeneratePrdTag**, **RegenerateCodeTag** | Workflow signals (defaults like `agent:approved`).  |
| **WaitingState**, **CodeReviewState**                                                         | States when paused vs after PR.                     |
| **RequirePrdApproval**                                                                        | If true, PRD poll in diagram before code gen.       |


### Workspace


| Key                   | Description                                                 |
| --------------------- | ----------------------------------------------------------- |
| **RootPath**          | Must exist before ADO run.                                  |
| **GitExecutablePath** | Optional full path to `git`.                                |
| **CursorRulesDir**    | Folder with `run-prd` / `run-dev` (default `cursor-rules`). |
| **EnableCodeGenStep** | Skip code gen when false.                                   |
| **TestProjectPath**   | Optional test project hint; empty = discover.               |
| **CommitScope**       | Conventional Commit scope; empty = from repo name.          |


### Polling


| Key                     | Description                                       |
| ----------------------- | ------------------------------------------------- |
| **ApprovalPollSeconds** | Delay between ADO checks.                         |
| **ApprovalMaxPolls**    | Max polls per wait loop; when exceeded, the run typically ends **waiting** (not necessarily `Failed`). Exit code is still **1** unless you change the CLI ([application-flow.md](docs/application-flow.md#exit-codes-vs-runstate)). |


### Notifications


| Key                                                           | Description                   |
| ------------------------------------------------------------- | ----------------------------- |
| **Mode**                                                      | `console` or `smtp`.          |
| **Smtp: Host, Port, EnableSsl, Username, Password, From, To** | Required when Mode is `smtp`. |


---

## Documentation


| Doc                                                                                                  | Content                 |
| ---------------------------------------------------------------------------------------------------- | ----------------------- |
| [docs/index.md](docs/index.md)                                                                       | Documentation index     |
| [docs/application-flow.md](docs/application-flow.md) | CLI entry, `WorkflowEngine`, exit codes |
| [docs/project-overview-and-future-developments.md](docs/project-overview-and-future-developments.md) | Architecture, roadmap   |
| [docs/project-rules.md](docs/project-rules.md)                                                       | Branches, commits, tags |
| [docs/project-source-tree.md](docs/project-source-tree.md)                                           | Code layout             |
| [CONTRIBUTING.md](CONTRIBUTING.md)                                                                   | Contributing            |
| [SECURITY.md](SECURITY.md)                                                                           | Security                |


---

## Security

Keep PATs and SMTP passwords out of git; use local `appsettings.json` or user secrets. Minimum PAT scope. See [SECURITY.md](SECURITY.md).

---

## Troubleshooting


| Issue                 | Check                                                                                                                                                  |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Skipped**           | ADO: `WorkItemTag` + `AssignedToUser` match the work item. Fake: rare if optional gates are off; set tag/assignee in config to align with fake fields. |
| Stuck on **BMAD**     | Install BMAD in repo.                                                                                                                                  |
| Missing PRD path      | Output must include `AGENTFLOW_PRD_PATH=<path>`.                                                                                                       |
| Test/build **Failed** | Fix branch; re-run; logs may be on the work item.                                                                                                      |


---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

---

## License

Copyright 2026 AgentFlow contributors. [MIT License](LICENSE).