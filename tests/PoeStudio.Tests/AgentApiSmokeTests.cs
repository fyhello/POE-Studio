using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class AgentApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AgentApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PoeStudio:WorkspaceRoot", Path.Combine(Path.GetTempPath(), "poe-studio-agent-api-tests", Guid.NewGuid().ToString("N")));
            builder.UseSetting("PoeStudio:WorkspaceSettingsPath", Path.Combine(Path.GetTempPath(), "poe-studio-agent-api-tests", Guid.NewGuid().ToString("N"), "workspace-settings.json"));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICodexProcessRunner>();
                services.AddScoped<ICodexProcessRunner>(_ => new FakeRunner());
            });
        });
    }

    [Fact]
    public async Task Agent_settings_roundtrip()
    {
        var client = _factory.CreateClient();

        var initial = await client.GetFromJsonAsync<ApiResponse<AgentSettingsDto>>("/api/agent/settings");
        var save = await client.PostAsJsonAsync("/api/agent/settings", initial!.Data! with { Model = "gpt-5.4", Sandbox = "read-only" });
        var saved = await client.GetFromJsonAsync<ApiResponse<AgentSettingsDto>>("/api/agent/settings");

        Assert.True(initial.Ok);
        Assert.Equal("poe-studio", initial.Data!.McpServerName);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal("gpt-5.4", saved!.Data!.Model);
        Assert.Equal("read-only", saved.Data.Sandbox);
    }

    [Fact]
    public async Task Agent_thread_message_and_snapshot_roundtrip()
    {
        var client = _factory.CreateClient();
        var thread = await CreateThreadAsync(client, "question");

        var messageResponse = await client.PostAsJsonAsync($"/api/agent/threads/{thread.Id}/messages", new AgentMessageCreateRequest("Hello", null));
        var snapshot = await client.GetFromJsonAsync<ApiResponse<AgentThreadSnapshotDto>>($"/api/agent/threads/{thread.Id}");

        Assert.Equal(HttpStatusCode.OK, messageResponse.StatusCode);
        Assert.True(snapshot!.Ok);
        Assert.Equal(thread.Id, snapshot.Data!.Thread.Id);
        Assert.Contains(snapshot.Data.Messages, x => x.Content == "Hello");
    }

    [Theory]
    [InlineData("question")]
    [InlineData("read-only-analysis")]
    [InlineData("datc64-translation")]
    public async Task Agent_runs_support_all_stage2_task_kinds(string taskKind)
    {
        var client = _factory.CreateClient();
        var thread = await CreateThreadAsync(client, taskKind);

        var response = await client.PostAsJsonAsync("/api/agent/runs", new AgentRunCreateRequest(
            thread.Id,
            thread.ProfileId,
            "Goal",
            taskKind,
            taskKind == "datc64-translation" ? "metadata/example.datc64" : null));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AgentRunDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload!.Ok);
        Assert.Equal(taskKind == "datc64-translation" ? AgentRunStatus.WaitingForApproval : AgentRunStatus.Succeeded, payload.Data!.Status);
    }

    [Fact]
    public async Task Agent_retry_creates_new_run_and_keeps_old_events()
    {
        var client = _factory.CreateClient();
        var thread = await CreateThreadAsync(client, "question");
        var run = await CreateRunAsync(client, thread, "question", null);

        var retryResponse = await client.PostAsync($"/api/agent/runs/{run.Id}/retry", null);
        var retry = await retryResponse.Content.ReadFromJsonAsync<ApiResponse<AgentRunDto>>();
        var oldEvents = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<AgentEventDto>>>($"/api/agent/runs/{run.Id}/events");

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.NotEqual(run.Id, retry!.Data!.Id);
        Assert.NotEmpty(oldEvents!.Data!);
    }

    [Fact]
    public async Task Agent_run_events_and_missing_approval_are_structured()
    {
        var client = _factory.CreateClient();
        var thread = await CreateThreadAsync(client, "question");
        var run = await CreateRunAsync(client, thread, "question", null);

        var events = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<AgentEventDto>>>($"/api/agent/runs/{run.Id}/events");
        var missingApproval = await client.PostAsync("/api/agent/approvals/missing/approve", null);

        Assert.True(events!.Ok);
        Assert.NotEmpty(events.Data!);
        Assert.Equal(HttpStatusCode.NotFound, missingApproval.StatusCode);
    }

    private static async Task<AgentThreadDto> CreateThreadAsync(HttpClient client, string taskKind)
    {
        var response = await client.PostAsJsonAsync("/api/agent/threads", new AgentThreadCreateRequest("profile-1", "Thread", "Goal", taskKind));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AgentThreadDto>>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return payload!.Data!;
    }

    private static async Task<AgentRunDto> CreateRunAsync(HttpClient client, AgentThreadDto thread, string taskKind, string? resourcePath)
    {
        var response = await client.PostAsJsonAsync("/api/agent/runs", new AgentRunCreateRequest(thread.Id, thread.ProfileId, "Goal", taskKind, resourcePath));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AgentRunDto>>();
        return payload!.Data!;
    }

    private sealed class FakeRunner : ICodexProcessRunner
    {
        public Task<CodexRunResult> RunAsync(AgentSettingsDto settings, string prompt, CancellationToken cancellationToken)
        {
            var isDatc64 = prompt.Contains("datc64-translation", StringComparison.Ordinal);
            var final = isDatc64
                ? """
                  ```json
                  {
                    "taskKind": "datc64-translation",
                    "profileId": "profile-1",
                    "resourcePath": "metadata/example.datc64",
                    "candidates": [
                      {
                        "locator": "row:1;column:3;name:text_3 @12",
                        "rowIndex": 0,
                        "columnIndex": 3,
                        "sourceText": "NoMana",
                        "translatedText": "法力不足",
                        "confidence": 0.86,
                        "notes": "test"
                      }
                    ]
                  }
                  ```
                  """
                : """
                  ```json
                  {"taskKind":"question","profileId":"profile-1","summary":"done","evidence":[]}
                  ```
                  """;
            var events = new[]
            {
                new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "poe_get_workspace completed", "{}", false, true, "poe_get_workspace"),
                new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, final, "{}", true, false, null)
            };
            return Task.FromResult(new CodexRunResult(0, false, false, events, null));
        }
    }
}
