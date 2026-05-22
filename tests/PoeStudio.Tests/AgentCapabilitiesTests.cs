using PoeStudio.Contracts;
using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class AgentCapabilitiesTests
{
    [Fact]
    public void Registry_contains_question_as_read_only_capability()
    {
        var capability = AgentCapabilities.GetRequired("question");

        Assert.Equal(AgentCapabilityKind.ReadOnly, capability.Kind);
        Assert.False(capability.RequiresApproval);
    }

    [Fact]
    public void Registry_contains_read_only_analysis_with_mcp_read_tools()
    {
        var capability = AgentCapabilities.GetRequired("read-only-analysis");

        Assert.Equal(AgentCapabilityKind.ReadOnly, capability.Kind);
        Assert.False(capability.RequiresApproval);
        Assert.Contains(capability.RequiredMcpTools, tool => tool.StartsWith("poe_", StringComparison.Ordinal));
    }

    [Fact]
    public void Registry_contains_datc64_translation_as_approval_gated_capability()
    {
        var capability = AgentCapabilities.GetRequired("datc64-translation");

        Assert.Equal(AgentCapabilityKind.WriteWithApproval, capability.Kind);
        Assert.True(capability.RequiresApproval);
        Assert.Contains("poe_datc64_extract_translatable_cells", capability.RequiredMcpTools);
    }
}
