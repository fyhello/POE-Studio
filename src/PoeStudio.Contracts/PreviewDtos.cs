namespace PoeStudio.Contracts;

public enum PreviewKind
{
    Unavailable = 0,
    Text = 1,
    Hex = 2
}

public sealed record ResourcePreviewRequest(
    string ProfileId,
    string VirtualPath,
    int Limit = 65536);

public sealed record ResourcePreviewResponse(
    string ProfileId,
    string VirtualPath,
    PreviewKind Kind,
    string? Language,
    string? Text,
    string? Hex,
    bool Truncated,
    string? ErrorCode,
    string? Message);
