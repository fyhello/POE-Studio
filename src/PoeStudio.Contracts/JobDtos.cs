namespace PoeStudio.Contracts;

public enum JobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

public sealed record JobSnapshotDto(
    string Id,
    string Kind,
    JobStatus Status,
    int ProgressPercent,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ErrorCode,
    string? ErrorMessage,
    string? ResultJson);
