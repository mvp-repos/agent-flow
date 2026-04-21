# AgentFlow PRD generation: runs Cursor CLI with /bmad-bmm-quick-spec and optional user prompt.
# AgentFlow sets: $env:WORK_ITEM_JSON_PATH, $env:REPO_PATH, $env:GUIDANCE_DIR (this directory).
# Optional: add prompt-prefix.md in this directory with user-specific instructions (e.g. "Read these docs before generating: [links]").

$ErrorActionPreference = "Stop"
$wiPath   = $env:WORK_ITEM_JSON_PATH
$repoPath = $env:REPO_PATH
$guideDir = $env:GUIDANCE_DIR

if (-not $wiPath -or -not (Test-Path -LiteralPath $wiPath)) {
    Write-Error "WORK_ITEM_JSON_PATH not set or file missing."
    exit 1
}
if (-not $repoPath -or -not (Test-Path -LiteralPath $repoPath -PathType Container)) {
    Write-Error "REPO_PATH not set or directory missing."
    exit 1
}

# Optional user prompt: "read these docs before generating" and links (in prompt-prefix.md)
$prefix = ""
$prefixPath = Join-Path $guideDir "prompt-prefix.md"
if ($guideDir -and (Test-Path -LiteralPath $prefixPath)) {
    $prefix = [System.IO.File]::ReadAllText($prefixPath).Trim()
    if ($prefix) { $prefix = $prefix + "`n`n" }
}

# Build prompt: user instructions + run quick-spec, use work item JSON and guidance dir.
# When the agent has questions: output them and exit with code 2 so AgentFlow posts them to the ticket; dev answers in comments and re-runs with agent:regenerate-prd.
$prompt = @"
${prefix}Run /bmad-bmm-quick-spec. Work item data (JSON): $wiPath. Repo root: $repoPath. Read all guidance .md files in $guideDir before generating the PRD.

Rules:
1. **BMAD output:** Use BMAD's normal output location (e.g. _bmad-output\implementation-artifacts or the path your BMAD setup uses). Do not create or force directories for the PRD; let BMAD behave as usual.
2. **Database / SQL MCP:** If this Cursor session exposes SQL-related MCP tools (only when they are actually available), you may use them for **read-only** database discovery when the work item or repo needs schema/data context for a solid PRD. If no such tools appear in this session, proceed without them. Summarize any MCP-assisted discovery briefly in the PRD or notes (what you inspected, not secrets). Do not run destructive DDL/DML; describe proposed data model changes and migrations in the PRD text instead.
3. **AgentFlow handoff:** After generating the PRD, output exactly one line so AgentFlow can pass the path to the code generation step. Path can be relative to repo root or absolute. Format:
   AGENTFLOW_PRD_PATH=<path-to-the-generated-PRD-document>
4. **Questions vs. done:** When you have doubts or questions for the dev, output your questions clearly (e.g. a numbered list) and exit with code 2. Do not generate the PRD until the dev has answered (they reply in work item comments; they add tag agent:regenerate-prd to re-run, and the work item JSON will include a Comments section with their answers). If you have no questions, generate the full PRD, output the AGENTFLOW_PRD_PATH line, and exit with code 0.

This run is unattended. Follow the rules above; exit 2 only when you need dev input before the PRD can be written.
"@

Push-Location $repoPath
try {
    # Cursor CLI non-interactive (AgentFlow runs unattended):
    #   --trust       : do not prompt for workspace trust
    #   --force (-f)  : auto-approve running commands (no "ask to run" prompt)
    #   --sandbox disabled : allow network access for ADO attachments, web search, etc.
    # Requires Cursor CLI installed (https://cursor.com/docs/cli/overview)
    & agent -p $prompt --trust --force --sandbox disabled
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
