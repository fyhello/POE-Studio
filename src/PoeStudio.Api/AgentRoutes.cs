using PoeStudio.Api;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Core.Workspace;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Api;

public static class AgentRoutes
{
    private static readonly string[] AllowedSandboxes = ["read-only", "workspace-write", "danger-full-access"];

    public static WebApplication MapAgentRoutes(this WebApplication app)
    {
        app.MapGet("/api/agent/settings", async (AgentStore store, WorkspaceRootProvider workspace, CancellationToken cancellationToken) =>
        {
            var settings = await store.GetSettingsAsync(cancellationToken) ?? DefaultSettings(workspace.CurrentRoot);
            return ApiResponse<AgentSettingsDto>.Success(settings);
        });

        app.MapPost("/api/agent/settings", async (AgentSettingsDto request, AgentStore store, CancellationToken cancellationToken) =>
        {
            if (!IsValidCodexPath(request.CodexPath))
            {
                return Results.BadRequest(ApiResponse<AgentSettingsDto>.Failure("invalid_codex_path", "codexPath must be codex or an executable path."));
            }

            if (!AllowedSandboxes.Contains(request.Sandbox, StringComparer.Ordinal))
            {
                return Results.BadRequest(ApiResponse<AgentSettingsDto>.Failure("invalid_sandbox", "sandbox must be read-only, workspace-write, or danger-full-access."));
            }

            var settings = request with
            {
                McpServerName = string.IsNullOrWhiteSpace(request.McpServerName) ? "poe-studio" : request.McpServerName,
                ApprovalMode = string.IsNullOrWhiteSpace(request.ApprovalMode) ? "manual" : request.ApprovalMode
            };
            await store.SaveSettingsAsync(settings, cancellationToken);
            return Results.Ok(ApiResponse<AgentSettingsDto>.Success(settings));
        });

        app.MapPost("/api/agent/threads", async (AgentThreadCreateRequest request, AgentStore store, CancellationToken cancellationToken) =>
        {
            try
            {
                AgentCapabilities.GetRequired(request.TaskKind);
                var thread = await store.SaveNewThreadAsync(request.ProfileId, request.Title, request.Goal, request.TaskKind, cancellationToken);
                return Results.Ok(ApiResponse<AgentThreadDto>.Success(thread));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ApiResponse<AgentThreadDto>.Failure(ex.Message, ex.Message));
            }
        });

        app.MapPost("/api/agent/threads/{threadId}/messages", async (
            string threadId,
            AgentMessageCreateRequest request,
            AgentStore store,
            CancellationToken cancellationToken) =>
        {
            var thread = await store.GetThreadAsync(threadId, cancellationToken);
            if (thread is null)
            {
                return Results.NotFound(ApiResponse<AgentMessageDto>.Failure("thread_not_found", "Thread not found."));
            }

            var message = new AgentMessageDto(
                NewId("message"),
                threadId,
                AgentMessageRole.User,
                request.Content,
                request.Attachments is null ? null : System.Text.Json.JsonSerializer.Serialize(request.Attachments),
                DateTimeOffset.UtcNow);
            await store.AppendMessageAsync(message, cancellationToken);
            return Results.Ok(ApiResponse<AgentMessageDto>.Success(message));
        });

        app.MapGet("/api/agent/threads/{threadId}", async (string threadId, AgentStore store, CancellationToken cancellationToken) =>
        {
            var thread = await store.GetThreadAsync(threadId, cancellationToken);
            if (thread is null)
            {
                return Results.NotFound(ApiResponse<AgentThreadSnapshotDto>.Failure("thread_not_found", "Thread not found."));
            }

            var messages = await store.ListMessagesAsync(threadId, cancellationToken);
            var runs = await store.ListRunsAsync(threadId, cancellationToken);
            var latestRun = runs.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
            var latestPlan = latestRun is null
                ? []
                : await store.GetPlanAsync(threadId, latestRun.Id, cancellationToken);
            var pending = latestRun is null
                ? []
                : (await store.ListApprovalsAsync(threadId, latestRun.Id, cancellationToken))
                    .Where(x => x.Status == AgentApprovalStatus.Pending)
                    .ToArray();
            return Results.Ok(ApiResponse<AgentThreadSnapshotDto>.Success(new AgentThreadSnapshotDto(thread, messages, runs, latestPlan, pending)));
        });

        app.MapPost("/api/agent/runs", async (
            AgentRunCreateRequest request,
            AgentOrchestrator orchestrator,
            AgentRunCancellationRegistry cancellations,
            IServiceScopeFactory scopeFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (string.Equals(request.TaskKind, "datc64-translation", StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(request.ResourcePath))
                {
                    return Results.BadRequest(ApiResponse<AgentRunDto>.Failure("resource_path_required", "resourcePath is required for datc64-translation."));
                }

                var run = await orchestrator.StartRunShellAsync(request.ThreadId, request.ProfileId, request.Goal, request.TaskKind, request.ResourcePath, cancellationToken);
                StartBackgroundRun(run.Id, cancellations, scopeFactory);
                return Results.Ok(ApiResponse<AgentRunDto>.Success(run));
            }
            catch (ArgumentException ex)
            {
                var errorCode = StableErrorCode(ex);
                return Results.BadRequest(ApiResponse<AgentRunDto>.Failure(errorCode, ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponse<AgentRunDto>.Failure(ex.Message, ex.Message));
            }
        });

        app.MapPost("/api/agent/runs/{runId}/retry", async (
            string runId,
            AgentOrchestrator orchestrator,
            AgentRunCancellationRegistry cancellations,
            IServiceScopeFactory scopeFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var run = await orchestrator.RetryShellAsync(runId, cancellationToken);
                StartBackgroundRun(run.Id, cancellations, scopeFactory);
                return Results.Ok(ApiResponse<AgentRunDto>.Success(run));
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(ApiResponse<AgentRunDto>.Failure(ex.Message, ex.Message));
            }
        });

        app.MapGet("/api/agent/runs/{runId}", async (string runId, AgentStore store, CancellationToken cancellationToken) =>
        {
            var run = await store.FindRunAsync(runId, cancellationToken);
            return run is null
                ? Results.NotFound(ApiResponse<AgentRunDto>.Failure("run_not_found", "Run not found."))
                : Results.Ok(ApiResponse<AgentRunDto>.Success(run));
        });

        app.MapGet("/api/agent/runs/{runId}/events", async (string runId, long? afterSequence, AgentStore store, CancellationToken cancellationToken) =>
        {
            var run = await store.FindRunAsync(runId, cancellationToken);
            if (run is null)
            {
                return Results.NotFound(ApiResponse<IReadOnlyList<AgentEventDto>>.Failure("run_not_found", "Run not found."));
            }

            var events = await store.ListEventsAsync(run.ThreadId, run.Id, afterSequence ?? 0, cancellationToken);
            return Results.Ok(ApiResponse<IReadOnlyList<AgentEventDto>>.Success(events));
        });

        app.MapPost("/api/agent/runs/{runId}/cancel", async (
            string runId,
            AgentStore store,
            AgentRunCancellationRegistry cancellations,
            CancellationToken cancellationToken) =>
        {
            var run = await store.FindRunAsync(runId, cancellationToken);
            if (run is null)
            {
                return Results.NotFound(ApiResponse<AgentRunDto>.Failure("run_not_found", "Run not found."));
            }

            if (run.Status != AgentRunStatus.Running)
            {
                return Results.BadRequest(ApiResponse<AgentRunDto>.Failure("run_not_running", "Run is not running."));
            }

            cancellations.Cancel(runId);
            var cancelled = run with { Status = AgentRunStatus.Cancelled, Message = "Cancelled", UpdatedAt = DateTimeOffset.UtcNow };
            await store.SaveRunAsync(cancelled, cancellationToken);
            await store.AppendEventAsync(run.ThreadId, run.Id, AgentEventType.RunCancelled, "Cancellation requested", null, cancellationToken);
            return Results.Ok(ApiResponse<AgentRunDto>.Success(cancelled));
        });

        app.MapPost("/api/agent/approvals/{approvalId}/approve", async (
            string approvalId,
            AgentStore store,
            Datc64DraftApplyService applyService,
            CancellationToken cancellationToken) =>
        {
            var located = await store.FindApprovalAsync(approvalId, cancellationToken);
            if (located is null)
            {
                return Results.NotFound(ApiResponse<AgentApprovalDto>.Failure("approval_not_found", "Approval not found."));
            }

            if (located.Value.Approval.Status != AgentApprovalStatus.Pending)
            {
                return Results.BadRequest(ApiResponse<AgentApprovalDto>.Failure("approval_not_pending", "Approval is not pending."));
            }

            if (string.Equals(located.Value.Approval.Kind, "datc64-translation", StringComparison.Ordinal))
            {
                var apply = await applyService.ApplyAsync(located.Value.ThreadId, located.Value.RunId, approvalId, cancellationToken);
                if (!apply.Applied)
                {
                    await store.AppendEventAsync(located.Value.ThreadId, located.Value.RunId, AgentEventType.RunFailed, apply.ErrorCode ?? "approval_apply_failed", null, cancellationToken);
                    return Results.BadRequest(ApiResponse<AgentApprovalDto>.Failure(apply.ErrorCode ?? "approval_apply_failed", apply.ErrorCode ?? "Approval apply failed."));
                }
            }
            else
            {
                var changed = await store.TryUpdateApprovalStatusAsync(located.Value.ThreadId, located.Value.RunId, approvalId, AgentApprovalStatus.Pending, AgentApprovalStatus.Approved, null, cancellationToken);
                if (!changed)
                {
                    return Results.BadRequest(ApiResponse<AgentApprovalDto>.Failure("approval_not_pending", "Approval is not pending."));
                }
            }

            await store.AppendEventAsync(located.Value.ThreadId, located.Value.RunId, AgentEventType.ApprovalApproved, "Approval approved", null, cancellationToken);
            var updated = (await store.ListApprovalsAsync(located.Value.ThreadId, located.Value.RunId, cancellationToken)).First(x => x.Id == approvalId);
            return Results.Ok(ApiResponse<AgentApprovalDto>.Success(updated));
        });

        app.MapPost("/api/agent/approvals/{approvalId}/reject", async (
            string approvalId,
            AgentStore store,
            CancellationToken cancellationToken) =>
        {
            var located = await store.FindApprovalAsync(approvalId, cancellationToken);
            if (located is null)
            {
                return Results.NotFound(ApiResponse<AgentApprovalDto>.Failure("approval_not_found", "Approval not found."));
            }

            var changed = await store.TryUpdateApprovalStatusAsync(located.Value.ThreadId, located.Value.RunId, approvalId, AgentApprovalStatus.Pending, AgentApprovalStatus.Rejected, null, cancellationToken);
            if (!changed)
            {
                return Results.BadRequest(ApiResponse<AgentApprovalDto>.Failure("approval_not_pending", "Approval is not pending."));
            }

            await store.AppendEventAsync(located.Value.ThreadId, located.Value.RunId, AgentEventType.ApprovalRejected, "Approval rejected", null, cancellationToken);
            var updated = (await store.ListApprovalsAsync(located.Value.ThreadId, located.Value.RunId, cancellationToken)).First(x => x.Id == approvalId);
            return Results.Ok(ApiResponse<AgentApprovalDto>.Success(updated));
        });

        return app;
    }

    private static AgentSettingsDto DefaultSettings(string workspaceRoot)
    {
        return new AgentSettingsDto("codex", null, null, "workspace-write", "poe-studio", workspaceRoot, "manual");
    }

    private static bool IsValidCodexPath(string codexPath)
    {
        return string.Equals(codexPath, "codex", StringComparison.OrdinalIgnoreCase)
            || File.Exists(codexPath);
    }

    private static string StableErrorCode(ArgumentException ex)
    {
        var marker = ex.Message.IndexOf(" (Parameter", StringComparison.Ordinal);
        return marker > 0 ? ex.Message[..marker] : ex.Message;
    }

    private static void StartBackgroundRun(
        string runId,
        AgentRunCancellationRegistry cancellations,
        IServiceScopeFactory scopeFactory)
    {
        var runToken = cancellations.Register(runId);
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scopedOrchestrator = scope.ServiceProvider.GetRequiredService<AgentOrchestrator>();
                await scopedOrchestrator.ContinueRunAsync(runId, runToken);
            }
            catch (Exception ex)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var scopedOrchestrator = scope.ServiceProvider.GetRequiredService<AgentOrchestrator>();
                    await scopedOrchestrator.FailRunAsync(runId, "agent_background_failed", ex.Message, CancellationToken.None);
                }
                catch
                {
                    // Last-resort guard for fire-and-forget work. Nothing else can observe this exception.
                }
            }
            finally
            {
                cancellations.Complete(runId);
            }
        }, CancellationToken.None);
    }

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
