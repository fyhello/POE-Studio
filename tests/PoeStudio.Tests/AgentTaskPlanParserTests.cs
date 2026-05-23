using PoeStudio.Contracts;
using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class AgentTaskPlanParserTests
{
    [Fact]
    public void Parse_extracts_ready_plan_from_fenced_json()
    {
        var parser = new AgentTaskPlanParser();
        var message = """
        ```json
        {
          "status": "ready",
          "requestedTaskKind": "auto",
          "resolvedTaskKind": "datc64-translation",
          "profileId": "profile-target",
          "resourcePath": "data/balance/traditional chinese/activeskills.datc64",
          "summary": "Translate the selected DATC64 table.",
          "userConstraints": ["only translate cells different from simplified source"],
          "steps": [
            {
              "order": 1,
              "title": "Extract target DATC64 cells",
              "reason": "Need current editable state.",
              "suggestedTools": ["poe_datc64_extract_translatable_cells"]
            }
          ],
          "requiredApprovals": ["overlay_draft"],
          "warnings": [],
          "questions": [],
          "missingCapability": null
        }
        ```
        """;

        var plan = parser.Parse(message);

        Assert.Equal(AgentTaskPlanStatus.Ready, plan.Status);
        Assert.Equal("datc64-translation", plan.ResolvedTaskKind);
        Assert.Equal("profile-target", plan.ProfileId);
        Assert.Equal("data/balance/traditional chinese/activeskills.datc64", plan.ResourcePath);
        Assert.Contains(plan.UserConstraints, x => x.Contains("simplified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_extracts_clarification_questions()
    {
        var parser = new AgentTaskPlanParser();
        var message = """
        ```json
        {
          "status": "needs_clarification",
          "requestedTaskKind": "auto",
          "resolvedTaskKind": null,
          "profileId": "profile-1",
          "resourcePath": null,
          "summary": "The user asked to translate a table but no resource is known.",
          "userConstraints": [],
          "steps": [],
          "requiredApprovals": [],
          "warnings": [],
          "questions": ["请告诉我要翻译哪个资源路径，或先在资源列表中选中它。"],
          "missingCapability": null
        }
        ```
        """;

        var plan = parser.Parse(message);

        Assert.Equal(AgentTaskPlanStatus.NeedsClarification, plan.Status);
        Assert.Null(plan.ResolvedTaskKind);
        Assert.Single(plan.Questions);
    }

    [Fact]
    public void Task_kind_policy_allows_auto_request_but_not_auto_execution()
    {
        Assert.True(AgentTaskKindPolicy.IsSupportedRequestTaskKind("auto"));
        Assert.False(AgentTaskKindPolicy.IsExecutableTaskKind("auto"));
        Assert.True(AgentTaskKindPolicy.IsExecutableTaskKind("question"));
    }
}
