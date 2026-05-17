using System.Text.Json;

namespace PoeStudio.Api;

public sealed class WorkspaceRootProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string settingsPath;
    private readonly object gate = new();
    private string currentRoot;

    public WorkspaceRootProvider(IConfiguration config)
    {
        var configuredRoot = config["PoeStudio:WorkspaceRoot"];
        var defaultRoot = configuredRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeStudio");
        settingsPath = config["PoeStudio:WorkspaceSettingsPath"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeStudio", "workspace-settings.json");
        currentRoot = Normalize(configuredRoot ?? ReadSavedRoot() ?? defaultRoot);
    }

    public string CurrentRoot
    {
        get
        {
            lock (gate)
            {
                return currentRoot;
            }
        }
    }

    public string SetRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("工作区目录不能为空。", nameof(workspaceRoot));
        }

        var normalized = Normalize(workspaceRoot);
        Directory.CreateDirectory(normalized);
        lock (gate)
        {
            currentRoot = normalized;
            SaveRoot(normalized);
            return currentRoot;
        }
    }

    private string? ReadSavedRoot()
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(settingsPath);
            return JsonSerializer.Deserialize<WorkspaceSettingsFile>(stream, JsonOptions)?.WorkspaceRoot;
        }
        catch
        {
            return null;
        }
    }

    private void SaveRoot(string workspaceRoot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var tempPath = $"{settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, new WorkspaceSettingsFile(workspaceRoot), JsonOptions);
            }

            File.Move(tempPath, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private sealed record WorkspaceSettingsFile(string WorkspaceRoot);
}
