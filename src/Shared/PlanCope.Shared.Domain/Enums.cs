namespace PlanCope.Shared.Domain;

public enum BlockType
{
    // Preserve database enum integers used by existing choice blocks.
    MultipleChoice = 2,
    TrueFalse = 3
}

public enum ExamStatus
{
    Draft,
    Review,
    Approved,
    Published,
    Archived
}

public enum UserRole
{
    Admin,
    Operator,
    Viewer
}

public enum TargetType
{
    School,
    Department,
    Province,
    Global
}

public enum SessionStatus
{
    Active,
    Paused,
    Closed
}

public enum SubmissionStatus
{
    InProgress,
    Submitted,
    Received,
    Rejected
}

public enum SyncDirection
{
    Inbound,
    Outbound
}

public enum SyncStatus
{
    Pending,
    Sent,
    Failed,
    Succeeded
}
