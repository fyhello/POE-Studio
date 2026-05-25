# Agent Project Cognition Integration Acceptance

Agent project cognition integration status: PASS

Date: 2026-05-23
Branch: codex/agent-cognition-snapshot-20260523

## Test Commands And Results

- `dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentApiSmokeTests`
  - Result: PASS, 18/18 tests passed.
- `dotnet test PoeStudio.sln --no-restore`
  - Result: PASS, 443/443 tests passed.
- Real API + Codex smoke through `http://localhost:5010`
  - Result: PASS, run `run-a22bd6b880cf4d3cbaf6cf509b7e2791` reached `Succeeded`.

## Fake Codex Prompt Verification

`AgentApiSmokeTests.Datc64_run_waits_for_approval_then_writes_overlay` captures the fake Codex runner prompt and verifies it contains:

- `Project context`
- `current working state`
- `poe_get_project_context`
- `poe_read_resource`
- `useOverlay parameter`
- `Requires approval`

The same smoke test verifies the prompt stays below 16000 characters and does not contain the full `docs/agent/poe-studio-project-workflows.md` document.

## Prompt Budget Verification

Budget is covered at three levels:

- `AgentProjectContextServiceTests.BuildAsync_enforces_summary_section_and_full_document_budgets` verifies summary <= 2500 chars, each relevant section <= 900 chars, and the serialized context does not include the full source document.
- `McpProjectContextToolTests.Get_project_context_returns_bounded_content_without_full_document` verifies the MCP tool returns bounded summary/sections and does not return the full workflow document.
- `AgentPromptBuilderTests.Build_injects_bounded_project_context_before_mcp_tools` verifies the final prompt stays below 16000 chars and does not include a long 1300-character source section verbatim.

A direct prompt budget probe for a DATC64 run produced a 9529-character prompt and `False` for full workflow-document inclusion.

## Run Event Verification

`AgentApiSmokeTests.Agent_background_run_unknown_exception_marks_run_failed_with_event` verifies the run records project context evidence even when the fake runner throws:

- event message: `Project context loaded`
- payload contains `projectContextLoaded: true`
- payload contains serialized preflight data
- latest plan starts with `Load project context`

`AgentOrchestratorTests.StartRunAsync_loads_project_context_before_runner_and_records_preflight` additionally verifies the prompt passed to the runner includes `Project context` and the preflight event contains `repositoryRoot` and `sources`.

## Real Codex Smoke

Run id: `run-a22bd6b880cf4d3cbaf6cf509b7e2791`

Event summary:

- `Project context loaded` was recorded before Codex execution.
- Preflight payload included `projectContextLoaded: true`, repository root, source paths, hashes, summary, required checks, and warnings.
- Codex emitted `poe-studio.poe_get_project_context in_progress` and `poe-studio.poe_get_project_context completed` MCP events.
- Latest plan completed `Load project context`, `Build prompt`, `Run Codex`, and `Store result`.

Final answer summary:

Codex explained that POE Studio current working state means overlay/draft first, base fallback, while current MCP read tools such as `poe_read_resource` and `poe_datc64_extract_translatable_cells` are Stage 1 read-only tools without `useOverlay`, `preferOverlay`, or `readLayer`, so their results cannot alone represent UI current working state. The answer explicitly stated no overlay was written and no files were modified.

Codex produced non-fatal plugin/authentication warnings during startup, but the run succeeded.

## DATC64 Cognition Sample

The fake DATC64 API smoke verifies the Agent prompt includes project context, current working state, MCP read-layer limitations, and approval boundaries before generating an approval-gated DATC64 proposal. The implementation does not add overlay-aware DATC64 reading, does not change the DATC64 proposal schema, and does not turn DATC64 into the only project capability.

## Unresolved Issues

- MCP resource tools still do not support `useOverlay` or target current working state; this plan only teaches the Agent that limitation and exposes project context.
- Codex startup logged remote plugin sync warnings under API-key auth; they did not block the successful run.
