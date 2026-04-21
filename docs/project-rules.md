# AgentFlow — Project Rules

**Author:** Cursor  
**Editor:** Darshana Wijesinghe  
**Created Date:** 12/03/2025  

This document defines the rules and policies that the AgentFlow project follows. For **architecture and technical details**, see **[project-overview-and-future-developments.md](project-overview-and-future-developments.md)**. For **CLI execution flow** (commands, exit codes), see **[application-flow.md](application-flow.md)**. Update this file when adding or changing rules.

---

## 1. Branch creation policy

Rules for how branches are named and which base branch is used. Implemented in `AgentFlow.Adapters/Repo/DefaultRepoResolver.cs` and configured via `AgentFlow:AzureDevOps` in appsettings.

### 1.1 Branch naming by work item type


| Work item type (System.WorkItemType) | Branch name pattern    | Example                          |
| ------------------------------------ | ---------------------- | -------------------------------- |
| Feature                              | `feature/<id>-<slug>`  | `feature/12345-add-login-button` |
| Product Backlog Item                 | `feature/<id>-<slug>`  | `feature/12346-fix-dashboard`    |
| Bug                                  | `bug/<id>-<slug>`      | `bug/12347-null-ref-on-save`     |
| Other (Task, etc.)                   | `users/agentflow/<id>` | `users/agentflow/12348`          |


- Work item type is read from the work item’s **System.WorkItemType** field. Values **Feature**, **Product Backlog Item**, and **Bug** are matched exactly (case-sensitive); any other or missing value is treated as Other.
- **Slug** is derived from the work item **Title** (see 1.2). If type is “Other”, no slug is used.

### 1.2 Title slug rules (for branch name segment)

- **Source:** Work item title.
- **Max length:** 40 characters.
- **Allowed characters:** Letters, digits; spaces, hyphens, and underscores become a single hyphen.
- **Casing:** Lowercase.
- **Empty/missing title:** Slug becomes `"work"`.
- **Result:** Git-safe segment only (e.g. `add-login-button-to-homepage`). No consecutive hyphens; trailing hyphens are removed.
- **Note:** The slug is built directly from the full title (character-by-character up to the max length). A possible future improvement is to derive a short title from the actual title (e.g. whole-word truncation or optional LLM summarization) before slugifying, so the branch name stays readable when titles are long.

### 1.3 Base branch by work item type


| Work item type       | Config key           | Default     | Notes                                                                               |
| -------------------- | -------------------- | ----------- | ----------------------------------------------------------------------------------- |
| Feature              | BranchBaseForFeature | `release/`* | Pattern supported: latest matching remote branch by version (e.g. `release/1.3.0`). |
| Product Backlog Item | BranchBaseForFeature | `release/*` | Same as Feature.                                                                    |
| Bug                  | BranchBaseForBug     | `main`      | Typically a fixed branch name.                                                      |
| Other                | BranchBaseForOther   | `main`      | Empty config = use current HEAD after clone.                                        |


- **Pattern `release/`*:** Resolver picks the latest remote branch matching the prefix by semantic-style version (e.g. `release/1.3.0` > `release/1.2.0`). If no match exists, the workflow pauses and comments on the work item.
- **Fixed base (e.g. `main`):** Branch must exist on remote; otherwise workflow pauses with a comment.

---

## 2. Commit and code conventions

- **Commit messages:** Use Conventional Commits. Format: `<type>(<scope>): <short summary>`. Reference the ADO work item or PR thread ID at the end in brackets (e.g. `ADO #12345`). Types: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `perf`, `ci`, `build`, `revert`.
- **One logical change per commit;** message describes what and why, not how. No “wip” or “tmp” commits on main branches; commits should build and pass tests.
- **Code documentation:** Use `<summary>`, `<param>`, `<returns>`, and `<exception>` to describe **current** behavior. Use `<remarks>` for (1) **implementation or lifecycle** notes (e.g. when dispose runs, what is not logged), and/or (2) **changelogs** when you change existing members: what changed, why, ADO work item reference; you may include **Author** and **Last Updated** in `<remarks>` (e.g. inside `<para>`) for an audit trail in source. **Commit messages** must still use Conventional Commits and ADO references—changelogs in XML supplement git history; they do not replace commits.
- **Code style:** Inline comments for non-obvious logic; follow existing project patterns.

---

## 3. Work item eligibility and single-item runs

- **Single work item per run.** The CLI runs for exactly one work item: `agentflow run --provider ado --workitem <id>`. No batch or multi-item execution. Future daemon mode (if added) would pick one eligible item per run.
- **WorkItemTag (required):** `AgentFlow:AzureDevOps:WorkItemTag` must be set (e.g. `agent:task`). Only work items that have this tag are processed; others end in Skipped. Validated by ConfigurationValidator at run start; if missing, the run does not start.
- **AssignedToUser (required):** `AgentFlow:AzureDevOps:AssignedToUser` must be set (e.g. ADO display name). Validated by ConfigurationValidator at run start; if missing, the run does not start. Only work items assigned to that user are processed; others end in Skipped. Each developer sets their own value.

---

## 4. Workflow and approval rules

- **No commit until human approval.** The agent must not commit until an explicit approval signal (e.g. ADO tag `agent:approved` or configured equivalent). Diff is presented for review first.
- **Approval signal:** Configurable via `AgentFlow:AzureDevOps:ApprovalTag` (default `agent:approved`). Implementations check work item tags/fields/comments as configured.
- **Post-approval PR creation:** After approval, AgentFlow commits and pushes, opens an ADO pull request, notifies the user with the PR link, and updates the work item state to `AgentFlow:AzureDevOps:CodeReviewState` (default “Code Review”).
- **PRD approval gate (configurable):** When `AgentFlow:AzureDevOps:RequirePrdApproval` is true, after PRD generation the workflow notifies the user and waits for **PrdApprovalTag** (continue), **RegeneratePrdTag** (re-run PRD with latest ADO data), or **StopTag** (stop). When false, the workflow continues without a PRD review step.
- **PRD data freshness:** ADO work item is re-fetched immediately before every PRD generate (first time and on regenerate) so any ticket updates are included.
- **Regenerate PRD:** In the PRD approval loop, **RegeneratePrdTag** (default `agent:regenerate-prd`) triggers re-run of the PRD step with latest ticket data; overwrite is controlled by guidance files (Cursor reads all docs before making changes).
- **Cursor rules directory (Option 3):** **CursorRulesDir** (optional, default `cursor-rules`) is the single directory for all Cursor rule files. The build copies **docs/templates/cursor-rules** to **cursor-rules** in the application directory; the end user edits those files. Relative path is resolved from the application directory. Passed as GUIDANCE_DIR; run-prd.cmd or run-prd.ps1 runs Cursor CLI with `/bmad-bmm-quick-spec`; optional prompt-prefix.md adds user-specific instructions (e.g. read these docs and links).
- **Code generation step:** When **EnableCodeGenStep** (AgentFlow:Workspace) is true, after PRD approval the workflow runs run-dev.ps1 to implement from the PRD. The LLM may only run **git commands from the allowlist** (status, diff, add, restore, checkout -- , branch, log, show, rev-parse). No commit, push, pull, merge, rebase, or destructive branch operations. See **AgentFlow.Core.Git.GitAllowlist**.
- **PRD path for code gen:** PRD path is obtained from the LLM. The PRD generation step (BMAD) uses its normal output (e.g. _bmad-output/implementation-artifacts); the agent must output one line AGENTFLOW_PRD_PATH= so AgentFlow can pass it to code generation. No directories are forced for the PRD. If the path is missing or the file is not found when code gen runs, the run fails with a clear message.
- **Stop tag:** Tag `agent:stop` (configurable via `StopTag`) stops the workflow when present during the PRD or code-gen approval loop; run ends in StoppedByUser.
- **Code-gen questions:** When the agent has questions during code generation, AgentFlow posts them and **polls** for **RegenerateCodeTag** (default `agent:regenerate-code`) or StopTag. Reply in the work item comment, then add the tag to re-run code gen; the same run continues and re-fetches the work item (with your reply) before re-running. No need to start a new process.
- **Diff review changes requested (post-checks):** After tests and checks pass, AgentFlow posts a diff summary for review and waits for approval. If the reviewer requests changes, they add a comment describing the requested edits and add **RegenerateCodeTag** (default `agent:regenerate-code`). AgentFlow removes the tag, re-runs code generation (when `EnableCodeGenStep` is true) using the latest work item comments as guidance, re-runs tests and checks, then posts an updated diff summary for review. This loop can repeat until `agent:approved` is added or `agent:stop` is added.
- **Waiting state:** When the workflow pauses (e.g. requirements unclear, repo not resolved, base branch not found), the work item state is set to the configured waiting state (`AgentFlow:AzureDevOps:WaitingState`; default “Waiting for info”) and a comment is added. User is notified.
- **Failure handling:** On unhandled exception or guardrail failure, the run moves to Failed and the user is notified; no commit is made.

---

## 5. Configuration rules

- **Secrets:** Personal access tokens and other secrets must not be hardcoded. Use config (e.g. appsettings) or environment variables; do not log secrets.
- **Required config (ADO provider):** When running with provider `ado`/`azuredevops`, the following must be set (validated at run start by `ConfigurationValidator` in `AgentFlow.Shared`): `AgentFlow:AzureDevOps:OrganizationUrl`, `AgentFlow:AzureDevOps:Project`, `AgentFlow:AzureDevOps:PersonalAccessToken`, `AgentFlow:Workspace:RootPath`, `AgentFlow:AzureDevOps:DefaultRepoUrl`, `AgentFlow:AzureDevOps:WorkItemTag`, `AgentFlow:AzureDevOps:AssignedToUser`.
- **Config file:** Primary config is `appsettings.json`; `appsettings.template.json` is the template. The `init` command copies the template to create a local config.

---

## 6. Environment and tooling

- **Shell:** Default environment uses **Windows PowerShell**. Do not use `&&` for chaining commands; use `;` or separate commands.
- **Paths:** Prefer backslashes for Windows paths in config; quote paths that contain spaces.
- **Azure DevOps:** When using ADO MCP or project context, assume project **“CIS-Portfolio”** unless overridden by config or context.

---

## 7. Where rules are implemented


| Rule area                                          | Primary implementation                                                                                                                                                                   |
| -------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Branch naming & base branch                        | `AgentFlow.Adapters/Repo/DefaultRepoResolver.cs`                                                                                                                                         |
| Base-branch pattern (e.g. release/*)               | `AgentFlow.Adapters/Git/ShellGitWorkspace.cs` (ResolveLatestMatchingBranchAsync)                                                                                                         |
| Approval tag / waiting state / PRD approval signal | `AgentFlow.Adapters/AzureDevOps/AzureDevOpsWorkItemProvider .cs`, `AgentFlow.Core/Workflow/WorkflowEngine.cs`                                                                            |
| PRD generation                                     | `AgentFlow.Core/Abstractions/IDraftGenerator.cs`, `AgentFlow.Adapters/Draft/ProcessDraftGenerator.cs`                                                                                    |
| Code generation / git allowlist                    | `AgentFlow.Core/Abstractions/ICodeGenerator.cs`, `AgentFlow.Core/Git/GitAllowlist.cs`, `AgentFlow.Adapters/CodeGen/ProcessCodeGenerator.cs`, `AgentFlow.Core/Workflow/WorkflowEngine.cs` |
| Work item tag / AssignedToUser (eligibility)       | `AgentFlow.Core/Workflow/WorkflowEngine.cs` (after intake)                                                                                                                               |
| BMAD verification (after clone)                    | `AgentFlow.Core/Workflow/WorkflowEngine.cs`, `AgentFlow.Adapters/Bmad/RepoBmadChecker.cs`                                                                                                |
| Config validation                                  | `AgentFlow.Shared/Configuration/ConfigurationValidator.cs`                                                                                                                               |
| Config schema                                      | `AgentFlow.Shared/Configuration/AppConfiguration.cs`                                                                                                                                     |


When changing a rule, update both this document and the corresponding code or config.