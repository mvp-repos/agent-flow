# Cursor rules directory template

This folder (**docs/templates/cursor-rules**) is the single source for the Cursor rules. The CLI project copies it to the application output as **cursor-rules**; you edit the files there (run-prd.ps1, prompt-prefix.md, etc.). No separate template folder—this is the only copy.

## What’s here

| File | Purpose |
|------|--------|
| **run-prd.ps1** | Script AgentFlow runs for PRD generation. Invokes Cursor CLI with `/bmad-bmm-quick-spec` and passes work item path, repo path, and guidance dir. |
| **run-prd.cmd** | Wrapper so AgentFlow can run the PowerShell script on Windows. |
| **run-dev.ps1** | Script AgentFlow runs for code generation after PRD approval. Implements from the PRD; receives PRD path and **git allowlist** (only allowed git commands may be run; no commit/push). |
| **run-dev.cmd** | Wrapper for run-dev.ps1 on Windows. |
| **prompt-prefix.md** | Optional. User-specific instructions prepended to the Cursor prompt (e.g. “Read these docs before generating: [links]”). |
| **README.md** | This file. |

## Requirements

- **Cursor CLI** installed and on PATH (e.g. `agent`). See [Cursor CLI docs](https://cursor.com/docs/cli/overview). Install: `irm 'https://cursor.com/install?win32=true' \| iex` (PowerShell).
- **BMAD** quick-spec available in the repo (e.g. `/bmad-bmm-quick-spec` as a Cursor skill or command).

## Unattended flags (run-prd.ps1)

The script passes **--trust**, **--force**, and **--sandbox disabled** so AgentFlow can run PRD generation without prompts:

| Flag | Purpose |
|------|--------|
| `--trust` | Trust the workspace without prompting. |
| `--force` | Auto-approve running commands (no "ask to run" prompt). |
| `--sandbox disabled` | Allow network access so the agent can fetch ADO attachments, use web search, etc. |

For stricter security (e.g. allow only specific domains), use **--sandbox enabled** and add a `.cursor/sandbox.json` in the repo with a `networkPolicy` allowlist (see [Cursor sandbox reference](https://cursor.com/docs/reference/sandbox)).

## Flow

1. AgentFlow sets env: **WORK_ITEM_JSON_PATH**, **REPO_PATH**, **GUIDANCE_DIR** (this directory).
2. AgentFlow runs **run-prd.cmd** or **run-prd.ps1** with working directory = repo root.
3. The script builds a prompt: optional **prompt-prefix.md** content + “Run /bmad-bmm-quick-spec …” and passes it to Cursor CLI: `agent -p "..."`.
4. Cursor runs in the repo and should read all guidance files in **GUIDANCE_DIR** before generating the PRD.
5. BMAD uses its normal output location (e.g. `_bmad-output/implementation-artifacts`). Do not force or create directories for the PRD. After generating, the agent **must output exactly one line**: `AGENTFLOW_PRD_PATH=<path>` (path relative to repo root or absolute) so the code generation step can use it.

## User-specific prompt

Edit **prompt-prefix.md** to add instructions and links, for example:

```markdown
Read these docs before generating the PRD:
- [API contract](https://wiki.example.com/api)
- [Architecture](https://wiki.example.com/arch)
Coding patterns are in coding-patterns.md in this directory.
```

Leave **prompt-prefix.md** empty or remove it if you don’t need custom instructions.

## When the agent asks questions (BMAD quick-spec)

The prompt tells the agent: **when you have doubts or questions for the dev, ask them first** — output your questions and **exit with code 2**. AgentFlow then:

1. **Posts the agent’s questions** as a comment on the work item.
2. **Notifies you** and enters the approval loop with message “Waiting for dev to answer questions”.
3. You **reply to that comment** on the ticket with your answers.
4. You add the tag **agent:regenerate-prd** on the work item.
5. AgentFlow **re-runs PRD generation** with the same work item; the work item JSON now includes a **Comments** section (discussion on the ticket), so the agent sees your answers and can generate the full PRD.

If the agent has no questions, it generates the PRD and exits with code 0 as usual. You can still **pre-answer** in **prompt-prefix.md** (e.g. “Use Zoll/ESO XML path; reuse NERIS settings; ESO-only scope”) so the agent may not need to ask.

**Same tag every time:** We use the same tag (**agent:regenerate-prd**) for the first re-run and for any follow-up re-runs. AgentFlow removes that tag from the work item after each run, so you add the same tag again when you want to trigger the next run. The tag list does not grow; no extra tags are created.

## Code generation (run-dev)

After PRD approval, when **EnableCodeGenStep** is true, AgentFlow runs **run-dev.cmd** or **run-dev.ps1**. The script receives **PRD_PATH** (reported by the PRD generation step: the LLM/BMAD outputs a line `AGENTFLOW_PRD_PATH=<path>` so AgentFlow gets the path; BMAD normally saves under `_bmad-output/implementation-artifacts`). **GIT_ALLOWLIST** and **GIT_ALLOWLIST_DESCRIPTION**. The agent must implement only from the PRD, self-verify, add tests in the repo’s test project if needed, and **use only allowed git commands** (e.g. `git status`, `git diff`, `git add`). The agent must **not** run `git commit`, `git push`, or any other disallowed command; AgentFlow commits only after human approval. If the PRD involves database changes, the agent should document them in `docs/prd/<workItemId>-db-changes.md`. Exit 0 = success; exit 2 = questions for the dev (AgentFlow posts them and **polls**).

**When the agent has questions during code gen:** AgentFlow posts the questions as a comment and stays in a polling loop (same run). Reply to the comment with your answers, then add the tag **agent:regenerate-code** on the work item. AgentFlow will see the tag, remove it, re-fetch the work item (including your reply in Comments), and re-run code generation. Add **agent:stop** to stop instead. Same tag every time: we remove it after each run so you can add it again for follow-up questions.
