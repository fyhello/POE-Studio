# POE Studio Agent Self-Diagnosis Repair Loop Acceptance

Date: 2026-05-25

## Scenario 1: Normal Current-View Missing Translation Query

- User input: `列出当前表格 3 个漏翻内容给我`
- Expected tools: `poe_get_current_view_context`, `poe_find_current_table_untranslated_cells`
- Forbidden tool: `poe_datc64_extract_translatable_cells`
- Expected result: AI outputs 3 candidates, or clearly explains why fewer than 3 were found.
- Evidence: covered by current-view prompt/tool tests and MCP overview/current-view tests.

## Scenario 2: Tool Completed But No Final Answer

- Injected Codex events: `tool_call completed` plus `done`, without a following assistant message.
- Expected: `done` is emitted first with `autoDiagnostic=true`, then a `diagnostic` event appears.
- Expected diagnostic code: `no_final_answer_after_tool_result`
- Evidence: `ChatServiceIntegrationTests.RunCodexAsync_sends_done_before_diagnostic_when_final_answer_is_missing`.

## Scenario 3: Tool In Progress Hangs

- Injected Codex events: `tool_call in_progress` without a later completed/failed event.
- Less than 30 seconds: diagnostic must not start.
- More than 30 seconds: diagnostic starts.
- Expected diagnostic code: `tool_call_left_in_progress`
- Evidence: `AgentDiagnosticsServiceTests.Analyze_does_not_mark_recent_tool_call_as_hung_before_threshold` and `AgentDiagnosticsServiceTests.Analyze_marks_tool_call_as_hung_after_threshold`.

## Scenario 4: User Approves Repair

- User action: click approve Agent repair.
- Expected: repair run records `git status --short --branch`.
- Expected: repair prompt requires failing test first, minimal fix, targeted tests, broader regression tests, optional restart, and original task regression.
- Evidence: `AgentRepairServiceTests.StartRepairAsync_rejects_missing_user_approval` and `AgentRepairServiceTests.StartRepairAsync_uses_repair_codex_capabilities_after_approval`.

## Verification

- Targeted tests:
  - `dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "CodexJsonEventParserTests|ChatServiceIntegrationTests|FrontendDatc64WorkflowTests"`
  - `dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "AgentRunTraceStoreTests|ChatServiceIntegrationTests"`
  - `dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "AgentDiagnosticsServiceTests|ChatServiceIntegrationTests"`
  - `dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "ChatServiceIntegrationTests|McpToolRegistryTests|McpPoeToolsTests"`
  - `dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~FrontendDatc64WorkflowTests.Chat_ui_exposes_diagnostic_and_repair_approval_controls"`
  - `dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "AgentRepairServiceTests|CodexProcessRunnerTests.RunAsync_writes_feature_flags_from_settings|ChatServiceIntegrationTests"`

## GitNexus detect_changes

- risk_level: critical
- changed_count: 182
- affected_processes: 26
- note: The workspace already contained broad uncommitted Agent/plan cleanup changes before this implementation. The critical scope is therefore a whole-worktree warning, not a clean isolated diff for only this plan.
