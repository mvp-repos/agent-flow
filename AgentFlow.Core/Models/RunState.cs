namespace AgentFlow.Core.Models
{
    /// <summary>
    /// Represents the various states that a run can be in during its lifecycle. This enum is used to 
    /// track the progress of a run through different stages, from intake to completion or failure.
    /// </summary>
    public enum RunState
    {
        Intake                    = 0,
        ClarityCheck              = 10,
        WaitingForInfo            = 20,

        RepoSelection             = 30,
        RepoPrepared              = 40,
        BmadVerified              = 45,     // BMAD (quick-spec) verified after clone, before branch
        BranchCreated             = 50,

        DraftGenerated            = 60,     // PRD generated; working tree may have PRD artifacts only (NO COMMIT)
        WaitingForPRDApproval     = 65,     // PRD ready; waiting for user tag (agent:prdapproved or agent:stop) when RequirePrdApproval is true
        ImplementationGenerated   = 67,     // Code generated from PRD (run-dev); working tree has code + tests (NO COMMIT). After PRD approval, before checks.
        TestsRun                  = 68,     // dotnet test (or equivalent) executed when a test project/solution exists; before general ICheckRunner checks.
        ChecksRun                 = 70,
        WaitingForApproval        = 80,

        Committed                 = 90,
        Pushed                    = 100,
        PullRequestOpened         = 110,

        Done                      = 200,
        Skipped                   = 800,    // Run refused: work item missing required tag or not assigned to configured user
        StoppedByUser             = 850,    // User added agent:stop; workflow stopped without commit
        Failed                    = 900
    }
}
