# POE Studio Agent Workspace Stage 3 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 在已完成的 Stage 1 MCP Tools、Stage 2 Codex Bridge Agent runtime 和项目认知层之上，交付 POE Studio 内置的 IDE-like Agent Workspace UI，让用户用自然语言提出目标，Agent 展示计划、工具调用、审批点、结果、失败原因和可恢复状态。

**架构：** Stage 3 只建设前端 Agent 工作台和必要的只读/列表型后端补口，不重写 Agent 内核，不新增任意脚本执行，不绕过 Stage 2 审批门禁。前端通过现有 `/api/agent/*` 线程、run、event、approval、settings API 消费 Stage 2 后端能力；如果缺少会话列表或能力列表，只补最小 API。DATC64 翻译是首个端到端验收样例，但 UI 必须支持 question、read-only-analysis、datc64-translation 三类 Stage 2 能力，保持全量项目助手方向。

**技术栈：** .NET 8、ASP.NET Core Minimal API、xUnit、原生 HTML/CSS/JavaScript、现有 `src/PoeStudio.Api/wwwroot` 静态前端、Stage 2 Agent API、Codex CLI JSONL 事件、POE Studio MCP 工具。

---

## 0. 固定硬约束

- [ ] **H0.1：目标锚定**
  Stage 3 的交付物是 Agent Workspace，不是 DATC64 表单，不是工具按钮集合，不是静态 prompt 输入框。用户必须可以只输入自然语言目标启动任务。

- [ ] **H0.2：能力边界**
  本阶段不新增自动写代码工具、不新增任意 shell 工具、不新增未审批的写入能力。写 overlay draft 仍只能通过 Stage 2 approval 记录批准后执行。

- [ ] **H0.3：消费现有 Agent runtime**
  前端必须调用 Stage 2 的 thread/run/event/approval/settings API。禁止绕过后端直接调用 Codex、MCP 或文件系统。

- [ ] **H0.4：可追溯**
  所有 UI 行为必须能追溯到 thread、run、plan、event、approval。页面刷新后，当前会话、设置、计划、事件、审批状态必须能恢复。

- [ ] **H0.5：不伪装成功**
  运行失败时必须展示错误码、错误信息、最近事件和重试入口。禁止只显示“失败”或静默回到空白状态。

- [ ] **H0.6：不扩大 token 成本**
  前端不直接灌入项目知识底座。项目知识仍由 Stage 2 project context service / `poe_get_project_context` 按预算注入和按需查询。

- [ ] **H0.7：计划与进度**
  执行者必须逐项更新本计划复选框。每个任务完成后提交一次 commit；偏离计划必须先补充计划说明。

- [ ] **H0.8：验收优先**
  每个任务必须先写失败测试或前端字符串级 smoke 断言，再实现最小代码。完成前必须跑定向测试和全量测试。

---

## 1. 当前代码事实

### 已存在后端能力

- `src/PoeStudio.Api/AgentRoutes.cs`
  - `GET /api/agent/settings`
  - `POST /api/agent/settings`
  - `POST /api/agent/threads`
  - `POST /api/agent/threads/{threadId}/messages`
  - `GET /api/agent/threads/{threadId}`
  - `POST /api/agent/runs`
  - `POST /api/agent/runs/{runId}/retry`
  - `GET /api/agent/runs/{runId}`
  - `GET /api/agent/runs/{runId}/events`
  - `POST /api/agent/runs/{runId}/cancel`
  - `POST /api/agent/approvals/{approvalId}/approve`
  - `POST /api/agent/approvals/{approvalId}/reject`

- `src/PoeStudio.Contracts/AgentDtos.cs`
  已包含 settings、thread、message、run、event、plan、capability、approval DTO。

- `src/PoeStudio.Storage/Agent/AgentStore.cs`
  已持久化 settings、thread、messages、runs、plan、events、approvals，但目前缺少显式 `ListThreadsAsync`。

- `src/PoeStudio.Core/Agent/AgentCapabilities.cs`
  已定义 `question`、`read-only-analysis`、`datc64-translation` 能力，但目前缺少公开给前端的 capabilities API。

### 已存在前端事实

- `src/PoeStudio.Api/wwwroot/index.html`
  当前是三栏工作台：左侧资源、中央资源编辑、右侧状态/草稿/批处理/输出/工具。尚无 Agent Workspace。

- `src/PoeStudio.Api/wwwroot/app.js`
  单文件状态管理，无构建链。所有新增 Agent UI 必须遵循现有 `state`、`api()`、事件绑定和渲染函数风格。

- `src/PoeStudio.Api/wwwroot/styles.css`
  现有设计偏操作台风格。Agent UI 必须是工作界面，避免营销式 hero、卡片堆叠和装饰背景。

---

## 2. 目标用户体验

- [ ] 用户打开 POE Studio 后能看到明确的 `Agent` 入口。
- [ ] 用户进入 Agent Workspace 后，能看到：
  - 当前模型/沙箱/工作目录设置摘要。
  - 会话列表和当前会话。
  - 自然语言任务输入框。
  - 任务类型选择，但默认允许“自动/通用问题”，不要强迫用户理解内部 taskKind。
  - 运行计划面板。
  - 事件时间线，能看到 Codex 输出、MCP 工具调用、审批请求、失败原因。
  - 审批面板，能批准或拒绝。
  - 结果面板，显示最终回答或写入结果。
  - 失败/取消/重试入口。
- [ ] 刷新页面后恢复最近 Agent 会话和设置。
- [ ] DATC64 翻译样例可从自然语言目标启动，等待审批，批准后写入 draft。
- [ ] 普通问题和只读分析不会产生审批或 overlay 写入。

---

## 3. 文件结构

### 必须修改

- `src/PoeStudio.Contracts/AgentDtos.cs`
  - 新增会话列表响应 DTO，必要时新增 capability list 响应 DTO。

- `src/PoeStudio.Storage/Agent/AgentStore.cs`
  - 新增 `ListThreadsAsync(int take, CancellationToken cancellationToken)`。

- `src/PoeStudio.Api/AgentRoutes.cs`
  - 新增 `GET /api/agent/threads?take=30`。
  - 新增 `GET /api/agent/capabilities`。

- `src/PoeStudio.Api/wwwroot/index.html`
  - 新增 Agent 主工作区入口和 Agent Workspace DOM。

- `src/PoeStudio.Api/wwwroot/app.js`
  - 新增 agent state、settings 加载、thread list、snapshot 恢复、run 创建、event polling、approval approve/reject、retry/cancel、渲染函数和事件绑定。

- `src/PoeStudio.Api/wwwroot/styles.css`
  - 新增 Agent Workspace 布局样式，保持现有操作台风格。

- `tests/PoeStudio.Tests/AgentApiSmokeTests.cs`
  - 补充 capabilities 和 thread list API smoke。

- `tests/PoeStudio.Tests/AgentStoreTests.cs`
  - 补充 `ListThreadsAsync` 持久化与排序测试。

- `tests/PoeStudio.Tests/FrontendAgentWorkspaceTests.cs`
  - 新增前端静态 smoke 测试，验证关键 DOM、函数、API 字符串和刷新恢复逻辑存在。

### 禁止修改

- 禁止修改 `src/PoeStudio.Core/Tables/TableInspector.cs`。
- 禁止修改 Native writer、patch writer 或 overlay store 的写入语义。
- 禁止新增第三方前端构建依赖。
- 禁止新增独立 SPA 框架。

---

## 4. 任务 1：后端补齐 Agent Workspace 必需列表 API

**文件：**
- 修改：`src/PoeStudio.Contracts/AgentDtos.cs`
- 修改：`src/PoeStudio.Storage/Agent/AgentStore.cs`
- 修改：`src/PoeStudio.Api/AgentRoutes.cs`
- 修改：`tests/PoeStudio.Tests/AgentStoreTests.cs`
- 修改：`tests/PoeStudio.Tests/AgentApiSmokeTests.cs`

- [ ] **步骤 1：运行影响分析**

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentStoreTests|FullyQualifiedName~AgentApiSmokeTests"
```

再通过 GitNexus 检查：

```text
mcp__gitnexus__impact({
  "repo": "POE-Studio",
  "target": "AgentStore",
  "direction": "upstream",
  "includeTests": true
})
```

记录风险。若 GitNexus 返回 HIGH 或 CRITICAL，先停下报告。

- [ ] **步骤 2：编写失败测试：AgentStore 列出线程**

在 `AgentStoreTests.cs` 新增测试：

```csharp
[Fact]
public async Task ListThreadsAsync_returns_recent_threads_ordered_by_updated_at()
{
    using var workspace = new TemporaryWorkspace();
    var store = new AgentStore(workspace.Root);
    var older = await store.SaveNewThreadAsync("profile-1", "Older", "Goal A", "question", CancellationToken.None);
    await Task.Delay(5);
    var newer = await store.SaveNewThreadAsync("profile-1", "Newer", "Goal B", "read-only-analysis", CancellationToken.None);

    var threads = await store.ListThreadsAsync(10, CancellationToken.None);

    Assert.Equal([newer.Id, older.Id], threads.Select(x => x.Id).ToArray());
}
```

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentStoreTests
```

预期：FAIL，`ListThreadsAsync` 不存在。

- [ ] **步骤 3：实现 `ListThreadsAsync`**

在 `AgentStore` 中新增：

```csharp
public async Task<IReadOnlyList<AgentThreadDto>> ListThreadsAsync(int take, CancellationToken cancellationToken)
{
    var threadsRoot = Path.Combine(AgentRoot, "threads");
    if (!Directory.Exists(threadsRoot))
    {
        return [];
    }

    var limit = Math.Clamp(take, 1, 100);
    var threads = new List<AgentThreadDto>();
    foreach (var path in Directory.EnumerateFiles(threadsRoot, "thread.json", SearchOption.AllDirectories))
    {
        var thread = await ReadJsonAsync<AgentThreadDto>(path, cancellationToken);
        if (thread is not null)
        {
            threads.Add(thread);
        }
    }

    return threads
        .OrderByDescending(x => x.UpdatedAt)
        .ThenByDescending(x => x.CreatedAt)
        .Take(limit)
        .ToArray();
}
```

- [ ] **步骤 4：编写失败测试：API 暴露 capabilities 和 thread list**

在 `AgentApiSmokeTests.cs` 新增：

```csharp
[Fact]
public async Task Agent_workspace_bootstrap_apis_return_capabilities_and_recent_threads()
{
    var client = _factory.CreateClient();
    var thread = await CreateThreadAsync(client, "question");

    var capabilities = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<AgentCapabilityDto>>>("/api/agent/capabilities");
    var threads = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<AgentThreadDto>>>("/api/agent/threads?take=20");

    Assert.True(capabilities!.Ok);
    Assert.Contains(capabilities.Data!, x => x.TaskKind == "question");
    Assert.Contains(capabilities.Data!, x => x.TaskKind == "read-only-analysis");
    Assert.Contains(capabilities.Data!, x => x.TaskKind == "datc64-translation" && x.RequiresApproval);
    Assert.True(threads!.Ok);
    Assert.Contains(threads.Data!, x => x.Id == thread.Id);
}
```

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~AgentApiSmokeTests
```

预期：FAIL，路由不存在。

- [ ] **步骤 5：实现 API**

在 `AgentRoutes.MapAgentRoutes` 中新增：

```csharp
app.MapGet("/api/agent/capabilities", () =>
{
    return ApiResponse<IReadOnlyList<AgentCapabilityDto>>.Success(AgentCapabilities.All);
});

app.MapGet("/api/agent/threads", async (int? take, AgentStore store, CancellationToken cancellationToken) =>
{
    var threads = await store.ListThreadsAsync(take ?? 30, cancellationToken);
    return ApiResponse<IReadOnlyList<AgentThreadDto>>.Success(threads);
});
```

若 `AgentCapabilities.All` 当前不存在，则在 `AgentCapabilities` 新增只读集合：

```csharp
public static IReadOnlyList<AgentCapabilityDto> All => Capabilities;
```

- [ ] **步骤 6：运行定向测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentStoreTests|FullyQualifiedName~AgentApiSmokeTests"
```

预期：PASS。

- [ ] **步骤 7：Commit**

```powershell
git add src\PoeStudio.Contracts\AgentDtos.cs src\PoeStudio.Storage\Agent\AgentStore.cs src\PoeStudio.Api\AgentRoutes.cs tests\PoeStudio.Tests\AgentStoreTests.cs tests\PoeStudio.Tests\AgentApiSmokeTests.cs
git commit -m "feat(agent): expose workspace bootstrap APIs"
```

---

## 5. 任务 2：新增 Agent Workspace DOM 与入口

**文件：**
- 修改：`src/PoeStudio.Api/wwwroot/index.html`
- 创建：`tests/PoeStudio.Tests/FrontendAgentWorkspaceTests.cs`

- [ ] **步骤 1：编写失败测试**

新增 `FrontendAgentWorkspaceTests.cs`：

```csharp
namespace PoeStudio.Tests;

public sealed class FrontendAgentWorkspaceTests
{
    [Fact]
    public async Task Index_contains_agent_workspace_shell()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "index.html"));

        Assert.Contains("id=\"openAgentWorkspaceBtn\"", html);
        Assert.Contains("id=\"agentWorkspace\"", html);
        Assert.Contains("id=\"agentThreadList\"", html);
        Assert.Contains("id=\"agentGoalInput\"", html);
        Assert.Contains("id=\"agentRunBtn\"", html);
        Assert.Contains("id=\"agentPlanList\"", html);
        Assert.Contains("id=\"agentEventTimeline\"", html);
        Assert.Contains("id=\"agentApprovalsPanel\"", html);
        Assert.Contains("id=\"agentResultPanel\"", html);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PoeStudio.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
```

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~FrontendAgentWorkspaceTests
```

预期：FAIL，DOM 不存在。

- [ ] **步骤 2：添加顶部入口**

在 `index.html` 的 `.top-actions` 中加入：

```html
<button id="openAgentWorkspaceBtn" type="button">Agent</button>
```

- [ ] **步骤 3：添加 Agent Workspace 主区域**

在 `main.workbench-shell` 内，现有三栏之后或中央工作区旁新增同级隐藏区域：

```html
<section id="agentWorkspace" class="panel agent-workspace hidden" aria-label="Agent 工作台">
  <div class="agent-layout">
    <aside class="agent-sidebar">
      <div class="agent-section-head">
        <h2>Agent</h2>
        <button id="agentNewThreadBtn" type="button">新任务</button>
      </div>
      <div id="agentSettingsSummary" class="agent-settings-summary">未加载设置</div>
      <div id="agentThreadList" class="agent-thread-list"></div>
    </aside>

    <section class="agent-main">
      <div class="agent-run-composer">
        <select id="agentTaskKindSelect" aria-label="任务类型">
          <option value="question">自动/提问</option>
          <option value="read-only-analysis">只读分析</option>
          <option value="datc64-translation">DATC64 翻译</option>
        </select>
        <textarea id="agentGoalInput" spellcheck="false" placeholder="告诉 Agent 你要完成什么"></textarea>
        <input id="agentResourcePathInput" type="text" spellcheck="false" placeholder="可选：资源路径，例如 data/balance/traditional chinese/activeskills.datc64">
        <div class="agent-run-actions">
          <button id="agentRunBtn" type="button">运行</button>
          <button id="agentCancelRunBtn" type="button" disabled>取消</button>
          <button id="agentRetryRunBtn" type="button" disabled>重试</button>
        </div>
      </div>

      <div class="agent-status-row">
        <span id="agentCurrentThreadStatus">未选择会话</span>
        <span id="agentCurrentRunStatus">未运行</span>
      </div>

      <div class="agent-work-panels">
        <section class="agent-panel">
          <h3>计划</h3>
          <div id="agentPlanList" class="agent-plan-list"></div>
        </section>
        <section class="agent-panel">
          <h3>事件</h3>
          <div id="agentEventTimeline" class="agent-event-timeline"></div>
        </section>
        <section class="agent-panel">
          <h3>审批</h3>
          <div id="agentApprovalsPanel" class="agent-approvals-panel"></div>
        </section>
        <section class="agent-panel">
          <h3>结果</h3>
          <pre id="agentResultPanel" class="agent-result-panel"></pre>
        </section>
      </div>
    </section>
  </div>
</section>
```

实现时可以按现有三栏布局调整位置，但必须保留上述 id。

- [ ] **步骤 4：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~FrontendAgentWorkspaceTests
```

预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src\PoeStudio.Api\wwwroot\index.html tests\PoeStudio.Tests\FrontendAgentWorkspaceTests.cs
git commit -m "feat(agent): add workspace shell"
```

---

## 6. 任务 3：前端 Agent 状态、启动加载和会话恢复

**文件：**
- 修改：`src/PoeStudio.Api/wwwroot/app.js`
- 修改：`tests/PoeStudio.Tests/FrontendAgentWorkspaceTests.cs`

- [ ] **步骤 1：编写失败测试**

在 `FrontendAgentWorkspaceTests.cs` 新增：

```csharp
[Fact]
public async Task App_js_contains_agent_bootstrap_and_restore_flow()
{
    var js = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "app.js"));

    Assert.Contains("agent:", js);
    Assert.Contains("loadAgentWorkspace", js);
    Assert.Contains("/api/agent/settings", js);
    Assert.Contains("/api/agent/capabilities", js);
    Assert.Contains("/api/agent/threads?take=", js);
    Assert.Contains("localStorage.getItem(\"poeStudioAgentThreadId\")", js);
    Assert.Contains("renderAgentThreads", js);
    Assert.Contains("renderAgentSnapshot", js);
}
```

运行：

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~FrontendAgentWorkspaceTests
```

预期：FAIL。

- [ ] **步骤 2：扩展 `state`**

在 `state` 中新增：

```javascript
agent: {
  visible: false,
  settings: null,
  capabilities: [],
  threads: [],
  currentThreadId: localStorage.getItem("poeStudioAgentThreadId") || null,
  snapshot: null,
  currentRun: null,
  eventTimer: null,
  lastEventSequence: 0
}
```

- [ ] **步骤 3：新增加载函数**

在 `app.js` 中新增：

```javascript
async function loadAgentWorkspace() {
  state.agent.settings = await api("/api/agent/settings");
  state.agent.capabilities = await api("/api/agent/capabilities");
  state.agent.threads = await api("/api/agent/threads?take=30");
  if (!state.agent.currentThreadId && state.agent.threads.length > 0) {
    state.agent.currentThreadId = state.agent.threads[0].id;
  }
  renderAgentSettings();
  renderAgentThreads();
  if (state.agent.currentThreadId) {
    await loadAgentSnapshot(state.agent.currentThreadId);
  } else {
    renderAgentSnapshot(null);
  }
}
```

并新增：

```javascript
async function loadAgentSnapshot(threadId) {
  const snapshot = await api(`/api/agent/threads/${encodeURIComponent(threadId)}`);
  state.agent.currentThreadId = threadId;
  state.agent.snapshot = snapshot;
  state.agent.currentRun = snapshot.recentRuns?.[0] || null;
  localStorage.setItem("poeStudioAgentThreadId", threadId);
  renderAgentThreads();
  renderAgentSnapshot(snapshot);
  startAgentEventPolling();
}
```

- [ ] **步骤 4：新增渲染占位函数**

新增 `renderAgentSettings`、`renderAgentThreads`、`renderAgentSnapshot`。空状态必须显示真实状态，不得空白：

```javascript
function renderAgentSettings() {
  const settings = state.agent.settings;
  $("agentSettingsSummary").textContent = settings
    ? `${settings.model || "默认模型"} · ${settings.sandbox} · ${settings.mcpServerName}`
    : "未加载设置";
}
```

- [ ] **步骤 5：绑定入口**

在初始化事件绑定区域加入：

```javascript
$("openAgentWorkspaceBtn").addEventListener("click", async () => {
  state.agent.visible = true;
  $("agentWorkspace").classList.remove("hidden");
  await loadAgentWorkspace();
});
```

如果实现者选择隐藏原有中央编辑区来进入 Agent 模式，必须提供返回资源工作台按钮；不得让用户失去原工作流。

- [ ] **步骤 6：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~FrontendAgentWorkspaceTests
```

预期：PASS。

- [ ] **步骤 7：Commit**

```powershell
git add src\PoeStudio.Api\wwwroot\app.js tests\PoeStudio.Tests\FrontendAgentWorkspaceTests.cs
git commit -m "feat(agent): restore workspace sessions"
```

---

## 7. 任务 4：自然语言任务创建与运行

**文件：**
- 修改：`src/PoeStudio.Api/wwwroot/app.js`
- 修改：`tests/PoeStudio.Tests/FrontendAgentWorkspaceTests.cs`

- [ ] **步骤 1：编写失败测试**

新增：

```csharp
[Fact]
public async Task App_js_starts_agent_runs_from_natural_language_goal()
{
    var js = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "app.js"));

    Assert.Contains("startAgentRun", js);
    Assert.Contains("/api/agent/threads", js);
    Assert.Contains("/api/agent/runs", js);
    Assert.Contains("agentGoalInput", js);
    Assert.Contains("agentTaskKindSelect", js);
    Assert.Contains("agentResourcePathInput", js);
    Assert.Contains("datc64-translation", js);
}
```

- [ ] **步骤 2：实现 `startAgentRun`**

新增：

```javascript
async function startAgentRun() {
  const goal = $("agentGoalInput").value.trim();
  if (!goal) {
    setStatus("请输入 Agent 任务目标");
    return;
  }

  const taskKind = $("agentTaskKindSelect").value || "question";
  const profileId = targetProfileId();
  const resourcePath = $("agentResourcePathInput").value.trim() || state.selectedResource?.virtualPath || null;
  const threadTitle = goal.length > 32 ? `${goal.slice(0, 32)}...` : goal;

  let thread = state.agent.snapshot?.thread || null;
  if (!thread || thread.taskKind !== taskKind) {
    thread = await api("/api/agent/threads", {
      profileId,
      title: threadTitle,
      goal,
      taskKind
    });
    state.agent.currentThreadId = thread.id;
    localStorage.setItem("poeStudioAgentThreadId", thread.id);
  }

  await api(`/api/agent/threads/${encodeURIComponent(thread.id)}/messages`, {
    content: goal,
    attachments: resourcePath ? [resourcePath] : null
  });

  const run = await api("/api/agent/runs", {
    threadId: thread.id,
    profileId,
    goal,
    taskKind,
    resourcePath: taskKind === "datc64-translation" ? resourcePath : null
  });

  state.agent.currentRun = run;
  state.agent.lastEventSequence = 0;
  await loadAgentSnapshot(thread.id);
}
```

注意：DATC64 任务没有 resourcePath 时，后端会返回 `resource_path_required`。前端必须把错误展示给用户，不得吞掉。

- [ ] **步骤 3：绑定运行按钮**

初始化区域新增：

```javascript
$("agentRunBtn").addEventListener("click", () => startAgentRun().catch((error) => setStatus(error.message)));
```

- [ ] **步骤 4：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~FrontendAgentWorkspaceTests
```

预期：PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src\PoeStudio.Api\wwwroot\app.js tests\PoeStudio.Tests\FrontendAgentWorkspaceTests.cs
git commit -m "feat(agent): start runs from natural language goals"
```

---

## 8. 任务 5：事件轮询、计划进度和结果展示

**文件：**
- 修改：`src/PoeStudio.Api/wwwroot/app.js`
- 修改：`tests/PoeStudio.Tests/FrontendAgentWorkspaceTests.cs`

- [ ] **步骤 1：编写失败测试**

新增：

```csharp
[Fact]
public async Task App_js_renders_agent_plan_events_status_and_result()
{
    var js = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "app.js"));

    Assert.Contains("startAgentEventPolling", js);
    Assert.Contains("/api/agent/runs/${encodeURIComponent(run.id)}/events", js);
    Assert.Contains("renderAgentPlan", js);
    Assert.Contains("renderAgentEvents", js);
    Assert.Contains("renderAgentRunStatus", js);
    Assert.Contains("agentResultPanel", js);
    Assert.Contains("Project context loaded", js);
}
```

- [ ] **步骤 2：实现事件轮询**

新增：

```javascript
function startAgentEventPolling() {
  if (state.agent.eventTimer) {
    clearInterval(state.agent.eventTimer);
    state.agent.eventTimer = null;
  }

  const run = state.agent.currentRun;
  if (!run || ["Succeeded", "Failed", "Cancelled", "Rejected"].includes(agentRunStatusText(run.status))) {
    return;
  }

  state.agent.eventTimer = setInterval(() => {
    pollAgentEvents().catch((error) => {
      setStatus(`Agent 事件刷新失败：${error.message}`);
    });
  }, 1200);
}
```

如果当前 DTO 序列化为数字状态，则实现 `agentRunStatusText(status)` 同时支持数字和字符串。

- [ ] **步骤 3：实现 `pollAgentEvents`**

```javascript
async function pollAgentEvents() {
  const run = state.agent.currentRun;
  if (!run) return;
  const events = await api(`/api/agent/runs/${encodeURIComponent(run.id)}/events?afterSequence=${state.agent.lastEventSequence || 0}`);
  if (events.length > 0) {
    state.agent.lastEventSequence = Math.max(...events.map((event) => event.sequence || 0));
    state.agent.snapshot = {
      ...state.agent.snapshot,
      events: [...(state.agent.snapshot?.events || []), ...events]
    };
    renderAgentEvents(events, { append: true });
  }
  const freshRun = await api(`/api/agent/runs/${encodeURIComponent(run.id)}`);
  state.agent.currentRun = freshRun;
  renderAgentRunStatus(freshRun);
  if ([2, 3, 4, 5, 6].includes(freshRun.status)) {
    await loadAgentSnapshot(freshRun.threadId);
  }
}
```

- [ ] **步骤 4：渲染计划和结果**

`renderAgentSnapshot(snapshot)` 必须调用：

```javascript
renderAgentRunStatus(snapshot?.recentRuns?.[0] || null);
renderAgentPlan(snapshot?.latestPlan || []);
renderAgentEvents([]);
renderAgentApprovals(snapshot?.pendingApprovals || []);
renderAgentResult(snapshot?.recentRuns?.[0] || null);
```

结果面板显示：

```javascript
function renderAgentResult(run) {
  $("agentResultPanel").textContent = run
    ? (run.resultJson || run.errorMessage || run.message || "暂无结果")
    : "暂无运行结果";
}
```

- [ ] **步骤 5：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~FrontendAgentWorkspaceTests
```

预期：PASS。

- [ ] **步骤 6：Commit**

```powershell
git add src\PoeStudio.Api\wwwroot\app.js tests\PoeStudio.Tests\FrontendAgentWorkspaceTests.cs
git commit -m "feat(agent): render run progress and events"
```

---

## 9. 任务 6：审批、拒绝、取消和重试交互

**文件：**
- 修改：`src/PoeStudio.Api/wwwroot/app.js`
- 修改：`tests/PoeStudio.Tests/FrontendAgentWorkspaceTests.cs`

- [ ] **步骤 1：编写失败测试**

新增：

```csharp
[Fact]
public async Task App_js_exposes_agent_approval_retry_and_cancel_actions()
{
    var js = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "app.js"));

    Assert.Contains("renderAgentApprovals", js);
    Assert.Contains("/api/agent/approvals/${encodeURIComponent(approvalId)}/approve", js);
    Assert.Contains("/api/agent/approvals/${encodeURIComponent(approvalId)}/reject", js);
    Assert.Contains("/api/agent/runs/${encodeURIComponent(run.id)}/cancel", js);
    Assert.Contains("/api/agent/runs/${encodeURIComponent(run.id)}/retry", js);
    Assert.Contains("approval.proposalJson", js);
}
```

- [ ] **步骤 2：实现审批渲染**

```javascript
function renderAgentApprovals(approvals) {
  const panel = $("agentApprovalsPanel");
  if (!approvals || approvals.length === 0) {
    panel.innerHTML = '<div class="agent-empty">没有待审批操作</div>';
    return;
  }

  panel.innerHTML = approvals.map((approval) => `
    <article class="agent-approval" data-approval-id="${escapeHtml(approval.id)}">
      <div class="agent-approval-head">
        <strong>${escapeHtml(approval.kind)}</strong>
        <span>${escapeHtml(approval.status)}</span>
      </div>
      <p>${escapeHtml(approval.summary || "需要审批")}</p>
      <pre>${escapeHtml(approval.proposalJson || "")}</pre>
      <div class="agent-approval-actions">
        <button type="button" data-agent-approve="${escapeHtml(approval.id)}">批准</button>
        <button type="button" data-agent-reject="${escapeHtml(approval.id)}">拒绝</button>
      </div>
    </article>
  `).join("");
}
```

- [ ] **步骤 3：实现审批动作**

```javascript
async function approveAgentApproval(approvalId) {
  await api(`/api/agent/approvals/${encodeURIComponent(approvalId)}/approve`, {});
  if (state.agent.currentThreadId) {
    await loadAgentSnapshot(state.agent.currentThreadId);
  }
}

async function rejectAgentApproval(approvalId) {
  await api(`/api/agent/approvals/${encodeURIComponent(approvalId)}/reject`, {});
  if (state.agent.currentThreadId) {
    await loadAgentSnapshot(state.agent.currentThreadId);
  }
}
```

- [ ] **步骤 4：实现取消和重试**

```javascript
async function cancelAgentRun() {
  const run = state.agent.currentRun;
  if (!run) return;
  await api(`/api/agent/runs/${encodeURIComponent(run.id)}/cancel`, {});
  await loadAgentSnapshot(run.threadId);
}

async function retryAgentRun() {
  const run = state.agent.currentRun;
  if (!run) return;
  const retry = await api(`/api/agent/runs/${encodeURIComponent(run.id)}/retry`, {});
  state.agent.currentRun = retry;
  state.agent.lastEventSequence = 0;
  await loadAgentSnapshot(retry.threadId);
}
```

- [ ] **步骤 5：事件委托绑定**

给 `agentWorkspace` 绑定 click 事件，处理 `data-agent-approve` 和 `data-agent-reject`。

- [ ] **步骤 6：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~FrontendAgentWorkspaceTests
```

预期：PASS。

- [ ] **步骤 7：Commit**

```powershell
git add src\PoeStudio.Api\wwwroot\app.js tests\PoeStudio.Tests\FrontendAgentWorkspaceTests.cs
git commit -m "feat(agent): support approvals retry and cancellation"
```

---

## 10. 任务 7：Agent Workspace 样式和响应式工作台体验

**文件：**
- 修改：`src/PoeStudio.Api/wwwroot/styles.css`
- 修改：`tests/PoeStudio.Tests/FrontendAgentWorkspaceTests.cs`

- [ ] **步骤 1：编写失败测试**

新增：

```csharp
[Fact]
public async Task Styles_define_agent_workspace_layout_without_marketing_shell()
{
    var css = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "styles.css"));

    Assert.Contains(".agent-workspace", css);
    Assert.Contains(".agent-layout", css);
    Assert.Contains(".agent-sidebar", css);
    Assert.Contains(".agent-main", css);
    Assert.Contains(".agent-event-timeline", css);
    Assert.Contains(".agent-approval", css);
    Assert.DoesNotContain("agent-hero", css, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **步骤 2：新增布局样式**

在 `styles.css` 中新增：

```css
.agent-workspace {
  grid-column: 1 / -1;
  min-height: 0;
}

.agent-layout {
  display: grid;
  grid-template-columns: 280px minmax(0, 1fr);
  gap: 10px;
  min-height: 0;
  height: 100%;
}

.agent-sidebar,
.agent-main,
.agent-panel {
  min-height: 0;
}

.agent-sidebar {
  display: flex;
  flex-direction: column;
  gap: 8px;
  border-right: 1px solid var(--border);
  padding-right: 10px;
}

.agent-main {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.agent-run-composer {
  display: grid;
  grid-template-columns: 180px minmax(0, 1fr);
  gap: 8px;
}

.agent-run-composer textarea {
  min-height: 84px;
  resize: vertical;
}

.agent-work-panels {
  display: grid;
  grid-template-columns: minmax(260px, 0.8fr) minmax(320px, 1.2fr);
  gap: 10px;
  min-height: 0;
  flex: 1;
}

.agent-panel {
  display: flex;
  flex-direction: column;
  gap: 8px;
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 10px;
  background: var(--surface-bg);
  overflow: auto;
}

.agent-event-timeline,
.agent-plan-list,
.agent-approvals-panel,
.agent-result-panel {
  min-height: 120px;
  overflow: auto;
}
```

实现者可以微调，但不得做大 hero、营销卡片或装饰背景。

- [ ] **步骤 3：运行测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter FullyQualifiedName~FrontendAgentWorkspaceTests
```

预期：PASS。

- [ ] **步骤 4：Commit**

```powershell
git add src\PoeStudio.Api\wwwroot\styles.css tests\PoeStudio.Tests\FrontendAgentWorkspaceTests.cs
git commit -m "feat(agent): style workspace interface"
```

---

## 11. 任务 8：端到端 smoke 与验收报告

**文件：**
- 创建：`docs/superpowers/reports/2026-05-23-poe-agent-workspace-stage3-acceptance.md`
- 修改：`tests/PoeStudio.Tests/AgentApiSmokeTests.cs`
- 修改：`tests/PoeStudio.Tests/FrontendAgentWorkspaceTests.cs`

- [ ] **步骤 1：运行定向测试**

```powershell
dotnet test tests\PoeStudio.Tests\PoeStudio.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentApiSmokeTests|FullyQualifiedName~AgentStoreTests|FullyQualifiedName~FrontendAgentWorkspaceTests"
```

预期：PASS。

- [ ] **步骤 2：运行全量测试**

```powershell
dotnet test PoeStudio.sln --no-restore
```

预期：PASS。

如果出现 `PoeStudio.Mcp.exe` 锁定 DLL，先确认是否是本地残留 MCP 进程：

```powershell
Get-Process PoeStudio.Mcp -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,Path
```

只允许停止确认属于本仓库 `src\PoeStudio.Mcp\bin\Debug\net8.0\PoeStudio.Mcp.exe` 的残留测试进程，然后重跑测试。

- [ ] **步骤 3：启动本地服务**

```powershell
dotnet run --project src\PoeStudio.Api\PoeStudio.Api.csproj --urls http://localhost:5010
```

如果 5010 已占用，使用 5011，并在验收报告记录实际 URL。

- [ ] **步骤 4：浏览器人工验收**

打开：

```text
http://localhost:5010
```

人工检查：

- 顶部有 Agent 入口。
- 点击 Agent 后出现完整工作台，不是小表单。
- 模型/沙箱/MCP 设置摘要能显示。
- 会话列表能显示历史线程。
- 输入自然语言目标可以创建 thread 和 run。
- 事件时间线能显示 project context、Codex 输出或 MCP 工具调用。
- DATC64 翻译任务能进入 waiting approval。
- 审批卡能显示 proposal JSON。
- 拒绝后状态可恢复；批准后写入 overlay draft。
- 刷新页面后仍能恢复当前会话和审批/结果。

- [ ] **步骤 5：可选 Playwright 验证**

如果当前环境有 Playwright，执行至少一个浏览器 smoke：

```powershell
npx playwright test --config playwright.config.ts --grep "Agent"
```

如果项目没有 Playwright 配置，不新增依赖；改用人工浏览器验收截图或记录。

- [ ] **步骤 6：GitNexus 变更检查**

```text
mcp__gitnexus__detect_changes({
  "repo": "POE-Studio",
  "scope": "all"
})
```

若风险 HIGH 或 CRITICAL，验收报告必须解释原因和是否符合预期。

- [ ] **步骤 7：写验收报告**

创建 `docs/superpowers/reports/2026-05-23-poe-agent-workspace-stage3-acceptance.md`，内容必须包含：

```markdown
# POE Studio Agent Workspace Stage 3 Acceptance

Stage 3 status: PASS 或 FAIL

## Scope
- Agent Workspace UI
- Thread/run/event/approval/settings consumption
- Natural language task entry

## Test Results
- <命令>: <结果>

## Manual Verification
- <URL>
- <逐项结果>

## Non-Goals Confirmed
- No new arbitrary shell tool
- No direct frontend Codex/MCP call
- No write without approval
- DATC64 remains sample workflow, not the whole Agent

## Known Gaps
- <仍未解决的限制>
```

- [ ] **步骤 8：Commit**

```powershell
git add docs\superpowers\reports\2026-05-23-poe-agent-workspace-stage3-acceptance.md
git commit -m "docs(agent): record workspace stage3 acceptance"
```

---

## 12. Stage 3 完成判定

只有同时满足以下条件，才允许标记 Stage 3 PASS：

- [ ] `dotnet test PoeStudio.sln --no-restore` 通过。
- [ ] `AgentApiSmokeTests` 通过。
- [ ] `FrontendAgentWorkspaceTests` 通过。
- [ ] 前端存在 Agent Workspace，而不是小表单入口。
- [ ] 用户可用自然语言创建并运行 Agent 任务。
- [ ] 计划、事件、审批、结果和失败原因都可见。
- [ ] 刷新后能恢复当前会话。
- [ ] DATC64 翻译样例仍走审批后写入 draft。
- [ ] 普通 question/read-only-analysis 不写 overlay。
- [ ] 验收报告明确记录 PASS / FAIL 和证据。

---

## 13. 跑偏防线

- [ ] **R1：如果实现者只加一个 prompt 表单，立即停止。**
- [ ] **R2：如果实现者绕过 `/api/agent/*` 直接调用 Codex 或 MCP，立即停止。**
- [ ] **R3：如果实现者新增任意脚本执行能力，立即停止。**
- [ ] **R4：如果实现者把 DATC64 写死成唯一工作流，立即停止。**
- [ ] **R5：如果实现者隐藏失败原因或工具调用，立即停止。**
- [ ] **R6：如果刷新后会话/设置丢失，Stage 3 不能 PASS。**
- [ ] **R7：如果批准前写入 overlay，Stage 3 不能 PASS。**

---

## 14. 自检记录

- [ ] 覆盖用户总目标：自然语言全量项目助手。
- [ ] 覆盖当前阶段：Stage 3 IDE-like Agent Workspace UI。
- [ ] 保持 Stage 2 后端 Agent runtime 边界，不重写内核。
- [ ] 保持项目知识底座按预算注入，不前端全文灌入。
- [ ] 保持写入审批门禁。
- [ ] 覆盖刷新恢复、进度跟踪、事件可见、失败可恢复。
- [ ] 覆盖 DATC64 作为样例而非唯一能力。
- [ ] 覆盖测试与验收报告。
