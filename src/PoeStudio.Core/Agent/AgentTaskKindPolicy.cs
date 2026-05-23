namespace PoeStudio.Core.Agent;

public static class AgentTaskKindPolicy
{
    public const string Auto = "auto";

    public static bool IsAuto(string taskKind)
    {
        return string.Equals(taskKind, Auto, StringComparison.Ordinal);
    }

    public static bool IsSupportedRequestTaskKind(string taskKind)
    {
        return IsAuto(taskKind)
            || AgentCapabilities.All.Any(x => string.Equals(x.TaskKind, taskKind, StringComparison.Ordinal));
    }

    public static bool IsExecutableTaskKind(string taskKind)
    {
        return AgentCapabilities.All.Any(x => string.Equals(x.TaskKind, taskKind, StringComparison.Ordinal));
    }
}
