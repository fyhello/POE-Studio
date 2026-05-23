using System.Text.Json;

namespace PoeStudio.Mcp;

public sealed record PoeWorkspaceResolution(bool Success, string? WorkspaceRoot, string Source, string? Error);

public sealed class PoeWorkspaceResolver
{
    public PoeWorkspaceResolution Resolve(string[] args, IReadOnlyDictionary<string, string?> environment)
    {
        var argumentRoot = GetArgumentWorkspaceRoot(args);
        if (!string.IsNullOrWhiteSpace(argumentRoot))
        {
            return Success(argumentRoot, "argument");
        }

        if (environment.TryGetValue("POE_STUDIO_WORKSPACE_ROOT", out var envRoot)
            && !string.IsNullOrWhiteSpace(envRoot))
        {
            return Success(envRoot, "environment");
        }

        var settingsPath = GetSettingsPath(environment);
        if (!string.IsNullOrWhiteSpace(settingsPath) && File.Exists(settingsPath))
        {
            var settingsRoot = ReadSettingsWorkspaceRoot(settingsPath);
            if (!string.IsNullOrWhiteSpace(settingsRoot))
            {
                return Success(settingsRoot, "local-settings");
            }
        }

        return new PoeWorkspaceResolution(
            false,
            null,
            "unresolved",
            "POE Studio workspace root is not configured. Start with --workspace-root <path>, set POE_STUDIO_WORKSPACE_ROOT, or write %LOCALAPPDATA%\\PoeStudio\\workspace-settings.json with { \"workspaceRoot\": \"<path>\" }.");
    }

    private static PoeWorkspaceResolution Success(string root, string source)
    {
        return new PoeWorkspaceResolution(true, Path.GetFullPath(root), source, null);
    }

    private static string? GetArgumentWorkspaceRoot(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--workspace-root", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length)
            {
                return args[index + 1];
            }

            const string prefix = "--workspace-root=";
            if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return args[index][prefix.Length..];
            }
        }

        return null;
    }

    private static string? GetSettingsPath(IReadOnlyDictionary<string, string?> environment)
    {
        if (!environment.TryGetValue("LOCALAPPDATA", out var localAppData)
            || string.IsNullOrWhiteSpace(localAppData))
        {
            return null;
        }

        return Path.Combine(localAppData, "PoeStudio", "workspace-settings.json");
    }

    private static string? ReadSettingsWorkspaceRoot(string settingsPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("workspaceRoot", out var root)
                && root.ValueKind == JsonValueKind.String)
            {
                return root.GetString();
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }
}
