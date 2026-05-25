# POE Studio Codex Bridge Stage 2 Acceptance

Date: 2026-05-22

Stage 2 status: PASS

## Baseline

- Current Stage 2 implementation branch/head includes Stage 1 MCP project.
- Stage 1 acceptance report contains `Stage 1 status: PASS`.
- Project memory was read before implementation: `docs/ai-project-memory.md`.
- GitNexus index was refreshed before Stage 2 work and after task commits.

## GitNexus Impact Evidence

- `Program` upstream impact before API routes: LOW, direct callers 0, affected processes 0.
- `TableInspector` upstream impact before DATC64 apply service: CRITICAL, direct callers 38, affected processes 5.
- `OverlayStore` upstream impact before DATC64 apply service: CRITICAL, direct callers 45, affected processes 0.
- Mitigation: Stage 2 did not modify `TableInspector` or `OverlayStore`; it only called existing public APIs from `Datc64DraftApplyService`.

## Codex And MCP Evidence

- Codex version: `codex-cli 0.131.0`.
- `codex mcp get poe-studio`: enabled, stdio transport, command `dotnet run --project src\PoeStudio.Mcp\PoeStudio.Mcp.csproj -- --workspace-root C:\Users\25147\AppData\Local\PoeStudio`.
- Real bare `codex -a on-request exec --json` MCP smoke:
  - `poe-studio.poe_get_workspace` completed.
  - `poe-studio.poe_list_profiles` completed.
  - Final message contained `{"workspaceSeen":true,"profilesSeen":true}`.
  - No `user cancelled MCP tool call` occurred.
- Note: Codex CLI can still emit plugin remote-sync and Windows sandbox shell warnings on stderr in API-key mode. These are persisted as stderr events and are separate from successful `poe-studio` MCP tool calls.

## Implemented Capabilities

- `question`: read-only capability through unified thread/message/run/plan/event storage.
- `read-only-analysis`: read-only capability through the same orchestrator and prompt builder.
- `datc64-translation`: proposal-only capability that creates approval records; overlay write happens only after approval.

## API Evidence

- `GET /api/agent/settings` returns persisted Codex bridge settings.
- `POST /api/agent/settings` persists settings.
- `POST /api/agent/threads` creates durable thread records.
- `POST /api/agent/threads/{threadId}/messages` persists user messages.
- `GET /api/agent/threads/{threadId}` returns messages, runs, latest plan, and pending approvals.
- `POST /api/agent/runs` supports `question`, `read-only-analysis`, and `datc64-translation`.
- `POST /api/agent/runs/{runId}/retry` creates a new run without overwriting old events.
- `GET /api/agent/runs/{runId}/events` returns recorded run events.
- `POST /api/agent/approvals/{approvalId}/approve` applies DATC64 overlay draft only after approval.
- `POST /api/agent/approvals/{approvalId}/reject` rejects pending approvals without writing overlay.

## Fake Codex End-To-End Evidence

- Question run:
  - Fake Codex emitted `poe_get_workspace` MCP tool event.
  - Run reached `Succeeded`.
  - Final result JSON was persisted.
  - Approvals were empty.
  - Overlay list stayed empty.
- DATC64 run:
  - Fake Codex emitted DATC64 proposal JSON.
  - Run reached `WaitingForApproval`.
  - Approval existed before write.
  - Overlay list was empty before approval.
  - Approval route changed status to `Applied`.
  - Overlay list contained `metadata/example.datc64` after approval.

## Verification Commands

```powershell
codex --version
codex mcp get poe-studio
codex -a on-request exec --json "<prompt that calls poe_get_workspace and poe_list_profiles>"
```

Result: Codex `0.131.0`; `poe-studio` MCP enabled; real MCP `poe_get_workspace` and `poe_list_profiles` completed.

```powershell
dotnet run --project src\PoeStudio.Api\PoeStudio.Api.csproj --urls http://localhost:5014
```

Real backend question run:
- Thread: `thread-68c38f7c8665410391ba3e211b0e19ca`
- Run: `run-1d823c95d1fd4cb7915713fd01f08031`
- Final status: `Succeeded`
- Event count: `44`
- Events included `poe-studio.poe_get_workspace completed` and `poe-studio.poe_list_profiles completed`.

Real backend DATC64 proposal run:
- Thread: `thread-389836dafd98494d9ce6cfb94a39ee45`
- Run: `run-7f4606755ff042ed8a89619ebd9ef5b8`
- Final status: `WaitingForApproval`
- Event count: `47`
- Events included `poe-studio.poe_datc64_extract_translatable_cells completed`.
- Approval: `approval-1f395d286578452896c53c7f054a2008`
- Approval summary: `0 DATC64 translation candidate(s)` for the physical fixture `metadata/agent-ui.datc64`; overlay write was not attempted because there were no candidates.

Real backend cancel run:
- Run: `run-adc087fbd4a04c36963ac8cdcce01a90`
- Final status: `Cancelled`
- Before cancel, process tree included `codex.exe`, `dotnet run ... PoeStudio.Mcp`, and `PoeStudio.Mcp.exe`.
- After cancel, no matching `codex exec` / `PoeStudio.Mcp` child processes remained.

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentApiSmokeTests
```

Result: included in targeted Agent test run below.

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~McpToolRegistryTests|FullyQualifiedName~CodexProcessRunnerTests|FullyQualifiedName~CodexJsonEventParserTests|FullyQualifiedName~AgentApiSmokeTests"
```

Result: 38 passed, 0 failed, 0 skipped.

```powershell
dotnet test PoeStudio.sln --no-restore --filter FullyQualifiedName~TableInspectorTests
```

Result: 32 passed, 0 failed, 0 skipped.

```powershell
dotnet test PoeStudio.sln --no-restore
```

Result: 410 passed, 0 failed, 0 skipped.

## Stage 2 Guardrail Check

- No Stage 3 Agent Workspace UI was added.
- No arbitrary shell route was added.
- No DATC64-specific primary route was added; DATC64 uses `/api/agent/runs`.
- Agent runs persist thread, messages, run, events, plan, approvals, status, and results.
- `Core` still does not reference `Storage`.
- DATC64 overlay write is approval-gated.
