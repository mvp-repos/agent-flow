**Author:** Cursor  
**Editor:** Darshana Wijesinghe  
**Created Date:** 16/04/2026  

# AgentFlow documentation index

Use this page as the entry point for generated and maintained project documentation.

| Document | Description |
|----------|-------------|
| [README.md](../README.md) | Overview, setup, configuration, CLI usage, troubleshooting |
| [application-flow.md](application-flow.md) | **Start here for code navigation:** `Program` → `CliRunner` → `run` → `WorkflowEngine` → exit codes, DI, logging |
| [project-overview-and-future-developments.md](project-overview-and-future-developments.md) | Architecture, state machine, implementation summary, configuration reference, future work |
| [project-rules.md](project-rules.md) | Branch naming, commits, eligibility, approval tags, Cursor rules, ADO behavior |
| [project-source-tree.md](project-source-tree.md) | Repository layout and where to change code |
| [templates/cursor-rules/README.md](templates/cursor-rules/README.md) | PRD/code-gen scripts (`run-prd.ps1`, `run-dev.ps1`), `prompt-prefix.md` |

**Diagram:** High-level workflow image: [agentflow.png](../agentflow.png) (repository root).

**Configuration schema:** `AgentFlow.Cli/appsettings.template.json` and `AgentFlow.Shared/Configuration/AppConfiguration.cs`.

**Contributing / security:** [CONTRIBUTING.md](../CONTRIBUTING.md), [SECURITY.md](../SECURITY.md).
