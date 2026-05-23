using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class AgentProjectContextSelectorTests
{
    [Fact]
    public void SelectKeys_for_datc64_current_translation_includes_layering_datc64_mcp_and_approval()
    {
        var keys = AgentProjectContextSelector.SelectKeys(
            "datc64-translation",
            "继续翻译当前表",
            "data/balance/traditional chinese/activeskills.datc64");

        Assert.Contains("overview", keys);
        Assert.Contains("workflow", keys);
        Assert.Contains("risk", keys);
        Assert.Contains("layering", keys);
        Assert.Contains("datc64", keys);
        Assert.Contains("mcp", keys);
        Assert.Contains("approval", keys);
    }

    [Fact]
    public void SelectKeys_for_patch_native_oodle_tasks_includes_patch_native_and_approval()
    {
        var keys = AgentProjectContextSelector.SelectKeys(
            "read-only-analysis",
            "构建补丁并检查安装回滚 Oodle Bundles2 Native",
            null);

        Assert.Contains("patch", keys);
        Assert.Contains("native", keys);
        Assert.Contains("approval", keys);
    }

    [Fact]
    public void SelectKeys_for_resource_search_and_index_tasks_includes_index_and_resource()
    {
        var keys = AgentProjectContextSelector.SelectKeys("question", "帮我搜索资源并检查索引", null);

        Assert.Contains("index", keys);
        Assert.Contains("resource", keys);
    }

    [Fact]
    public void SelectKeys_always_includes_default_overview_workflow_and_risk()
    {
        var keys = AgentProjectContextSelector.SelectKeys("question", "hello", null);

        Assert.Equal("overview", keys[0]);
        Assert.Contains("workflow", keys);
        Assert.Contains("risk", keys);
    }
}
