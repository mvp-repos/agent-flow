# AgentFlow code generation: implements from the approved PRD using Cursor CLI.
# AgentFlow sets: WORK_ITEM_JSON_PATH, REPO_PATH, GUIDANCE_DIR, PRD_PATH, GIT_ALLOWLIST, GIT_ALLOWLIST_DESCRIPTION,
# and when applicable: AGENTFLOW_HAS_TEST_PROJECT (true/false), AGENTFLOW_TEST_PROJECT_PATH (full path to .csproj or .sln).
# Optional: prompt-prefix.md in this directory for user instructions.

$ErrorActionPreference = "Stop"
$wiPath   = $env:WORK_ITEM_JSON_PATH
$repoPath = $env:REPO_PATH
$guideDir = $env:GUIDANCE_DIR
$prdPath  = $env:PRD_PATH
$gitAllowDesc = $env:GIT_ALLOWLIST_DESCRIPTION
if (-not $gitAllowDesc) { $gitAllowDesc = "Allowed git commands: git status, git diff, git diff --stat, git add, git restore, git checkout -- <path>(s), git branch, git log, git show, git rev-parse. You must NOT run: git commit, git push, git pull, git merge, git rebase, git reset, or any other command not in the allowed list." }

$hasTestProj = $env:AGENTFLOW_HAS_TEST_PROJECT -eq "true"
$testProjPath = $env:AGENTFLOW_TEST_PROJECT_PATH
$testRule = ""
if ($hasTestProj -and $testProjPath) {
    $testRule = @"

7. **Tests:** A test project or solution was detected in this repo: $testProjPath
   - Add or update automated tests there when the change can be covered by tests (match existing test style and frameworks).
   - Prefer tests that prove PRD acceptance criteria; skip only when truly not applicable (e.g. pure config-only with no test hook).
   - Do **not** run `dotnet test` yourself here; AgentFlow runs tests after this step.
"@
}

if (-not $wiPath -or -not (Test-Path -LiteralPath $wiPath)) {
    Write-Error "WORK_ITEM_JSON_PATH not set or file missing."
    exit 1
}
if (-not $repoPath -or -not (Test-Path -LiteralPath $repoPath -PathType Container)) {
    Write-Error "REPO_PATH not set or directory missing."
    exit 1
}
if (-not $prdPath -or -not (Test-Path -LiteralPath $prdPath)) {
    Write-Error "PRD_PATH not set or file missing."
    exit 1
}

$prefix = ""
$prefixPath = Join-Path $guideDir "prompt-prefix.md"
if ($guideDir -and (Test-Path -LiteralPath $prefixPath)) {
    $prefix = [System.IO.File]::ReadAllText($prefixPath).Trim()
    if ($prefix) { $prefix = $prefix + "`n`n" }
}

$prompt = @"
${prefix}Implement the changes specified in the PRD document at: $prdPath

Work item data (JSON): $wiPath. Repo root: $repoPath. Read all guidance .md files in $guideDir before implementing.

Rules:
1. All code and test changes MUST align with the PRD. Do not add features or scope beyond the PRD.
2. After implementing, perform a self-verification: list each PRD requirement or section and briefly confirm how the code or tests satisfy it (file/method or test name). You may append this as a short "PRD verification" section in a comment or in docs/prd if helpful.
3. If there is **no** detected test project in this repo, still implement production code; add tests only if you introduce a new test project consistent with the solution (rare).
4. Git: $gitAllowDesc Do not run git commit, git push, or any command not in the allowlist. AgentFlow will commit only after human approval.
5. **Database:** If this Cursor session exposes SQL-related MCP tools (only when they are actually available), use them for **read-only** discovery when it helps implement the PRD (schema, relationships, safe sample queries within tool limits). If those tools are not available here, rely on the PRD, repo, and work item only. For schema/data changes: document clearly under the PRD doc area (e.g. docs/prd/<work-item-id>-db-changes.md) with summary, migration-style scripts or steps in-repo when applicable, optional rollback, and a short note of any MCP tools used at a high level. Do not delete data or schema without explicit developer permission.
6. When you have doubts or questions for the dev, output your questions clearly (e.g. a numbered list) and exit with code 2. Otherwise implement fully and exit with code 0.
$testRule

This run is unattended. Implement from the PRD, self-verify, then exit 0. If you need dev input, output questions and exit 2.
"@

Push-Location $repoPath
try {
    & agent -p $prompt --trust --force --sandbox disabled
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
