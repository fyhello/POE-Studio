using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Storage.Resources;

namespace PoeStudio.Tests;

public sealed class AgentApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _workspaceRoot;
    private readonly FakeRunner _runner = new();

    public AgentApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "poe-studio-agent-api-tests", Guid.NewGuid().ToString("N"));
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PoeStudio:WorkspaceRoot", _workspaceRoot);
            builder.UseSetting("PoeStudio:WorkspaceSettingsPath", Path.Combine(Path.GetTempPath(), "poe-studio-agent-api-tests", Guid.NewGuid().ToString("N"), "workspace-settings.json"));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICodexProcessRunner>();
                services.AddScoped<ICodexProcessRunner>(_ => _runner);
            });
        });
    }

    [Fact]
    public async Task Question_run_records_mcp_event_result_and_no_approval_or_overlay()
    {
        var client = _factory.CreateClient();
        var thread = await CreateThreadAsync(client, "question");

        var run = await CreateRunAsync(client, thread, "question", null);
        run = await WaitForRunStatusAsync(client, run.Id, AgentRunStatus.Succeeded);
        var events = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<AgentEventDto>>>($"/api/agent/runs/{run.Id}/events");
        var snapshot = await client.GetFromJsonAsync<ApiResponse<AgentThreadSnapshotDto>>($"/api/agent/threads/{thread.Id}");
        var overlay = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(thread.ProfileId));
        var overlayPayload = await overlay.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(AgentRunStatus.Succeeded, run.Status);
        Assert.Contains(events!.Data!, x => x.Type == AgentEventType.McpToolCall);
        Assert.Contains("done", run.ResultJson);
        Assert.Empty(snapshot!.Data!.PendingApprovals);
        Assert.Empty(overlayPayload!.Data!.Items);
    }

    [Fact]
    public async Task Datc64_run_waits_for_approval_then_writes_overlay()
    {
        var client = _factory.CreateClient();
        var profileId = "profile-1";
        var resourcePath = "metadata/example.datc64";
        var basePath = Path.Combine(_workspaceRoot, "fixtures", "example.datc64");
        Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
        await File.WriteAllBytesAsync(basePath, BuildDatc64PointerTableData([("NoMana", "法力不足")]));
        await new ResourceIndexStore(_workspaceRoot).SaveAsync(
            profileId,
            [
                new ResourceSummaryDto(
                    Guid.NewGuid().ToString("N"),
                    profileId,
                    resourcePath,
                    resourcePath,
                    ".datc64",
                    ResourceKind.Table,
                    new FileInfo(basePath).Length,
                    basePath,
                    ResourceSourceLayer.Base,
                    DateTimeOffset.UtcNow)
            ],
            [],
            CancellationToken.None);
        var thread = await CreateThreadAsync(client, "datc64-translation");

        var run = await CreateRunAsync(client, thread, "datc64-translation", resourcePath);
        run = await WaitForRunStatusAsync(client, run.Id, AgentRunStatus.WaitingForApproval);
        var beforeOverlay = await (await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(profileId))).Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();
        var snapshot = await client.GetFromJsonAsync<ApiResponse<AgentThreadSnapshotDto>>($"/api/agent/threads/{thread.Id}");
        var approval = Assert.Single(snapshot!.Data!.PendingApprovals);
        var approveResponse = await client.PostAsync($"/api/agent/approvals/{approval.Id}/approve", null);
        var approvePayload = await approveResponse.Content.ReadFromJsonAsync<ApiResponse<AgentApprovalDto>>();
        var afterOverlay = await (await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(profileId))).Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(AgentRunStatus.WaitingForApproval, run.Status);
        Assert.Empty(beforeOverlay!.Data!.Items);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        Assert.Equal(AgentApprovalStatus.Applied, approvePayload!.Data!.Status);
        var entry = Assert.Single(afterOverlay!.Data!.Items);
        Assert.Equal(resourcePath, entry.NormalizedPath);
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
        var run = payload!.Data!;
        run = await WaitForRunStatusAsync(client, run.Id, taskKind == "datc64-translation" ? AgentRunStatus.WaitingForApproval : AgentRunStatus.Succeeded);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload.Ok);
        Assert.Equal(taskKind == "datc64-translation" ? AgentRunStatus.WaitingForApproval : AgentRunStatus.Succeeded, run.Status);
    }

    [Fact]
    public async Task Agent_retry_creates_new_run_and_keeps_old_events()
    {
        var client = _factory.CreateClient();
        var thread = await CreateThreadAsync(client, "question");
        var run = await CreateRunAsync(client, thread, "question", null);
        run = await WaitForRunStatusAsync(client, run.Id, AgentRunStatus.Succeeded);

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
        run = await WaitForRunStatusAsync(client, run.Id, AgentRunStatus.Succeeded);

        var events = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<AgentEventDto>>>($"/api/agent/runs/{run.Id}/events");
        var missingApproval = await client.PostAsync("/api/agent/approvals/missing/approve", null);

        Assert.True(events!.Ok);
        Assert.NotEmpty(events.Data!);
        Assert.Equal(HttpStatusCode.NotFound, missingApproval.StatusCode);
    }

    [Fact]
    public async Task Agent_run_cancel_requests_running_runner_cancellation()
    {
        _runner.BlockUntilCancelled = true;
        var client = _factory.CreateClient();
        var thread = await CreateThreadAsync(client, "question");

        var createResponse = await client.PostAsJsonAsync("/api/agent/runs", new AgentRunCreateRequest(thread.Id, thread.ProfileId, "Goal", "question", null));
        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<AgentRunDto>>();
        var cancelResponse = await client.PostAsync($"/api/agent/runs/{created!.Data!.Id}/cancel", null);
        var cancelPayload = await cancelResponse.Content.ReadFromJsonAsync<ApiResponse<AgentRunDto>>();
        var final = await WaitForRunStatusAsync(client, created.Data.Id, AgentRunStatus.Cancelled);
        await WaitForRunnerCancellationAsync();
        var events = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<AgentEventDto>>>($"/api/agent/runs/{created.Data.Id}/events");

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal(AgentRunStatus.Cancelled, cancelPayload!.Data!.Status);
        Assert.Equal(AgentRunStatus.Cancelled, final.Status);
        Assert.True(_runner.SawCancellation);
        Assert.Contains(events!.Data!, x => x.Type == AgentEventType.RunCancelled);
    }

    [Fact]
    public async Task Agent_run_cancel_rejects_completed_run_without_overwriting_status()
    {
        var client = _factory.CreateClient();
        var thread = await CreateThreadAsync(client, "question");
        var run = await CreateRunAsync(client, thread, "question", null);
        run = await WaitForRunStatusAsync(client, run.Id, AgentRunStatus.Succeeded);

        var cancelResponse = await client.PostAsync($"/api/agent/runs/{run.Id}/cancel", null);
        var cancelPayload = await cancelResponse.Content.ReadFromJsonAsync<ApiResponse<AgentRunDto>>();
        var after = await client.GetFromJsonAsync<ApiResponse<AgentRunDto>>($"/api/agent/runs/{run.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, cancelResponse.StatusCode);
        Assert.False(cancelPayload!.Ok);
        Assert.Equal("run_not_running", cancelPayload.ErrorCode);
        Assert.Equal(AgentRunStatus.Succeeded, after!.Data!.Status);
    }

    [Theory]
    [InlineData("missing-thread", "profile-1", "Goal", "question", null, "thread_not_found")]
    [InlineData("valid-thread", "profile-1", "Goal", "unsupported", null, "unsupported_task_kind")]
    [InlineData("valid-thread", "profile-1", "Goal", "datc64-translation", null, "resource_path_required")]
    public async Task Agent_run_create_returns_structured_failure_without_500(
        string threadSelector,
        string profileId,
        string goal,
        string taskKind,
        string? resourcePath,
        string expectedError)
    {
        var client = _factory.CreateClient();
        var threadId = threadSelector;
        if (threadSelector == "valid-thread")
        {
            threadId = (await CreateThreadAsync(client, taskKind == "unsupported" ? "question" : taskKind)).Id;
        }

        var response = await client.PostAsJsonAsync("/api/agent/runs", new AgentRunCreateRequest(threadId, profileId, goal, taskKind, resourcePath));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AgentRunDto>>();

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(payload!.Ok);
        Assert.Equal(expectedError, payload.ErrorCode);
    }

    [Fact]
    public async Task Approval_apply_failure_keeps_approval_pending_so_user_can_reject()
    {
        var client = _factory.CreateClient();
        _runner.Datc64RowIndex = 10;
        var profileId = "profile-1";
        var resourcePath = "metadata/example.datc64";
        var basePath = Path.Combine(_workspaceRoot, "fixtures", "bad-locator.datc64");
        Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
        await File.WriteAllBytesAsync(basePath, BuildDatc64PointerTableData([("NoMana", "法力不足")]));
        await SaveResourceIndexAsync(profileId, resourcePath, basePath);
        var thread = await CreateThreadAsync(client, "datc64-translation");
        var run = await CreateRunAsync(client, thread, "datc64-translation", resourcePath);
        run = await WaitForRunStatusAsync(client, run.Id, AgentRunStatus.WaitingForApproval);
        var snapshot = await client.GetFromJsonAsync<ApiResponse<AgentThreadSnapshotDto>>($"/api/agent/threads/{thread.Id}");
        var approval = Assert.Single(snapshot!.Data!.PendingApprovals);

        var approveResponse = await client.PostAsync($"/api/agent/approvals/{approval.Id}/approve", null);
        var rejectResponse = await client.PostAsync($"/api/agent/approvals/{approval.Id}/reject", null);
        var rejected = await rejectResponse.Content.ReadFromJsonAsync<ApiResponse<AgentApprovalDto>>();

        Assert.Equal(HttpStatusCode.BadRequest, approveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        Assert.Equal(AgentApprovalStatus.Rejected, rejected!.Data!.Status);
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

    private static async Task<AgentRunDto> WaitForRunStatusAsync(HttpClient client, string runId, AgentRunStatus status)
    {
        for (var i = 0; i < 50; i++)
        {
            var payload = await client.GetFromJsonAsync<ApiResponse<AgentRunDto>>($"/api/agent/runs/{runId}");
            if (payload!.Data!.Status == status)
            {
                return payload.Data;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Run {runId} did not reach {status}.");
    }

    private async Task WaitForRunnerCancellationAsync()
    {
        for (var i = 0; i < 50; i++)
        {
            if (_runner.SawCancellation)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Runner did not observe cancellation.");
    }

    private async Task SaveResourceIndexAsync(string profileId, string resourcePath, string physicalPath)
    {
        await new ResourceIndexStore(_workspaceRoot).SaveAsync(
            profileId,
            [
                new ResourceSummaryDto(
                    Guid.NewGuid().ToString("N"),
                    profileId,
                    resourcePath,
                    resourcePath,
                    ".datc64",
                    ResourceKind.Table,
                    new FileInfo(physicalPath).Length,
                    physicalPath,
                    ResourceSourceLayer.Base,
                    DateTimeOffset.UtcNow)
            ],
            [],
            CancellationToken.None);
    }

    private sealed class FakeRunner : ICodexProcessRunner
    {
        public int Datc64RowIndex { get; set; }
        public bool BlockUntilCancelled { get; set; }
        public bool SawCancellation { get; private set; }

        public async Task<CodexRunResult> RunAsync(AgentSettingsDto settings, string prompt, CancellationToken cancellationToken)
        {
            if (BlockUntilCancelled)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    SawCancellation = true;
                    return new CodexRunResult(
                        null,
                        false,
                        true,
                        [new CodexParsedEvent("{}", CodexParsedEventType.Cancelled, "cancelled", "{}", true, false, null)],
                        null);
                }
            }

            var isDatc64 = prompt.Contains("datc64-translation", StringComparison.Ordinal);
            var rowIndex = Datc64RowIndex;
            var final = isDatc64
                ? $$"""
                  ```json
                  {
                    "taskKind": "datc64-translation",
                    "profileId": "profile-1",
                    "resourcePath": "metadata/example.datc64",
                    "candidates": [
                      {
                        "locator": "row:{{rowIndex + 1}};column:3;name:text_3 @12",
                        "rowIndex": {{rowIndex}},
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
            return new CodexRunResult(0, false, false, events, null);
        }
    }

    private static byte[] BuildDatc64PointerTableData(IReadOnlyList<(string Id, string Text)> rows)
    {
        const int rowLength = 32;
        var fixedData = new byte[rows.Count * rowLength];
        using var variable = new MemoryStream();
        variable.Write(new byte[] { 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb });

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowOffset = rowIndex * rowLength;
            WriteUInt32(fixedData, rowOffset, (uint)rowIndex);
            WriteUInt32(fixedData, rowOffset + 4, AppendDatc64String(variable, rows[rowIndex].Id));
            WriteUInt32(fixedData, rowOffset + 8, 0);
            WriteUInt32(fixedData, rowOffset + 12, AppendDatc64String(variable, rows[rowIndex].Text));
        }

        var variableData = variable.ToArray();
        var data = new byte[4 + fixedData.Length + variableData.Length];
        WriteUInt32(data, 0, (uint)rows.Count);
        fixedData.CopyTo(data, 4);
        variableData.CopyTo(data, 4 + fixedData.Length);
        return data;
    }

    private static uint AppendDatc64String(Stream stream, string value)
    {
        var offset = checked((uint)stream.Position);
        var bytes = Encoding.Unicode.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        return offset;
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value & 0xff);
        data[offset + 1] = (byte)((value >> 8) & 0xff);
        data[offset + 2] = (byte)((value >> 16) & 0xff);
        data[offset + 3] = (byte)((value >> 24) & 0xff);
    }
}
