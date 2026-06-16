namespace PlanCope.Shared.Domain;

public enum BlockType
{
    Text,
    Image,
    MultipleChoice,
    TrueFalse,
    ShortAnswer
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
