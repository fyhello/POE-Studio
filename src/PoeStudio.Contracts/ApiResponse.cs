namespace PoeStudio.Contracts;

public sealed record ApiResponse<T>(bool Ok, T? Data, string? ErrorCode, string? Message)
{
    public static ApiResponse<T> Success(T data) => new(true, data, null, null);

    public static ApiResponse<T> Failure(string errorCode, string message) =>
        new(false, default, errorCode, message);
}

public sealed record AppDiagnosticsDto(
    string Status,
    string WorkspaceRoot,
    bool WorkspaceWritable,
    int ProfileCount,
    DateTimeOffset CheckedAt,
    IReadOnlyList<string> Warnings);

public sealed record WorkspaceSettingsDto(
    string WorkspaceRoot,
    bool WorkspaceWritable,
    IReadOnlyList<string> Warnings);

public sealed record WorkspaceSettingsUpdateRequest(string WorkspaceRoot);
