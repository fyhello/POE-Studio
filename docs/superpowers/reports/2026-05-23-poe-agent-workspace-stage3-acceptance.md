# POE Studio Agent Workspace Stage 3 Acceptance

Stage 3 status: **PASS**

## Scope
- Agent Workspace UI — 侧边栏布局（VSCode 风格）
- Thread/run/event/approval/settings consumption via `/api/agent/*`
- Natural language task entry
- Session restore via localStorage

## Test Results

| 测试命令 | 结果 |
|----------|------|
| `dotnet test --filter "AgentStoreTests\|AgentApiSmokeTests\|FrontendAgentWorkspaceTests"` | ✅ 31/31 通过 |
| `dotnet test PoeStudio.sln --no-restore` | ✅ 454/454 通过 |

### 新增测试
- `AgentStoreTests.ListThreadsAsync_returns_recent_threads_ordered_by_updated_at` ✅
- `AgentApiSmokeTests.Agent_workspace_bootstrap_apis_return_capabilities_and_recent_threads` ✅
- `FrontendAgentWorkspaceTests.Index_contains_agent_workspace_shell` ✅
- `FrontendAgentWorkspaceTests.App_js_contains_agent_bootstrap_and_restore_flow` ✅
- `FrontendAgentWorkspaceTests.App_js_starts_agent_runs_from_natural_language_goal` ✅
- `FrontendAgentWorkspaceTests.App_js_renders_agent_plan_events_status_and_result` ✅
- `FrontendAgentWorkspaceTests.App_js_exposes_agent_approval_retry_and_cancel_actions` ✅
- `FrontendAgentWorkspaceTests.Styles_define_agent_workspace_layout_without_marketing_shell` ✅

## Manual Verification (浏览器人工验证证据)

- **服务访问 URL**: `http://localhost:5010`
- **人工实测结论**:
  - **界面完整性**: 顶部展示 Agent 入口，点击后以右侧侧边栏展示完整的 Agent Workspace，没有使用简化的表单。
  - **组件完备性**: 模型/沙箱/MCP 设置摘要可以正确展示，支持展示最多 30 条历史会话列表，提供自然语言输入框，并且计划、事件、审批和结果区域均存在且能够正常呈现运行过程。
  - **状态可恢复性**: 经历页面刷新后，能够完美恢复最近的 `Project context smoke` 会话、运行状态、历史事件和运行结果（彻底解决刷新后事件清空丢失的阻塞缺陷）。
  - **审批交互**: DATC64 翻译任务可以正常触发等待审批（WaitingForApproval），审批卡片中显示 Proposal JSON，并在批准后执行 Draft 写入。普通的 question 提问和 read-only-analysis 不会产生非法 overlay 写入。

## Changes Made

### Backend (Task 1)
- `AgentStore.ListThreadsAsync` — 按 UpdatedAt 降序列出线程
- `GET /api/agent/capabilities` — 暴露 AgentCapabilities.All
- `GET /api/agent/threads?take=N` — 列出最近线程

### Frontend (Tasks 2-7)
- `index.html` — 顶部 Agent 按钮 + 侧边栏面板（aside#agentWorkspace）
- `app.js` — state.agent + 完整生命周期管理
  - `loadAgentWorkspace` / `loadAgentSnapshot` — 加载与恢复（已修复刷新后通过拉取历史 events 恢复事件时间线的问题）
  - `startAgentRun` — 自然语言任务创建
  - `startAgentEventPolling` / `pollAgentEvents` — 事件轮询与状态自动拉取
  - `renderAgentSettings/Threads/Snapshot/RunStatus/Plan/Events/Approvals/Result` — 渲染
  - `approveAgentApproval` / `rejectAgentApproval` / `cancelAgentRun` / `retryAgentRun` — 交互
- `styles.css` — 侧边栏布局（展开时 4 栏 grid），可折叠面板

## Layout Design
- **侧边栏形式**（用户调整）：Agent 面板固定在右侧，展开时挤压中央编辑区
- 使用 CSS `:has()` 选择器自动切换 3 栏/4 栏 grid
- 不替换原有三栏工作台，用户可同时操作

## Non-Goals Confirmed
- ✅ No new arbitrary shell tool
- ✅ No direct frontend Codex/MCP call
- ✅ No write without approval (DATC64 仍走审批)
- ✅ DATC64 remains sample workflow, not the whole Agent
- ✅ No front-end project knowledge injection (由 Stage 2 project context service 管理)

## Known Gaps
- Agent 设置编辑入口（model/sandbox 修改）尚未暴露给用户界面
- 暂无 Playwright 自动化浏览器测试，全靠人工实测与静态前端 Assert 测试
