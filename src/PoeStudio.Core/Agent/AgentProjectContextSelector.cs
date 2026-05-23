namespace PoeStudio.Core.Agent;

public static class AgentProjectContextSelector
{
    public static IReadOnlyList<string> SelectKeys(string taskKind, string goal, string? resourcePath)
    {
        var text = $"{taskKind} {goal} {resourcePath}".ToLowerInvariant();
        var keys = new List<string> { "overview", "workflow", "risk" };

        if (ContainsAny(text, ".datc64", "datc64", "表", "翻译", "草稿", "当前"))
        {
            Add(keys, "layering");
            Add(keys, "datc64");
            Add(keys, "mcp");
            Add(keys, "approval");
        }

        if (ContainsAny(text, "构建", "补丁", "安装", "回滚", "patch"))
        {
            Add(keys, "patch");
            Add(keys, "overlay");
            Add(keys, "approval");
        }

        if (ContainsAny(text, "native", "ggpk", "oodle", "bundles2"))
        {
            Add(keys, "native");
        }

        if (ContainsAny(text, "找", "搜索", "资源", "索引", "search", "index"))
        {
            Add(keys, "index");
            Add(keys, "resource");
        }

        return keys;
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(text.Contains);
    }

    private static void Add(List<string> keys, string key)
    {
        if (!keys.Contains(key))
        {
            keys.Add(key);
        }
    }
}
