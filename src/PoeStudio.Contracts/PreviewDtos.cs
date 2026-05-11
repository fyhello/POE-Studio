namespace PoeStudio.Contracts;

public enum PreviewKind
{
    Unavailable = 0,
    Text = 1,
    Hex = 2,
    Image = 3,
    Audio = 4,
    Font = 5
}

public sealed record ResourcePreviewRequest(
    string ProfileId,
    string VirtualPath,
    int Limit = 65536,
    string? OodlePath = null);

public sealed record ResourcePreviewResponse(
    string ProfileId,
    string VirtualPath,
    PreviewKind Kind,
    string? Language,
    string? Text,
    string? Hex,
    string? MediaType,
    string? Base64Content,
    bool Truncated,
    string? ErrorCode,
    string? Message);
