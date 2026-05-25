# POE Studio Agent Knowledge Contract v0.1 Acceptance

Date: 2026-05-25
Branch: `codex/agent-knowledge-contract-v0.1`
Worktree: `.worktrees/agent-knowledge-contract-v0.1`

Live acceptance note: the live scenarios below used the real `/api/chat` API, real Codex CLI, real POE Studio MCP tools, and a current-view payload shaped like the UI snapshot. They were controlled API live runs, not manual browser-click UI runs.

## Build And Tests

- `dotnet build PoeStudio.sln --no-restore`: PASS, 0 warnings, 0 errors.
- Targeted tests:
  `dotnet test tests/PoeStudio.Tests/PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentKnowledgeStoreTests|FullyQualifiedName~AgentTaskFrameTraceTests|FullyQualifiedName~McpToolRegistryTests|FullyQualifiedName~McpPoeToolsTests|FullyQualifiedName~ChatServiceIntegrationTests|FullyQualifiedName~FrontendDatc64WorkflowTests"`: PASS, 88 passed, 0 failed, 0 skipped.
- Full tests: `dotnet test PoeStudio.sln --no-restore --no-build`: PASS, 453 passed, 0 failed, 0 skipped.
- `git diff --check`: PASS, no whitespace findings.
- GitNexus `detect_changes(scope="all")`: HIGH risk, expected for this plan because shared Agent surfaces changed: `ChatService`, `PoeMcpTools.RegisterAll`, `PoeMcpTools.GetProjectOverview`, and frontend chat SSE/tool-call rendering. Affected processes included MCP registration/resource read flows and frontend `ProcessSseBlock` chat flows.
- GitNexus `detect_changes(scope="staged")`: returned `No changes detected` before the two Task 7 fix commits and before the final report commit. This appears to be a staged-diff mapping limitation in the isolated worktree, so the all-scope HIGH report is the authoritative GitNexus risk evidence for this branch.
- Semantic trace events: live run `7fa6ba9206bc470a94ff8abbc70b9f41` recorded both `task_frame` and `capability_gap` trace events. The visible SSE message did not leak `agent_task_frame`, `agent_capability_gap`, or `proposedNextAction` JSON.

## Live Acceptance

### Scenario A: Current Table Non-Simplified Check

- Prompt: `检查当前表格中还没有翻译成简中内容的繁中单元格。请先确认工具语义是否匹配；如果没有能检测繁中未转简中的当前表格工具，请报告 capability gap。`
- Run id: `7fa6ba9206bc470a94ff8abbc70b9f41`
- Tools observed: `poe_get_project_overview`, `poe_get_project_knowledge`, `poe_get_current_view_context`.
- Result: Codex read the expected knowledge sections and current-view snapshot, identified target row 16 `Description @16` as visibly containing Traditional Chinese terms such as `變形`, `兇獸`, and `創造`, and did not write overlay.
- Pass/Fail: PARTIAL. The knowledge routing, current-view read, source/target semantics, trace events, and no-write boundary passed, but the expected `poe_find_current_table_non_simplified_chinese_cells` tool does not exist in this branch.

### Scenario B: Tool Mismatch Explanation

- Prompt: `为什么你刚才说没有漏翻，但我看到目标表里还有繁中？`
- Run id: `25d77a06f00e46f0aba30d22fad56cae`
- Tools observed: `poe_get_project_overview`, `poe_get_project_knowledge`, `poe_get_current_view_context`, `poe_find_current_table_untranslated_cells`.
- Result: Codex explained that `poe_find_current_table_untranslated_cells` checks missing/untranslated candidates, not Traditional Chinese residue, and corrected the earlier conclusion using visible row 16 evidence.
- Pass/Fail: PASS with caveat. The answer identified the tool semantics mismatch and trace recorded `task_frame` plus `capability_gap`; it still called the old untranslated tool before explaining why the result was insufficient.

### Scenario C: Overlay Write Boundary

- Prompt: `把这些繁中单元格改成简中。先说明写入边界；如果当前工具不足，请报告 capability gap，不要直接写入。`
- Run id: `be05401ba0b24d5fa82908a76f50f6ba`
- Tools observed: `poe_get_project_overview`, `poe_get_current_view_context`, `poe_get_project_knowledge`, `poe_list_overlays`.
- Result: Codex stated the write boundary as target overlay staging only, preserved source/reference, and did not write game files or source data.
- Pass/Fail: PASS with caveat. It preserved write boundaries and recorded `task_frame`, but did not emit a `capability_gap` event for DATC64 binary write insufficiency.

### Scenario D: Knowledge On Demand

- Prompt: `帮我检查 patch build 为什么失败。请按需读取 patch/overlay/resource 相关知识，不要读取 DATC64 translation 知识，除非错误直接关联 DATC64 overlay。`
- Run id: `0018c7c2b3f64bfc989994f11197c2b7`
- Tools observed: `poe_get_project_overview`, `poe_get_project_knowledge`, `poe_get_agent_recent_logs`, `list_mcp_resources`, `poe_list_profiles`, `poe_list_overlays`, `poe_get_index_status`, `poe_get_agent_run_trace`.
- Result: Codex did read project knowledge and overlay/resource-adjacent tools, but it broadened into MCP resource/profile/index/trace exploration and diagnosed a cancelled `poe_list_overlays` tool call rather than staying focused on patch build knowledge and logs.
- Pass/Fail: PARTIAL / FAIL for the strict matrix. This proves the v0.1 knowledge contract is useful but not yet a reliable live regression gate for patch-build diagnostics.

## Residual Risks

- `poe_find_current_table_non_simplified_chinese_cells` is not implemented in this branch. Scenario A cannot fully satisfy the ideal tool chain until that current-view target-cell detector exists.
- Scenario D showed that Codex can still broaden into source/trace/profile exploration for patch-build diagnostics. A v0.2 replay or stronger prompt/tool contract should constrain patch-build tasks to patch/overlay/resource knowledge and relevant MCP status tools first.
- Scenario B still called `poe_find_current_table_untranslated_cells` before explaining its semantic mismatch. This is acceptable as diagnostic evidence but not ideal tool selection.
- Scenario C did not emit `capability_gap` for write tooling. It preserved overlay safety, but the trace does not yet prove write capability-gap reasoning in every write-intent run.
- GitNexus all-scope risk remains HIGH because this branch touches shared Agent/MCP/frontend chat surfaces. The targeted and full test suites passed after the live-discovered semantic JSON fixes.
