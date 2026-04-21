using AgentFlow.Core.Abstractions;
using AgentFlow.Core.Models;
using AgentFlow.Shared.Helpers;
using AgentFlow.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace AgentFlow.Core.Workflow
{
    /// <summary>
    /// Orchestrates the end-to-end workflow for a single work item: intake, tag/assignee gate, clarity gate,
    /// repo resolution, workspace preparation (clone, BMAD verification, branch creation), PRD generation (via
    /// <see cref="IDraftGenerator"/>), optional PRD approval loop, code generation (via <see cref="ICodeGenerator"/> when enabled),
    /// local <c>dotnet test</c> via <see cref="ILocalTestRunner"/> when a test target exists, checks via <see cref="ICheckRunner"/>,
    /// human review request, approval loop, and commit/push on approval. Delegates to <see cref="IWorkItemProvider"/>,
    /// <see cref="IRepoResolver"/>, <see cref="IGitWorkspace"/>, <see cref="IBmadChecker"/>, <see cref="ICheckRunner"/>,
    /// <see cref="ILocalTestRunner"/>, <see cref="IDraftGenerator"/>, <see cref="ICodeGenerator"/>, and <see cref="INotifier"/>; uses <see cref="AzureDevOpsConfig"/>
    /// and <see cref="WorkspaceConfig"/> for tag, assignee, waiting state, PRD approval, and code-gen settings. No commit occurs until approval.
    /// Uses <see cref="IWorkflowRunLogFactory"/> for per-run logs (Serilog file in the CLI; swappable).
    /// </summary>
    public sealed class WorkflowEngine
    {
        // Services
        private readonly IWorkItemProvider      _workItems;
        private readonly INotifier              _notifier;
        private readonly IGitWorkspace          _git;
        private readonly ICheckRunner           _checks;
        private readonly ILocalTestRunner       _localTests;
        private readonly IRepoResolver          _repoResolver;
        private readonly IBmadChecker           _bmadChecker;
        private readonly IDraftGenerator        _draftGenerator;
        private readonly ICodeGenerator         _codeGenerator;
        private readonly AzureDevOpsConfig      _cfgAzure;
        private readonly WorkspaceConfig        _workspace;
        private readonly IWorkflowRunLogFactory _runLogFactory;

        private IWorkflowRunLog?                _runLog;

        /// <summary>
        /// Gets the step log file path from the last completed <see cref="RunAsync"/> (typically under workspace <c>agentflow-logs/&lt;runId&gt;/log.txt</c> when file logging is enabled).
        /// </summary>
        /// <value>The absolute path from <see cref="IWorkflowRunLog.LogFilePath"/> after the run, or <see langword="null"/> before a run or when logging is disabled.</value>
        public string? LastStepLogPath { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowEngine"/> class with the adapters and options required to run one work item end-to-end.
        /// </summary>
        /// <param name="workItems">Azure DevOps or fake work item operations.</param>
        /// <param name="notifier">Console or SMTP notifications.</param>
        /// <param name="git">Clone, branch, diff, commit, and push operations.</param>
        /// <param name="checks">Post-<c>dotnet test</c> checks (e.g. <c>dotnet build</c>).</param>
        /// <param name="localTests">Local test runner (e.g. <c>dotnet test</c>).</param>
        /// <param name="repoResolver">Resolves default or work-item-specific repository URL and branch name.</param>
        /// <param name="bmadChecker">Verifies BMAD is installed in the cloned repo.</param>
        /// <param name="draftGenerator">PRD / draft generation step.</param>
        /// <param name="codeGenerator">Code generation from PRD when enabled.</param>
        /// <param name="cfgAzure">Tags, assignee, PRD/code approval tags, and related Azure DevOps settings.</param>
        /// <param name="workspace">Workspace root, code-gen toggle, and commit scope.</param>
        /// <param name="runLogFactory">Creates the per-run <see cref="IWorkflowRunLog"/> (file or no-op).</param>
        public WorkflowEngine(
            IWorkItemProvider workItems,
            INotifier notifier,
            IGitWorkspace git,
            ICheckRunner checks,
            ILocalTestRunner localTests,
            IRepoResolver repoResolver,
            IBmadChecker bmadChecker,
            IDraftGenerator draftGenerator,
            ICodeGenerator codeGenerator,
            IOptions<AzureDevOpsConfig> cfgAzure,
            IOptions<WorkspaceConfig> workspace,
            IWorkflowRunLogFactory runLogFactory)
        {
            _workItems      = workItems;
            _notifier       = notifier;
            _git            = git;
            _checks         = checks;
            _localTests     = localTests;
            _repoResolver   = repoResolver;
            _bmadChecker    = bmadChecker;
            _draftGenerator = draftGenerator;
            _codeGenerator  = codeGenerator;
            _cfgAzure       = cfgAzure.Value;
            _workspace      = workspace.Value;
            _runLogFactory  = runLogFactory;
        }

        /// <summary>
        /// Executes the workflow for a given work item: intake, tag/assignee gate, clarity check, repo resolution,
        /// workspace preparation (clone, BMAD verification, branch creation), PRD generation via <see cref="IDraftGenerator"/>,
        /// optional PRD approval loop, optional code generation, <c>dotnet test</c>, checks (e.g. <c>dotnet build</c>),
        /// human review request, approval loop, and commit/push on approval. Progress and failures are written to
        /// <see cref="IWorkflowRunLog"/> (steps, <see cref="IWorkflowRunLog.Error"/>, <see cref="IWorkflowRunLog.Configuration"/>).
        /// Only a single work item is processed per run; work items are filtered by WorkItemTag and AssignedToUser (config).
        /// The work item is re-fetched from ADO immediately before every PRD generate (first run and on <see cref="ApprovalOutcome.RegeneratePrd"/>)
        /// so latest ticket data is used. When <see cref="AzureDevOpsConfig.RequirePrdApproval"/> is true, the workflow notifies
        /// the user and polls for <see cref="AzureDevOpsConfig.PrdApprovalTag"/> (continue), <see cref="AzureDevOpsConfig.RegeneratePrdTag"/>
        /// (re-run PRD with latest data), or <see cref="AzureDevOpsConfig.StopTag"/> (stop). No commit occurs until approval.
        /// </summary>
        /// <param name="provider">The source provider of the work item (e.g. <c>fake</c>, <c>ado</c>).</param>
        /// <param name="workItemId">The unique identifier of the work item to process.</param>
        /// <param name="options">The workflow execution options, including dry-run mode and approval polling configuration.</param>
        /// <param name="ct">The cancellation token to cancel the operation if needed.</param>
        /// <returns>
        /// An <see cref="AgentRun"/> representing the final state of the workflow execution, including any messages or errors encountered.
        /// </returns>
        /// <remarks>
        /// Most failures (including cancellation) are caught, logged via <see cref="IWorkflowRunLog"/>, and returned as <see cref="AgentRun"/> with an appropriate <see cref="RunState"/> (often <see cref="RunState.Failed"/>).
        /// After completion, <see cref="LastStepLogPath"/> is set from the run log when file logging is enabled.
        /// </remarks>
        public async Task<AgentRun> RunAsync(string provider, string workItemId, WorkflowOptions options, CancellationToken ct)
        {
            LastStepLogPath = null;

            // Initial run record
            var run = new AgentRun(
                RunId     : Guid.NewGuid().ToString("N"),
                Provider  : provider,
                WorkItemId: workItemId,
                State     : RunState.Intake,
                StartedAt : DateTimeOffset.UtcNow,
                UpdatedAt : DateTimeOffset.UtcNow,
                Message   : "Starting"
            );

            _runLog = _runLogFactory.Create(_workspace.RootPath, run.RunId, workItemId);
            try
            {
                try
                {
                    _runLog.Step("Run", $"provider={provider} dryRun={options.DryRun}");

                    // 1) Intake
                    var wi = await _workItems.GetWorkItemAsync(workItemId, ct);
                    run = run with { State = RunState.ClarityCheck, UpdatedAt = DateTimeOffset.UtcNow, Message = $"Loaded work item: {wi.Title}" };
                    _runLog.Step("Intake", wi.Title);

                    var requiredTag    = _cfgAzure.WorkItemTag.Trim();
                    var assignedToUser = _cfgAzure.AssignedToUser.Trim();

                    // 2) Tag and assignee gate
                    var tagResult = ValidateTagAndAssignee(wi, run, requiredTag, assignedToUser);
                    run = tagResult.Run;
                    if (!tagResult.ShouldContinue)
                    {
                        _runLog.Configuration(run.Message ?? "Tag or assignee gate failed");
                        return run;
                    }

                    _runLog.Step("Tag and assignee gate", "passed");

                    // 3) Clarity gate (description or repro steps by type)
                    var clarityResult = await ValidateClarityAsync(workItemId, wi, run, ct);
                    run = clarityResult.Run;
                    if (!clarityResult.ShouldContinue)
                    {
                        _runLog.Configuration(run.Message ?? "Clarity gate: requirements unclear");
                        return run;
                    }

                    _runLog.Step("Clarity gate", "passed");

                    // 4) Repo resolution
                    run = run with { State = RunState.RepoSelection, UpdatedAt = DateTimeOffset.UtcNow, Message = "Resolving repo" };
                    _runLog.Step("Repo resolution", "starting");
                    var resolveResult = await ResolveRepoAsync(wi, run, ct);
                    run = resolveResult.Run;
                    if (!resolveResult.ShouldContinue || resolveResult.Resolution is null)
                    {
                        _runLog.Configuration(run.Message ?? "Repository could not be resolved");
                        return run;
                    }

                    var resolution = resolveResult.Resolution;
                    _runLog.Step("Repo resolution", $"localPath={resolution.LocalPath}");

                    var prepareResult = await PrepareWorkspaceAndBranchAsync(workItemId, options, resolution, run, ct);
                    run = prepareResult.Run;
                    if (!prepareResult.ShouldContinue)
                    {
                        _runLog.Configuration(run.Message ?? "Workspace preparation failed (BMAD or branch)");
                        return run;
                    }

                    var localPath  = resolution.LocalPath!;
                    var branchName = resolution.BranchName!;
                    _runLog.Step("Workspace ready", $"branch={branchName} path={localPath}");

                    // 5) PRD generation and optional approval loop
                    var prdResult = await RunPrdGenerationAndApprovalAsync(workItemId, localPath, branchName, options, run, ct);
                    run = prdResult.Run;
                    if (!prdResult.ShouldContinue)
                    {
                        if (run.State == RunState.Failed)
                            _runLog.Error(run.Message ?? "PRD generation / approval failed");
                        else if (run.State == RunState.StoppedByUser)
                            _runLog.Step("PRD generation / approval", run.Message);
                        else
                            _runLog.Configuration(run.Message ?? "PRD generation / approval paused or timed out");
                        return run;
                    }

                    _runLog.Step("PRD generation / approval", "completed");

                    // 5b) Code generation from PRD (when enabled)
                    if (_workspace.EnableCodeGenStep)
                    {
                        var codeGenResult = await RunCodeGenerationAsync(workItemId, localPath, options, run, ct);
                        run = codeGenResult.Run;
                        if (!codeGenResult.ShouldContinue)
                        {
                            if (run.State == RunState.Failed)
                                _runLog.Error(run.Message ?? "Code generation failed");
                            else if (run.State == RunState.StoppedByUser)
                                _runLog.Step("Code generation", run.Message);
                            else
                                _runLog.Configuration(run.Message ?? "Code generation paused or waiting");
                            return run;
                        }

                        _runLog.Step("Code generation", "completed");
                    }
                    else
                    {
                        _runLog.Step("Code generation", "skipped (EnableCodeGenStep=false)");
                    }

                    // 6) Local automated tests (dotnet test) — after implementation, before general checks
                    run = run with { State = RunState.TestsRun, UpdatedAt = DateTimeOffset.UtcNow, Message = "Running dotnet test" };
                    _runLog.Step("dotnet test", "starting");
                    (bool testsOk, string testLog) = options.DryRun
                        ? (true, "Dry-run: dotnet test not executed.")
                        : await _localTests.RunAsync(localPath, ct);

                    if (!testsOk)
                    {
                        _runLog.Error("dotnet test failed");
                        _runLog.Step("dotnet test output", TextFx.TruncateForWorkItemComment(testLog, 8000));
                        await _notifier.NotifyAsync(
                            subject: $"[AgentFlow] Tests failed for {workItemId}",
                            body   : testLog,
                            ct);
                        var commentBody = "**AgentFlow – automated tests failed**\n\n```\n" + TextFx.TruncateForWorkItemComment(testLog) + "\n```";
                        await _workItems.AddCommentAsync(workItemId, commentBody, ct);
                        return run with { State = RunState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Message = "dotnet test failed" };
                    }

                    _runLog.Step("dotnet test", "passed");

                    // 7) Checks (additional scripts / linters via ICheckRunner)
                    run = run with { State = RunState.ChecksRun, UpdatedAt = DateTimeOffset.UtcNow, Message = "Running checks" };
                    _runLog.Step("Checks (e.g. dotnet build)", "starting");
                    (bool checksOk, string checksLog) = options.DryRun
                        ? (true, "Dry-run: checks not executed.")
                        : await _checks.RunAsync(localPath, ct);

                    if (!checksOk)
                    {
                        _runLog.Error("Checks failed (e.g. dotnet build)");
                        _runLog.Step("Checks output", TextFx.TruncateForWorkItemComment(checksLog, 8000));
                        await _notifier.NotifyAsync(
                            subject: $"[AgentFlow] Checks failed for {workItemId}",
                            body   : checksLog,
                            ct);
                        var checksComment = "**AgentFlow – build/checks failed (e.g. dotnet build)**\n\n```\n" + TextFx.TruncateForWorkItemComment(checksLog) + "\n```\n\nFix the branch locally or in the repo, then **run AgentFlow again** for this work item (`run --workitem <id>`). The run does not continue until build succeeds.";
                        await _workItems.AddCommentAsync(workItemId, checksComment, ct);
                        return run with { State = RunState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Message = "Checks failed (dotnet build)" };
                    }

                    _runLog.Step("Checks (e.g. dotnet build)", "passed");

                    // 8) Review request
                    var diff = options.DryRun ? "Dry-run: no diff." : await _git.GetDiffSummaryAsync(localPath, ct);
                    run      = run with { State = RunState.WaitingForApproval, UpdatedAt = DateTimeOffset.UtcNow, Message = "Waiting for human approval (no commit yet)" };
                    _runLog.Step("Post diff", "waiting for approval tags");
                    await _notifier.NotifyAsync(
                        subject: $"[AgentFlow] Review required for {workItemId}",
                        body   : $"Branch: {branchName}\n\nDiff summary:\n{diff}\n\nApprove in the work item to proceed. To request changes, add a comment with your requested edits and add tag {_cfgAzure.RegenerateCodeTag?.Trim() ?? "agent:regenerate-code"} to trigger another iteration.",
                        ct);
                    await _workItems.AddCommentAsync(
                        workItemId,
                        $"**AgentFlow – diff ready for review**\n\nDiff summary:\n```\n{diff}\n```\n\n- Approve: add tag **{_cfgAzure.ApprovalTag?.Trim() ?? "agent:approved"}**\n- Request changes: add a comment describing requested edits, then add tag **{_cfgAzure.RegenerateCodeTag?.Trim() ?? "agent:regenerate-code"}** (AgentFlow will update the working tree, re-run tests/build, and post a new diff)\n- Stop: add tag **{_cfgAzure.StopTag?.Trim() ?? "agent:stop"}**",
                        ct);

                    // 9) Poll for approval, changes request, or stop; on changes request, loop with code
                    // regeneration, tests, checks, and updated diff until approval or max polls reached
                    return await WaitForFinalApprovalAsync(workItemId, localPath, branchName, resolution.BaseBranch, options, run, diff, ct);
                }
                catch (Exception ex)
                {
                    _runLog.Error("Unhandled workflow exception", ex);
                    await _notifier.NotifyAsync(
                        subject: $"[AgentFlow] Failed work item {workItemId}",
                        body   : ex.ToString(),
                        ct);

                    return run with { State = RunState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Message = ex.Message };
                }
            }
            finally
            {
                LastStepLogPath = _runLog?.LogFilePath;
                _runLog?.Dispose();
                _runLog = null;
            }
        }

        /// <summary>
        /// Polls the work item for final workflow signals (approve, stop, or request changes via regenerate-code) and progresses
        /// the workflow accordingly. On regenerate-code, re-runs code generation (if enabled), tests, checks, and posts an updated diff for review.
        /// </summary>
        /// <param name="workItemId">The work item identifier.</param>
        /// <param name="localPath">Absolute path to the repository root.</param>
        /// <param name="branchName">Current branch name (for notifications and pull request creation).</param>
        /// <param name="baseBranchSpec">Target branch name for the pull request (e.g. <c>main</c>); may be null to default in PR creation.</param>
        /// <param name="options">Workflow options (dry-run and polling configuration).</param>
        /// <param name="run">Current run state.</param>
        /// <param name="diff">Latest diff summary posted for review.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// An updated <see cref="AgentRun"/> representing the outcome (done, stopped, failed, or still waiting).
        /// </returns>
        private async Task<AgentRun> WaitForFinalApprovalAsync(
            string workItemId,
            string localPath,
            string branchName,
            string? baseBranchSpec,
            WorkflowOptions options,
            AgentRun run,
            string diff,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(_runLog);
            _runLog.Step("Final approval poll", $"interval={options.ApprovalPollInterval} maxPolls={options.ApprovalMaxPolls}");

            for (int i = 0; i < options.ApprovalMaxPolls; i++)
            {
                ct.ThrowIfCancellationRequested();
                var regenCodeTag = _cfgAzure.RegenerateCodeTag?.Trim() ?? "agent:regenerate-code";
                var outcome      = await _workItems.GetWorkItemSignalAsync(workItemId, ct, regenerateCodeTag: regenCodeTag);

                if (outcome == ApprovalOutcome.Stopped)
                {
                    _runLog.Step("Final approval", "stopped by user (StopTag)");
                    return run with { State = RunState.StoppedByUser, UpdatedAt = DateTimeOffset.UtcNow, Message = "Stopped by user (agent:stop)" };
                }

                if (outcome == ApprovalOutcome.RegenerateCode)
                {
                    _runLog.Step("Final approval", "regenerate code requested");
                    await _workItems.RemoveTagAsync(workItemId, regenCodeTag, ct);

                    // Re-run code generation (agent applies requested edits based on latest work item comments), then tests and checks
                    if (_workspace.EnableCodeGenStep)
                    {
                        var codeGenResult = await RunCodeGenerationAsync(workItemId, localPath, options, run, ct);
                        run               = codeGenResult.Run;
                        if (!codeGenResult.ShouldContinue) return run;
                    }

                    run = run with { State = RunState.TestsRun, UpdatedAt = DateTimeOffset.UtcNow, Message = "Running dotnet test" };
                    (bool reTestsOk, string reTestLog) = options.DryRun
                        ? (true, "Dry-run: dotnet test not executed.")
                        : await _localTests.RunAsync(localPath, ct);
                    if (!reTestsOk)
                    {
                        _runLog.Error("dotnet test failed (after regenerate)");
                        _runLog.Step("dotnet test output (regenerate)", TextFx.TruncateForWorkItemComment(reTestLog, 8000));
                        await _notifier.NotifyAsync(
                            subject: $"[AgentFlow] Tests failed for {workItemId}", 
                            body   : reTestLog, 
                            ct);
                        var commentBody = "**AgentFlow – automated tests failed**\n\n```\n" + TextFx.TruncateForWorkItemComment(reTestLog) + "\n```";
                        await _workItems.AddCommentAsync(workItemId, commentBody, ct);
                        return run with { State = RunState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Message = "dotnet test failed" };
                    }

                    run = run with { State = RunState.ChecksRun, UpdatedAt = DateTimeOffset.UtcNow, Message = "Running checks" };
                    (bool reChecksOk, string reChecksLog) = options.DryRun
                        ? (true, "Dry-run: checks not executed.")
                        : await _checks.RunAsync(localPath, ct);
                    if (!reChecksOk)
                    {
                        _runLog.Error("Checks failed after regenerate");
                        _runLog.Step("Checks output (regenerate)", TextFx.TruncateForWorkItemComment(reChecksLog, 8000));
                        await _notifier.NotifyAsync(
                            subject: $"[AgentFlow] Checks failed for {workItemId}", 
                            body   : reChecksLog, 
                            ct);
                        var checksComment = "**AgentFlow – build/checks failed (e.g. dotnet build)**\n\n```\n" + TextFx.TruncateForWorkItemComment(reChecksLog) + "\n```";
                        await _workItems.AddCommentAsync(workItemId, checksComment, ct);
                        return run with { State = RunState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Message = "Checks failed (dotnet build)" };
                    }

                    // Post updated diff for review and continue polling for approval/another change request
                    diff = options.DryRun ? "Dry-run: no diff." : await _git.GetDiffSummaryAsync(localPath, ct);
                    run  = run with { State = RunState.WaitingForApproval, UpdatedAt = DateTimeOffset.UtcNow, Message = "Waiting for human approval (no commit yet)" };
                    await _notifier.NotifyAsync(
                        subject: $"[AgentFlow] Updated diff ready for review — {workItemId}",
                        body   : $"Branch: {branchName}\n\nUpdated diff summary:\n{diff}\n\nApprove in the work item to proceed. To request changes again, comment and re-add tag {regenCodeTag}.",
                        ct);
                    await _workItems.AddCommentAsync(
                        workItemId,
                        $"**AgentFlow – updated diff ready for review**\n\nUpdated diff summary:\n```\n{diff}\n```\n\nApprove: add tag **{_cfgAzure.ApprovalTag?.Trim() ?? "agent:approved"}**\nRequest changes again: add the tag **{regenCodeTag}**\nStop: add tag **{_cfgAzure.StopTag?.Trim() ?? "agent:stop"}**",
                        ct);

                    i = -1;
                    continue;
                }

                // Pushes the changes and opens the PR
                if (outcome == ApprovalOutcome.Approved)
                {
                    _runLog.Step("Final approval", "ApprovalTag received");
                    if (!options.DryRun)
                    {
                        var wiForCommit = await _workItems.GetWorkItemAsync(workItemId, ct);
                        var commitMsg   = BuildConventionalCommitMessage(wiForCommit, workItemId);
                        await _git.CommitAsync(localPath, commitMsg, ct);
                        run = run with { State = RunState.Committed, UpdatedAt = DateTimeOffset.UtcNow, Message = "Committed" };
                        await _git.PushAsync(localPath, ct);
                        run = run with { State = RunState.Pushed, UpdatedAt = DateTimeOffset.UtcNow, Message = "Pushed" };
                    }
                    if (!options.DryRun)
                    {
                        var repoName = !string.IsNullOrWhiteSpace(_cfgAzure.DefaultRepoName)
                            ? _cfgAzure.DefaultRepoName.Trim()
                            : RepoFx.DeriveRepoNameFromUrl(_cfgAzure.DefaultRepoUrl);
                        var target = string.IsNullOrWhiteSpace(baseBranchSpec) ? "main" : baseBranchSpec!;
                        var prTitle = $"[{workItemId}] {branchName}";
                        var prBody = $"Automated PR created by AgentFlow.\n\nWork item: {workItemId}\nBranch: {branchName}\n\nDiff summary:\n{diff}";

                        var prUrl = await _workItems.CreatePullRequestAsync(
                            repoName    : repoName,
                            sourceBranch: branchName,
                            targetBranch: target,
                            title       : prTitle,
                            description : prBody,
                            ct          : ct);

                        run = run with { State = RunState.PullRequestOpened, UpdatedAt = DateTimeOffset.UtcNow, Message = $"Pull request opened: {prUrl}" };

                        // Updates the ticket status and adds the comments
                        var codeReviewState = _cfgAzure.CodeReviewState?.Trim();
                        if (!string.IsNullOrWhiteSpace(codeReviewState))
                            await _workItems.UpdateStateAsync(workItemId, codeReviewState, ct);

                        await _workItems.AddCommentAsync(workItemId, $"**AgentFlow – pull request created**\n\nPR: {prUrl}", ct);
                        await _notifier.NotifyAsync(
                            subject: $"[AgentFlow] PR opened for {workItemId}",
                            body   : $"PR created and work item moved to '{codeReviewState}'.\n\nPR: {prUrl}\nBranch: {branchName}",
                            ct);
                    }

                    _runLog.Step("Workflow", "Done");
                    return run with { State = RunState.Done, UpdatedAt = DateTimeOffset.UtcNow, Message = "Done" };
                }

                await Task.Delay(options.ApprovalPollInterval, ct);
            }

            _runLog.Step("Final approval", "max polls reached without ApprovalTag");
            return run with { State = RunState.WaitingForApproval, UpdatedAt = DateTimeOffset.UtcNow, Message = "Approval not received within polling window" };
        }

        /// <summary>
        /// Performs the standard pause flow: add comment on work item, update state to waiting, and send notification.
        /// The caller must set run state to <see cref="RunState.WaitingForInfo"/> before returning.
        /// </summary>
        /// <param name="workItemId">The work item identifier to comment on and update state for.</param>
        /// <param name="comment">Markdown comment to add to the work item.</param>
        /// <param name="subject">Notification subject line.</param>
        /// <param name="body">Notification body text.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <remarks>
        /// Does not write to <see cref="IWorkflowRunLog"/>; callers record gate outcomes in the run log before or after pausing.
        /// </remarks>
        private async Task PauseAndNotifyAsync(string workItemId, string comment, string subject, string body, CancellationToken ct)
        {
            await _workItems.AddCommentAsync(workItemId, comment, ct);
            await _workItems.UpdateStateAsync(workItemId, _cfgAzure.WaitingState, ct);
            await _notifier.NotifyAsync(subject: subject, body: body, ct);
        }

        /// <summary>
        /// Validates that the work item has the required tag and is assigned to the configured user.
        /// </summary>
        /// <param name="wi">The work item to validate (tags and assignee fields).</param>
        /// <param name="run">Current run state to update if validation fails.</param>
        /// <param name="requiredTag">Required tag from config (e.g. agent:task).</param>
        /// <param name="assignedToUser">Required assignee from config (e.g. ADO display name).</param>
        /// <returns>
        /// A <see cref="WorkflowStepResult"/> with updated run and whether to continue.
        /// </returns>
        private static WorkflowStepResult ValidateTagAndAssignee(WorkItem wi, AgentRun run, string requiredTag, string assignedToUser)
        {
            var tags    = wi.Fields.GetValueOrDefault("System.Tags") ?? string.Empty;
            var tagList = tags.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
            if (!string.IsNullOrWhiteSpace(requiredTag) &&
                !tagList.Any(t => string.Equals(t, requiredTag, StringComparison.OrdinalIgnoreCase)))
            {
                var skipped = run with { State = RunState.Skipped, UpdatedAt = DateTimeOffset.UtcNow, Message = $"Work item does not have required tag '{requiredTag}'. Add the tag to run automation." };
                return new WorkflowStepResult(skipped, false);
            }

            if (!string.IsNullOrWhiteSpace(assignedToUser))
            {
                var assignedToRaw = wi.Fields.GetValueOrDefault("System.AssignedTo")?.Trim() ?? string.Empty;
                var assignedTo    = AzureDevOpsFx.GetDisplayNameOrUniqueName(assignedToRaw);
                if (!string.Equals(assignedTo, assignedToUser, StringComparison.OrdinalIgnoreCase))
                {
                    var skipped = run with { State = RunState.Skipped, UpdatedAt = DateTimeOffset.UtcNow, Message = $"Work item is not assigned to the configured user '{assignedToUser}'. Only that user's tickets are processed." };
                    return new WorkflowStepResult(skipped, false);
                }
            }

            return new WorkflowStepResult(run, true);
        }

        /// <summary>
        /// Validates that the work item has required content for automation (description or Repro Steps by type).
        /// If not clear, pauses with comment and notification.
        /// </summary>
        /// <param name="workItemId">The work item identifier (for comment and notification).</param>
        /// <param name="wi">The work item to validate (description or Repro Steps by type).</param>
        /// <param name="run">Current run state to update if validation fails.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A <see cref="WorkflowStepResult"/> with updated run and whether to continue.
        /// </returns>
        private async Task<WorkflowStepResult> ValidateClarityAsync(string workItemId, WorkItem wi, AgentRun run, CancellationToken ct)
        {
            if (HasRequiredContentForAutomation(wi))
                return new WorkflowStepResult(run, true);

            var runPaused = run with { State = RunState.WaitingForInfo, UpdatedAt = DateTimeOffset.UtcNow, Message = "Requirements unclear; pausing" };
            await PauseAndNotifyAsync(
                workItemId,
                "Automation paused: requirements are unclear.\n\nPlease add:\n- Expected behavior\n- Acceptance criteria\n- Repro steps (for bugs)\n- Repo/module impacted\n",
                $"[AgentFlow] Paused work item {workItemId} (needs info)",
                $"Paused because requirements are unclear.\nTitle: {wi.Title}",
                ct);
            return new WorkflowStepResult(runPaused, false);
        }

        /// <summary>
        /// Resolves repository URL, local path, and branch for the work item.
        /// If not resolved, pauses with comment and notification.
        /// </summary>
        /// <param name="wi">The work item used to resolve repo (e.g. type, title for branch name).</param>
        /// <param name="run">Current run state to update if resolution fails.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A <see cref="WorkflowStepResult"/> with updated run, optional resolution, and whether to continue.
        /// </returns>
        private async Task<WorkflowStepResult> ResolveRepoAsync(WorkItem wi, AgentRun run, CancellationToken ct)
        {
            var resolution = await _repoResolver.ResolveAsync(wi, ct);
            if (resolution.Resolved)
                return new WorkflowStepResult(run, true, resolution);

            var reason    = resolution.FailureReason ?? "Could not determine repository.";
            var runPaused = run with { State = RunState.WaitingForInfo, UpdatedAt = DateTimeOffset.UtcNow, Message = "Repo not resolved; pausing" };
            await PauseAndNotifyAsync(
                run.WorkItemId,
                $"Automation paused: {reason}\n\nPlease set the default repo in config or add repo info to the work item.",
                $"[AgentFlow] Paused work item {run.WorkItemId} (repo not resolved)",
                reason,
                ct);
            return new WorkflowStepResult(runPaused, false);
        }

        /// <summary>
        /// Ensures repo is cloned, verifies BMAD is installed, and creates the branch.
        /// On BMAD missing or base-branch-not-found, pauses with comment and notification.
        /// </summary>
        /// <param name="workItemId">The work item identifier (for comments and notifications on pause).</param>
        /// <param name="options">Workflow options (e.g. DryRun to skip clone/BMAD/branch).</param>
        /// <param name="resolution">Resolved repo URL, local path, branch name, and base branch.</param>
        /// <param name="run">Current run state to update through the step.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A <see cref="WorkflowStepResult"/> with updated run and whether to continue.
        /// </returns>
        private async Task<WorkflowStepResult> PrepareWorkspaceAndBranchAsync(
            string workItemId,
            WorkflowOptions options,
            RepoResolutionResult resolution,
            AgentRun run,
            CancellationToken ct)
        {
            var repoIdOrUrl = resolution.RepoUrl!;
            var localPath   = resolution.LocalPath!;
            var branchName  = resolution.BranchName!;

            run = run with { State = RunState.RepoPrepared, UpdatedAt = DateTimeOffset.UtcNow, Message = "Ensuring repo" };
            if (!options.DryRun)
                await _git.EnsureRepoAsync(repoIdOrUrl, localPath, ct);

            run = run with { State = RunState.BmadVerified, UpdatedAt = DateTimeOffset.UtcNow, Message = "Verifying BMAD" };
            if (!options.DryRun)
            {
                var bmadInstalled = await _bmadChecker.IsBmadInstalledAsync(localPath, ct);
                if (!bmadInstalled)
                {
                    var runPaused = run with { State = RunState.WaitingForInfo, UpdatedAt = DateTimeOffset.UtcNow, Message = "BMAD not installed; pausing" };
                    await PauseAndNotifyAsync(
                        workItemId,
                        "Automation paused: BMAD is required in the repository but not installed.\n\nBMAD is installed **in the repository** (not as an external tool). From the repo root, run:\n\n`npx bmad-method install`\n\nThis creates the `_bmad/` folder and method module. Then re-run the workflow. See the [CIS-Portfolio BMAD wiki](https://dev.azure.com/CIS-Inc/CIS-Portfolio/_wiki/wikis/CIS-Portfolio.wiki/6/bmad) or [BMAD installation docs](https://docs.bmad-method.org/how-to/installation/install-bmad/).",
                        $"[AgentFlow] Paused work item {workItemId} (install BMAD in repo)",
                        "BMAD must be installed in the repository. Run: npx bmad-method install from the repo root, then re-run.",
                        ct);
                    return new WorkflowStepResult(runPaused, false);
                }
            }

            run = run with { State = RunState.BranchCreated, UpdatedAt = DateTimeOffset.UtcNow, Message = "Creating branch" };
            if (!options.DryRun)
            {
                try
                {
                    await _git.CreateBranchAsync(localPath, branchName, resolution.BaseBranch, ct);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Base branch", StringComparison.OrdinalIgnoreCase) && ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    var runPaused = run with { State = RunState.WaitingForInfo, UpdatedAt = DateTimeOffset.UtcNow, Message = "Base branch not found; pausing" };
                    await PauseAndNotifyAsync(
                        workItemId,
                        $"Automation paused: {ex.Message}\n\nPlease create the base branch in the repository or update branch base settings in appsettings.json.",
                        $"[AgentFlow] Paused work item {workItemId} (base branch not found)",
                        ex.Message,
                        ct);
                    return new WorkflowStepResult(runPaused, false);
                }
            }

            return new WorkflowStepResult(run, true);
        }

        /// <summary>
        /// Runs PRD generation (re-fetching work item first) and, when <see cref="AzureDevOpsConfig.RequirePrdApproval"/> is true,
        /// the PRD approval loop (notify, poll for PrdApprovalTag, RegeneratePrdTag, or StopTag). On RegeneratePrd, re-fetches work item and re-runs generation.
        /// </summary>
        /// <param name="workItemId">The work item identifier.</param>
        /// <param name="localPath">Resolved repo path.</param>
        /// <param name="branchName">Branch name for notifications.</param>
        /// <param name="options">Workflow options (DryRun, polling).</param>
        /// <param name="run">Current run state.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A <see cref="WorkflowStepResult"/> with updated run and whether to continue to the next step.
        /// </returns>
        /// <remarks>
        /// After the dry-run early exit, uses <see cref="IWorkflowRunLog"/> for PRD failures, approval timeouts, and configuration messages from polling.
        /// </remarks>
        private async Task<WorkflowStepResult> RunPrdGenerationAndApprovalAsync(
            string workItemId,
            string localPath,
            string branchName,
            WorkflowOptions options,
            AgentRun run,
            CancellationToken ct)
        {
            if (options.DryRun)
            {
                run = run with { State = RunState.DraftGenerated, UpdatedAt = DateTimeOffset.UtcNow, Message = "Dry-run: PRD generation skipped" };
                return new WorkflowStepResult(run, true);
            }

            ArgumentNullException.ThrowIfNull(_runLog);

            // Fetch work item and attach comments
            var wi       = await _workItems.GetWorkItemAsync(workItemId, ct);
            var comments = await _workItems.GetCommentsAsync(workItemId, ct);
            if (comments.Count > 0)
                wi = wi with { Comments = comments };

            // Generate draft PRD
            var draftResult = await _draftGenerator.GeneratePrdAsync(wi, localPath, ct);

            var prdApprovalTag = _cfgAzure.PrdApprovalTag?.Trim() ?? "agent:prdapproved";
            var stopTag        = _cfgAzure.StopTag?.Trim() ?? "agent:stop";
            var regenTag       = _cfgAzure.RegeneratePrdTag?.Trim() ?? "agent:regenerate-prd";

            // Agent has questions for the dev: post them as a comment and enter approval loop; dev answers then adds agent:regenerate-prd.
            if (!string.IsNullOrWhiteSpace(draftResult.QuestionsForDev))
            {
                await _workItems.AddCommentAsync(
                    workItemId,
                    "**AgentFlow – questions from the agent (please answer in a reply below):**\n\n" + draftResult.QuestionsForDev.Trim(),
                    ct);
                await _notifier.NotifyAsync(
                    subject: $"[AgentFlow] Agent has questions — {workItemId}",
                    body   : draftResult.QuestionsForDev.Trim(),
                    ct);
                run = run with { State = RunState.WaitingForPRDApproval, UpdatedAt = DateTimeOffset.UtcNow, Message = "Waiting for dev to answer questions" };
                await _workItems.AddCommentAsync(
                    workItemId,
                    $"Answer the questions above in a reply comment. When done, add the tag **{regenTag}** (same tag for any follow-up; we remove it after each run so you can add it again to trigger the next run). Or add **{stopTag}** to stop.",
                    ct);
                await _notifier.NotifyAsync(
                    subject: $"[AgentFlow] Waiting for your answers — {workItemId}",
                    body   : $"Reply to the questions on the work item, then add tag {regenTag} to re-run. Or {stopTag} to stop.",
                    ct);

                // Fall through to the approval loop below (same polling; agent:regenerate-prd will re-run PRD with comments included).
            }
            else if (!draftResult.Success)
            {
                _runLog.Error(draftResult.Message ?? "PRD generation failed");
                await _notifier.NotifyAsync(
                    subject: $"[AgentFlow] PRD generation failed for {workItemId}",
                    body   : draftResult.Message ?? string.Empty,
                    ct);
                return new WorkflowStepResult(run with { State = RunState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Message = "PRD generation failed" }, false);
            }
            else
            {
                run = run with { State = RunState.DraftGenerated, UpdatedAt = DateTimeOffset.UtcNow, Message = "PRD generated", PrdPath = draftResult.PrdPath };
            }

            // Optional PRD approval loop (also when waiting for dev to answer questions)
            if (!_cfgAzure.RequirePrdApproval)
                return new WorkflowStepResult(run, true);

            if (run.State == RunState.DraftGenerated)
            {
                run = run with { State = RunState.WaitingForPRDApproval, UpdatedAt = DateTimeOffset.UtcNow, Message = "Waiting for PRD approval" };
                await _workItems.AddCommentAsync(
                    workItemId,
                    $"**PRD ready for review.**\n\nAdd tag **{prdApprovalTag}** to continue, **{regenTag}** to re-run PRD with latest ticket data, or **{stopTag}** to stop (no commit).",
                    ct);
                await _notifier.NotifyAsync(
                    subject: $"[AgentFlow] PRD ready for review — {workItemId}",
                    body   : $"Branch: {branchName}\n\nAdd tag {prdApprovalTag} to continue, {regenTag} to regenerate PRD, or {stopTag} to stop.",
                    ct);
            }

            // Poll for approval, regenerate, or stop
            for (int i = 0; i < options.ApprovalMaxPolls; i++)
            {
                ct.ThrowIfCancellationRequested();
                var outcome = await _workItems.GetWorkItemSignalAsync(
                    workItemId,
                    ct,
                    continueApprovalTag: prdApprovalTag,
                    regeneratePrdTag: regenTag);
                if (outcome == ApprovalOutcome.Stopped)
                {
                    _runLog.Step("PRD approval poll", "Stopped by user (StopTag)");
                    return new WorkflowStepResult(run with { State = RunState.StoppedByUser, UpdatedAt = DateTimeOffset.UtcNow, Message = "Stopped by user (agent:stop)" }, false);
                }
                if (outcome == ApprovalOutcome.RegeneratePrd)
                {
                    wi                = await _workItems.GetWorkItemAsync(workItemId, ct);
                    var regenComments = await _workItems.GetCommentsAsync(workItemId, ct);
                    if (regenComments.Count > 0)
                        wi = wi with { Comments = regenComments };

                    var regenResult = await _draftGenerator.GeneratePrdAsync(wi, localPath, ct);
                    if (!regenResult.Success)
                    {
                        if (!string.IsNullOrWhiteSpace(regenResult.QuestionsForDev))
                        {
                            await _workItems.AddCommentAsync(
                                workItemId,
                                "**AgentFlow – follow-up questions from the agent:**\n\n" + regenResult.QuestionsForDev.Trim(),
                                ct);
                            await _workItems.AddCommentAsync(
                                workItemId,
                                $"Reply with your answers above, then add the same tag **{regenTag}** again to re-run (we remove it after each run so tag count does not grow).",
                                ct);
                            await _notifier.NotifyAsync(
                                subject: $"[AgentFlow] Agent has follow-up questions — {workItemId}",
                                body   : regenResult.QuestionsForDev.Trim(),
                                ct);
                        }
                        else
                        {
                            await _workItems.AddCommentAsync(workItemId, $"PRD regeneration failed: {regenResult.Message}", ct);
                            await _notifier.NotifyAsync(
                                subject: $"[AgentFlow] PRD regeneration failed — {workItemId}",
                                body   : regenResult.Message,
                                ct);
                        }
                    }
                    else
                    {
                        run = run with { PrdPath = regenResult.PrdPath };
                        await _workItems.AddCommentAsync(
                            workItemId,
                            $"**PRD regenerated** with latest ticket data. Review and add **{prdApprovalTag}** to continue or **{stopTag}** to stop.",
                            ct);
                    }

                    // Remove tag so the next poll does not trigger RegeneratePrd again; dev adds the same tag
                    // again to trigger another run.
                    await _workItems.RemoveTagAsync(workItemId, regenTag, ct);
                    continue;
                }
                if (outcome == ApprovalOutcome.Approved)
                    return new WorkflowStepResult(run, true);
                await Task.Delay(options.ApprovalPollInterval, ct);
                if (i == options.ApprovalMaxPolls - 1)
                {
                    _runLog.Configuration("PRD approval not received within polling window (max polls reached).");
                    return new WorkflowStepResult(run with { State = RunState.WaitingForPRDApproval, UpdatedAt = DateTimeOffset.UtcNow, Message = "PRD approval not received within polling window" }, false);
                }
            }

            return new WorkflowStepResult(run, true);
        }

        /// <summary>
        /// Runs code generation from the approved PRD (run-dev script). PRD path is obtained from the LLM output (AGENTFLOW_PRD_PATH=) during PRD generation; stored on <paramref name="run"/>.
        /// On success sets state to <see cref="RunState.ImplementationGenerated"/>. On agent questions, posts comment and pauses; on failure, notifies and returns failed.
        /// </summary>
        /// <param name="workItemId">The work item identifier (comments, notifications, and re-fetch for code gen).</param>
        /// <param name="localPath">Absolute path to the cloned repository root.</param>
        /// <param name="options">Workflow options (dry-run skips code gen; approval polling for regenerate-code loop).</param>
        /// <param name="run">Current run; must include <see cref="AgentRun.PrdPath"/> from PRD generation output.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A <see cref="WorkflowStepResult"/> with updated run and whether the workflow should continue.
        /// </returns>
        /// <remarks>
        /// After the dry-run early exit, uses <see cref="IWorkflowRunLog"/> for missing PRD path, file-not-found, failures, and question-poll timeouts.
        /// </remarks>
        private async Task<WorkflowStepResult> RunCodeGenerationAsync(
            string workItemId,
            string localPath,
            WorkflowOptions options,
            AgentRun run,
            CancellationToken ct)
        {
            if (options.DryRun)
            {
                run = run with { State = RunState.ImplementationGenerated, UpdatedAt = DateTimeOffset.UtcNow, Message = "Dry-run: code generation skipped" };
                return new WorkflowStepResult(run, true);
            }

            ArgumentNullException.ThrowIfNull(_runLog);

            if (string.IsNullOrWhiteSpace(run.PrdPath))
            {
                _runLog.Configuration("Code generation requires AGENTFLOW_PRD_PATH from the PRD step; none was provided.");
                await _notifier.NotifyAsync(
                    subject: $"[AgentFlow] Code generation skipped — no PRD path for {workItemId}",
                    body   : "The PRD generation step must output a line: AGENTFLOW_PRD_PATH=<path> (path to the generated PRD, e.g. _bmad-output/implementation-artifacts/...). AgentFlow uses this to run code generation.",
                    ct);
                return new WorkflowStepResult(run with { State = RunState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Message = "PRD path not provided by PRD generation (output AGENTFLOW_PRD_PATH=)" }, false);
            }

            var prdPath = Path.IsPathRooted(run.PrdPath) ? run.PrdPath : Path.Combine(localPath, run.PrdPath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!File.Exists(prdPath))
            {
                _runLog.Error($"PRD file not found for code generation: {prdPath}");
                await _notifier.NotifyAsync(
                    subject: $"[AgentFlow] Code generation skipped — PRD file not found for {workItemId}",
                    body   : $"PRD file not found: {prdPath}. The path was reported by the PRD generation step (AGENTFLOW_PRD_PATH).",
                    ct);
                return new WorkflowStepResult(run with { State = RunState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Message = "PRD file not found for code generation" }, false);
            }

            // Fetch work item and attach comments
            var wi       = await _workItems.GetWorkItemAsync(workItemId, ct);
            var comments = await _workItems.GetCommentsAsync(workItemId, ct);
            if (comments.Count > 0)
                wi = wi with { Comments = comments };

            var regenCodeTag = _cfgAzure.RegenerateCodeTag?.Trim() ?? "agent:regenerate-code";
            var stopTag      = _cfgAzure.StopTag?.Trim() ?? "agent:stop";

            // Loop: generate, and if agent has questions, post them and poll for dev to add agent:regenerate-code (re-run) or agent:stop
            CodeGeneratorResult codeResult;
            while (true)
            {
                // Code generation process
                codeResult = await _codeGenerator.GenerateAsync(wi, localPath, prdPath, ct);
                if (!string.IsNullOrWhiteSpace(codeResult.QuestionsForDev))
                {
                    await _workItems.AddCommentAsync(
                        workItemId,
                        "**AgentFlow – code generation: questions from the agent (please answer in a reply below):**\n\n" + codeResult.QuestionsForDev.Trim(),
                        ct);
                    await _workItems.AddCommentAsync(
                        workItemId,
                        $"Reply to the questions above, then add the tag **{regenCodeTag}** to re-run code generation (we remove it after each run so you can add it again). Or add **{stopTag}** to stop.",
                        ct);
                    await _notifier.NotifyAsync(
                        subject: $"[AgentFlow] Agent has questions (code gen) — {workItemId}",
                        body   : $"Reply on the work item, then add tag {regenCodeTag} to re-run. Or {stopTag} to stop.",
                        ct);
                    run = run with { State = RunState.WaitingForInfo, UpdatedAt = DateTimeOffset.UtcNow, Message = "Waiting for dev to answer code-gen questions" };

                    // Poll for regenerate-code or stop
                    for (int i = 0; i < options.ApprovalMaxPolls; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var outcome = await _workItems.GetWorkItemSignalAsync(
                            workItemId,
                            ct,
                            regenerateCodeTag: regenCodeTag,
                            regenerateCodeExclusive: true);
                        if (outcome == ApprovalOutcome.Stopped)
                        {
                            _runLog.Step("Code-gen question poll", "Stopped by user (StopTag)");
                            return new WorkflowStepResult(run with { State = RunState.StoppedByUser, UpdatedAt = DateTimeOffset.UtcNow, Message = "Stopped by user (agent:stop)" }, false);
                        }
                        if (outcome == ApprovalOutcome.RegenerateCode)
                        {
                            wi = await _workItems.GetWorkItemAsync(workItemId, ct);
                            var regenComments = await _workItems.GetCommentsAsync(workItemId, ct);
                            if (regenComments.Count > 0)
                                wi = wi with { Comments = regenComments };
                            await _workItems.RemoveTagAsync(workItemId, regenCodeTag, ct);
                            break; // re-run code gen (next iteration of while)
                        }
                        await Task.Delay(options.ApprovalPollInterval, ct);
                        if (i == options.ApprovalMaxPolls - 1)
                        {
                            _runLog.Configuration("Code-gen reply not received within polling window (max polls reached).");
                            return new WorkflowStepResult(run with { State = RunState.WaitingForInfo, UpdatedAt = DateTimeOffset.UtcNow, Message = "Code-gen reply not received within polling window" }, false);
                        }
                    }
                    continue;
                }

                if (!codeResult.Success)
                {
                    _runLog.Error(codeResult.Message ?? "Code generation failed");
                    await _notifier.NotifyAsync(
                        subject: $"[AgentFlow] Code generation failed for {workItemId}",
                        body   : codeResult.Message ?? string.Empty,
                        ct);
                    return new WorkflowStepResult(run with { State = RunState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Message = "Code generation failed" }, false);
                }

                run = run with { State = RunState.ImplementationGenerated, UpdatedAt = DateTimeOffset.UtcNow, Message = "Code generated" };
                return new WorkflowStepResult(run, true);
            }
        }

        /// <summary>
        /// Builds a Conventional Commit message that includes a scope and an ADO reference.
        /// </summary>
        /// <param name="wi">Work item (type/title) used to derive commit type and description.</param>
        /// <param name="workItemId">ADO work item ID to include as <c>ADO #&lt;id&gt;</c>.</param>
        /// <returns>
        /// Commit message (single line).
        /// </returns>
        private string BuildConventionalCommitMessage(WorkItem wi, string workItemId)
        {
            // Work item type maps to commit type: Bug → fix, Feature/Product Backlog Item → feat, otherwise chore
            var type       = (wi.Fields?.GetValueOrDefault("System.WorkItemType") ?? string.Empty).Trim();
            var commitType = string.Equals(type, "Bug", StringComparison.OrdinalIgnoreCase) ? "fix"
                : (string.Equals(type, "Feature", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Product Backlog Item", StringComparison.OrdinalIgnoreCase))
                    ? "feat"
                    : "chore";

            // Repo name or scope for the commit: prefer config value, then derive from repo URL, then fallback to "work"
                        var repoName = !string.IsNullOrWhiteSpace(_cfgAzure.DefaultRepoName)
                            ? _cfgAzure.DefaultRepoName.Trim()
                            : RepoFx.DeriveRepoNameFromUrl(_cfgAzure.DefaultRepoUrl);

            var scope = !string.IsNullOrWhiteSpace(_workspace.CommitScope)
                ? _workspace.CommitScope.Trim()
                : repoName;

            scope = TextFx.SlugifyForCommitScope(scope);

            var title = (wi.Title ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title))
                title = $"work item {workItemId}";

            var shortTitle = TextFx.TruncateSingleLine(title, 60);

            return $"{commitType}({scope}): {shortTitle} ADO #{workItemId}";
        }

        /// <summary>
        /// Determines whether the work item has the minimum required content for automation based on its type.
        /// Non-bug types (Task, User Story, etc.) require a non-empty description. Bugs have no description field 
        /// and require only non-empty Repro Steps.
        /// </summary>
        /// <param name="wi">The work item to evaluate.</param>
        /// <returns>
        /// <see langword="true"/> if the work item has the required content for its type (description for non-bugs, Repro Steps for bugs); otherwise, <see langword="false"/>.
        /// </returns>
        private static bool HasRequiredContentForAutomation(WorkItem wi)
        {
            // Work item type
            var workItemType = wi.Fields?.GetValueOrDefault("System.WorkItemType")?.Trim() ?? string.Empty;

            if (string.Equals(workItemType, "Bug", StringComparison.OrdinalIgnoreCase))
            {
                // Bugs have no description; require Repro Steps only (Azure DevOps: Microsoft.VSTS.TCM.ReproSteps)
                var reproSteps = wi.Fields?.GetValueOrDefault("Microsoft.VSTS.TCM.ReproSteps") ?? string.Empty;
                return !string.IsNullOrWhiteSpace(reproSteps);
            }

            // Non-bug: require non-empty description
            var desc = wi.Description ?? string.Empty;
            return !string.IsNullOrWhiteSpace(desc);
        }
    }
}