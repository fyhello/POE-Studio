namespace PoeStudio.Contracts;

public sealed record ApiResponse<T>(bool Ok, T? Data, string? ErrorCode, string? Message)
{
    public static ApiResponse<T> Success(T data) => new(true, data, null, null);

    public static ApiResponse<T> Failure(string errorCode, string message) =>
        new(false, default, errorCode, message);
}
